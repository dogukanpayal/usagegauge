using System;
using System.Threading;
using System.Threading.Tasks;
using UsageGauge.Models;
using UsageGauge.Providers;

namespace UsageGauge.Services;

/// <summary>
/// ChatGPT ve Antigravity sağlayıcılarının bağımsız otomatik yenileme,
/// üstel geri çekilme (exponential backoff) ve sıfırlanma zamanı döngülerini yöneten zamanlayıcı.
/// </summary>
public sealed class QuotaScheduler : IDisposable
{
    private readonly IChatGptProvider _chatGptProvider;
    private readonly IAntigravityProvider _antigravityProvider;
    private readonly Func<DateTimeOffset> _utcNow;

    private readonly SemaphoreSlim _chatGptLock = new(1, 1);
    private readonly SemaphoreSlim _antigravityLock = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly object _taskLock = new();
    private readonly HashSet<Task> _inFlightTasks = new();

    private int _chatGptConsecutiveErrors;
    private int _antigravityConsecutiveErrors;

    private DateTimeOffset _nextChatGptRefreshUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _nextAntigravityRefreshUtc = DateTimeOffset.MinValue;

    // Her sıfırlanma zaman damgası için yalnızca tek bir erken sorgu yapılması için takip
    private DateTimeOffset? _lastChatGptHandledExpiredResetUtc;
    private DateTimeOffset? _lastAntigravityHandledExpiredResetUtc;

    private bool _isChatGptRefreshing;
    private bool _isAntigravityRefreshing;
    private bool _isAntigravityHalted;

    private ChatGptQuotaResult? _lastChatGptResult;
    private AntigravityQuotaResult? _lastAntigravityResult;

    private bool _isDisposed;

    public ChatGptQuotaResult? LastChatGptResult => _lastChatGptResult;
    public AntigravityQuotaResult? LastAntigravityResult => _lastAntigravityResult;

    public bool IsChatGptRefreshing => _isChatGptRefreshing;
    public bool IsAntigravityRefreshing => _isAntigravityRefreshing;
    public bool IsAnyRefreshing => _isChatGptRefreshing || _isAntigravityRefreshing;
    public bool IsAntigravityHalted => _isAntigravityHalted;

    public int ChatGptConsecutiveErrors => _chatGptConsecutiveErrors;
    public int AntigravityConsecutiveErrors => _antigravityConsecutiveErrors;

    public DateTimeOffset NextChatGptRefreshUtc => _nextChatGptRefreshUtc;
    public DateTimeOffset NextAntigravityRefreshUtc => _nextAntigravityRefreshUtc;
    public DateTimeOffset? LastSuccessfulCheckUtc { get; private set; }

    public event Action<ChatGptQuotaResult>? ChatGptUpdated;
    public event Action<AntigravityQuotaResult>? AntigravityUpdated;
    public event Action<bool>? RefreshStateChanged;

    public QuotaScheduler(
        IChatGptProvider? chatGptProvider = null,
        IAntigravityProvider? antigravityProvider = null,
        Func<DateTimeOffset>? utcNowProvider = null)
    {
        _chatGptProvider = chatGptProvider ?? new ChatGptProvider();
        _antigravityProvider = antigravityProvider ?? new AntigravityProvider();
        _utcNow = utcNowProvider ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Hata sayısına göre bekleme aralığını hesaplar:
    /// 1. hata: 10 dk, 2. hata: 15 dk, 3. hata: 20 dk, 4+ hata: 30 dk.
    /// </summary>
    public static TimeSpan GetBackoffInterval(int consecutiveErrors)
    {
        return consecutiveErrors switch
        {
            <= 1 => TimeSpan.FromMinutes(10),
            2 => TimeSpan.FromMinutes(15),
            3 => TimeSpan.FromMinutes(20),
            _ => TimeSpan.FromMinutes(30)
        };
    }

    /// <summary>
    /// Her iki sağlayıcıyı paralel olarak sorgular.
    /// Biri yanıt verdiğinde diğerini beklemeden kartını günceller.
    /// </summary>
    public async Task RefreshAllAsync(bool isManual = false, CancellationToken cancellationToken = default)
    {
        Task<ChatGptQuotaResult> chatTask = RefreshChatGptAsync(isManual, cancellationToken);
        Task<AntigravityQuotaResult> agyTask = RefreshAntigravityAsync(isManual, cancellationToken);

        await Task.WhenAll(chatTask, agyTask);
    }

    private void TrackTask(Task task)
    {
        lock (_taskLock)
        {
            if (_isDisposed) return;
            _inFlightTasks.Add(task);
        }

        _ = task.ContinueWith(t =>
        {
            lock (_taskLock)
            {
                _inFlightTasks.Remove(t);
            }
            if (t.IsFaulted && t.Exception != null)
            {
                // Unobserved task exception koruması
                _ = t.Exception;
            }
        }, TaskScheduler.Default);
    }

    public Task<ChatGptQuotaResult> RefreshChatGptAsync(bool isManual = false, CancellationToken cancellationToken = default)
    {
        Task<ChatGptQuotaResult> task = RefreshChatGptInternalAsync(isManual, cancellationToken);
        TrackTask(task);
        return task;
    }

    private async Task<ChatGptQuotaResult> RefreshChatGptInternalAsync(bool isManual, CancellationToken cancellationToken)
    {
        if (_isDisposed)
        {
            return _lastChatGptResult ?? ChatGptQuotaResult.CreateError(ProviderStatus.Timeout, "Uygulama kapatıldı.");
        }

        CancellationTokenSource? linkedCts = null;
        CancellationToken effectiveToken;
        try
        {
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
            effectiveToken = linkedCts.Token;
        }
        catch (ObjectDisposedException)
        {
            return _lastChatGptResult ?? ChatGptQuotaResult.CreateError(ProviderStatus.Timeout, "Uygulama kapatıldı.");
        }

        // Mükerrer sorgu koruması: Halihazırda bir sorgu devam ediyorsa yenisini başlatma
        bool acquired = false;
        try
        {
            acquired = await _chatGptLock.WaitAsync(0, effectiveToken);
        }
        catch (OperationCanceledException)
        {
            linkedCts.Dispose();
            return _lastChatGptResult ?? ChatGptQuotaResult.CreateError(ProviderStatus.Timeout, "İşlem iptal edildi.");
        }

        if (!acquired)
        {
            linkedCts.Dispose();
            return _lastChatGptResult ?? ChatGptQuotaResult.CreateError(ProviderStatus.Timeout, "Sorgu zaten devam ediyor.");
        }

        try
        {
            _isChatGptRefreshing = true;
            RefreshStateChanged?.Invoke(IsAnyRefreshing);

            ChatGptQuotaResult fresh = await _chatGptProvider.FetchQuotaAsync(effectiveToken);

            // Stale data koruması: Yeni sorgu hata aldıysa ve önceki geçerli veri varsa bayat olarak sakla
            if (!fresh.IsSuccess && _lastChatGptResult != null && _lastChatGptResult.CodexGroup.HasUsableData)
            {
                var staleGroup = _lastChatGptResult.CodexGroup.ToStale(fresh.Status, fresh.ErrorMessage);
                fresh = new ChatGptQuotaResult(staleGroup, fresh.Status, fresh.ErrorMessage, fresh.FetchedAtUtc);
            }

            if (fresh.IsSuccess)
            {
                _chatGptConsecutiveErrors = 0;
                LastSuccessfulCheckUtc = _utcNow();
            }
            else
            {
                _chatGptConsecutiveErrors++;
            }

            _nextChatGptRefreshUtc = _utcNow() + GetBackoffInterval(_chatGptConsecutiveErrors);
            _lastChatGptResult = fresh;
            ChatGptUpdated?.Invoke(fresh);
            return fresh;
        }
        catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested || _isDisposed)
        {
            return _lastChatGptResult ?? ChatGptQuotaResult.CreateError(ProviderStatus.Timeout, "Uygulama kapatıldı.");
        }
        finally
        {
            linkedCts.Dispose();
            _isChatGptRefreshing = false;
            RefreshStateChanged?.Invoke(IsAnyRefreshing);
            if (acquired)
            {
                try
                {
                    _chatGptLock.Release();
                }
                catch (ObjectDisposedException) { }
                catch (SemaphoreFullException) { }
            }
        }
    }

    public Task<AntigravityQuotaResult> RefreshAntigravityAsync(bool isManual = false, CancellationToken cancellationToken = default)
    {
        Task<AntigravityQuotaResult> task = RefreshAntigravityInternalAsync(isManual, cancellationToken);
        TrackTask(task);
        return task;
    }

    private async Task<AntigravityQuotaResult> RefreshAntigravityInternalAsync(bool isManual, CancellationToken cancellationToken)
    {
        if (_isDisposed)
        {
            return _lastAntigravityResult ?? AntigravityQuotaResult.CreateError(ProviderStatus.Timeout, "Uygulama kapatıldı.");
        }

        if (_isAntigravityHalted)
        {
            // Model turn detected güvenlik kuralı: Sağlayıcıya YENİ ÇAĞRI YAPILMAZ!
            var haltedResult = _lastAntigravityResult ?? AntigravityQuotaResult.CreateError(
                ProviderStatus.ModelTurnDetected,
                "Model turu veya token harcaması tespit edildi; otomatik sorgular güvenlik gereği durduruldu.");
            AntigravityUpdated?.Invoke(haltedResult);
            return haltedResult;
        }

        CancellationTokenSource? linkedCts = null;
        CancellationToken effectiveToken;
        try
        {
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
            effectiveToken = linkedCts.Token;
        }
        catch (ObjectDisposedException)
        {
            return _lastAntigravityResult ?? AntigravityQuotaResult.CreateError(ProviderStatus.Timeout, "Uygulama kapatıldı.");
        }

        bool acquired = false;
        try
        {
            acquired = await _antigravityLock.WaitAsync(0, effectiveToken);
        }
        catch (OperationCanceledException)
        {
            linkedCts.Dispose();
            return _lastAntigravityResult ?? AntigravityQuotaResult.CreateError(ProviderStatus.Timeout, "İşlem iptal edildi.");
        }

        if (!acquired)
        {
            linkedCts.Dispose();
            return _lastAntigravityResult ?? AntigravityQuotaResult.CreateError(ProviderStatus.Timeout, "Sorgu zaten devam ediyor.");
        }

        try
        {
            _isAntigravityRefreshing = true;
            RefreshStateChanged?.Invoke(IsAnyRefreshing);

            AntigravityQuotaResult fresh = await _antigravityProvider.FetchQuotaAsync(effectiveToken);

            if (fresh.Status == ProviderStatus.ModelTurnDetected)
            {
                _isAntigravityHalted = true;
                _antigravityConsecutiveErrors++;
                _lastAntigravityResult = fresh;
                AntigravityUpdated?.Invoke(fresh);
                return fresh;
            }

            // Kısmi başarı ve bayat veri koruması
            if (_lastAntigravityResult != null)
            {
                QuotaGroup gemini = fresh.GeminiGroup;
                if (!fresh.GeminiGroup.IsSuccess && _lastAntigravityResult.GeminiGroup.HasUsableData)
                {
                    gemini = _lastAntigravityResult.GeminiGroup.ToStale(fresh.GeminiGroup.Status, fresh.GeminiGroup.ErrorMessage);
                }

                QuotaGroup tp = fresh.ThirdPartyGroup;
                if (!fresh.ThirdPartyGroup.IsSuccess && _lastAntigravityResult.ThirdPartyGroup.HasUsableData)
                {
                    tp = _lastAntigravityResult.ThirdPartyGroup.ToStale(fresh.ThirdPartyGroup.Status, fresh.ThirdPartyGroup.ErrorMessage);
                }

                fresh = new AntigravityQuotaResult(
                    gemini,
                    tp,
                    fresh.Status,
                    fresh.NumTurns,
                    fresh.TotalTokens,
                    fresh.ErrorMessage,
                    fresh.FetchedAtUtc);
            }

            if (fresh.IsSuccess || fresh.Status == ProviderStatus.PartialSuccess)
            {
                _antigravityConsecutiveErrors = 0;
                LastSuccessfulCheckUtc = _utcNow();
            }
            else
            {
                _antigravityConsecutiveErrors++;
            }

            _nextAntigravityRefreshUtc = _utcNow() + GetBackoffInterval(_antigravityConsecutiveErrors);
            _lastAntigravityResult = fresh;
            AntigravityUpdated?.Invoke(fresh);
            return fresh;
        }
        catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested || _isDisposed)
        {
            return _lastAntigravityResult ?? AntigravityQuotaResult.CreateError(ProviderStatus.Timeout, "Uygulama kapatıldı.");
        }
        finally
        {
            linkedCts.Dispose();
            _isAntigravityRefreshing = false;
            RefreshStateChanged?.Invoke(IsAnyRefreshing);
            if (acquired)
            {
                try
                {
                    _antigravityLock.Release();
                }
                catch (ObjectDisposedException) { }
                catch (SemaphoreFullException) { }
            }
        }
    }

    /// <summary>
    /// Periyodik zamanlayıcı tarafından her saniye tetiklenir.
    /// Vakti gelen otomatik sorguları veya süresi dolan kotaların tek seferlik kontrolünü başlatır.
    /// </summary>
    public void Tick(DateTimeOffset nowUtc)
    {
        if (_isDisposed) return;

        // 1. ChatGPT planlı otomatik sorgu kontrolü
        if (!_isChatGptRefreshing && _nextChatGptRefreshUtc != DateTimeOffset.MinValue && nowUtc >= _nextChatGptRefreshUtc)
        {
            _ = RefreshChatGptAsync(isManual: false);
        }

        // 2. Antigravity planlı otomatik sorgu kontrolü
        if (!_isAntigravityRefreshing && !_isAntigravityHalted && _nextAntigravityRefreshUtc != DateTimeOffset.MinValue && nowUtc >= _nextAntigravityRefreshUtc)
        {
            _ = RefreshAntigravityAsync(isManual: false);
        }

        // 3. Sıfırlanma zamanı dolan pencereler için tek seferlik erken kontrol
        CheckResetTimeExpired(nowUtc);
    }

    private void CheckResetTimeExpired(DateTimeOffset nowUtc)
    {
        // ChatGPT 5h veya haftalık sıfırlanma zamanı dolduysa
        if (!_isChatGptRefreshing && _lastChatGptResult != null)
        {
            var g = _lastChatGptResult.CodexGroup;
            DateTimeOffset? expiredTarget = null;
            if (g.FiveHourLimit != null && g.FiveHourLimit.ResetsAtUtc <= nowUtc)
            {
                expiredTarget = g.FiveHourLimit.ResetsAtUtc;
            }
            else if (g.WeeklyLimit != null && g.WeeklyLimit.ResetsAtUtc <= nowUtc)
            {
                expiredTarget = g.WeeklyLimit.ResetsAtUtc;
            }

            if (expiredTarget.HasValue)
            {
                // Bu spesifik sıfırlanma zaman damgası için henüz erken sorgu yapılmadıysa tek seferlik tetikle
                if (_lastChatGptHandledExpiredResetUtc != expiredTarget.Value)
                {
                    _lastChatGptHandledExpiredResetUtc = expiredTarget.Value;
                    _ = RefreshChatGptAsync(isManual: false);
                }
                // Başarısız olursa veya geçmiş zaman dönmeye devam ederse _nextChatGptRefreshUtc takvimine uyulur
            }
        }

        // Antigravity sıfırlanma zamanı dolduysa
        if (!_isAntigravityRefreshing && !_isAntigravityHalted && _lastAntigravityResult != null)
        {
            var g1 = _lastAntigravityResult.GeminiGroup;
            var g2 = _lastAntigravityResult.ThirdPartyGroup;
            DateTimeOffset? expiredTarget = null;

            if (g1.FiveHourLimit != null && g1.FiveHourLimit.ResetsAtUtc <= nowUtc)
                expiredTarget = g1.FiveHourLimit.ResetsAtUtc;
            else if (g1.WeeklyLimit != null && g1.WeeklyLimit.ResetsAtUtc <= nowUtc)
                expiredTarget = g1.WeeklyLimit.ResetsAtUtc;
            else if (g2.FiveHourLimit != null && g2.FiveHourLimit.ResetsAtUtc <= nowUtc)
                expiredTarget = g2.FiveHourLimit.ResetsAtUtc;
            else if (g2.WeeklyLimit != null && g2.WeeklyLimit.ResetsAtUtc <= nowUtc)
                expiredTarget = g2.WeeklyLimit.ResetsAtUtc;

            if (expiredTarget.HasValue)
            {
                if (_lastAntigravityHandledExpiredResetUtc != expiredTarget.Value)
                {
                    _lastAntigravityHandledExpiredResetUtc = expiredTarget.Value;
                    _ = RefreshAntigravityAsync(isManual: false);
                }
            }
        }
    }

    /// <summary>
    /// Devam eden görevleri izler, iptal sinyali gönderir ve belirtilen süre boyunca
    /// temiz şekilde sonlanmalarını bekleyip hatalarını gözlemler.
    /// </summary>
    public async Task<bool> DisposeAsync(TimeSpan timeout = default)
    {
        if (timeout == default)
        {
            timeout = TimeSpan.FromSeconds(3);
        }

        if (_isDisposed)
        {
            return true;
        }

        _isDisposed = true;

        try
        {
            _disposeCts.Cancel();
        }
        catch (ObjectDisposedException) { }

        Task[] tasksToWait;
        lock (_taskLock)
        {
            tasksToWait = _inFlightTasks.ToArray();
        }

        bool allFinished = true;
        if (tasksToWait.Length > 0)
        {
            try
            {
                Task all = Task.WhenAll(tasksToWait);
                Task completed = await Task.WhenAny(all, Task.Delay(timeout));
                if (completed != all)
                {
                    allFinished = false;
                }
                else
                {
                    await all;
                }
            }
            catch
            {
                // İstisnalar (iptal vb.) güvenle gözlemlendi
            }
        }

        try
        {
            _disposeCts.Dispose();
        }
        catch { }

        return allFinished;
    }

    /// <summary>
    /// UI thread'ini kilitlemeden iptal sinyali gönderir, arka planda çalışan görevlerin
    /// sonlanmasını izler, hatalarını güvenle gözlemler ve kaynakları serbest bırakır.
    /// </summary>
    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            try
            {
                _disposeCts.Cancel();
            }
            catch (ObjectDisposedException) { }

            Task[] tasksToWait;
            lock (_taskLock)
            {
                tasksToWait = _inFlightTasks.ToArray();
            }

            if (tasksToWait.Length > 0)
            {
                // UI thread'ini kilitlemeden arka planda devam eden görevlerin bitmesini bekle,
                // hatalarını güvenle gözlemle ve ardından kaynakları temizle
                _ = Task.WhenAll(tasksToWait).ContinueWith(t =>
                {
                    if (t.IsFaulted && t.Exception != null)
                    {
                        _ = t.Exception;
                    }
                    try
                    {
                        _disposeCts.Dispose();
                    }
                    catch { }
                }, TaskScheduler.Default);
            }
            else
            {
                try
                {
                    _disposeCts.Dispose();
                }
                catch { }
            }
        }
    }
}
