using System;
using System.Threading;
using System.Threading.Tasks;
using UsageGauge.Models;
using UsageGauge.Providers;
using UsageGauge.Services;
using Xunit;

namespace UsageGauge.Tests.Services;

public class QuotaSchedulerTests
{
    private sealed class StubChatGptProvider : IChatGptProvider
    {
        public Func<CancellationToken, Task<ChatGptQuotaResult>> Handler { get; set; }
        public int CallCount { get; private set; }

        public StubChatGptProvider(Func<Task<ChatGptQuotaResult>> handler)
        {
            Handler = _ => handler();
        }

        public StubChatGptProvider(Func<CancellationToken, Task<ChatGptQuotaResult>> handler)
        {
            Handler = handler;
        }

        public Task<ChatGptQuotaResult> FetchQuotaAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Handler(cancellationToken);
        }
    }

    private sealed class StubAntigravityProvider : IAntigravityProvider
    {
        public Func<CancellationToken, Task<AntigravityQuotaResult>> Handler { get; set; }
        public int CallCount { get; private set; }

        public StubAntigravityProvider(Func<Task<AntigravityQuotaResult>> handler)
        {
            Handler = _ => handler();
        }

        public StubAntigravityProvider(Func<CancellationToken, Task<AntigravityQuotaResult>> handler)
        {
            Handler = handler;
        }

        public Task<AntigravityQuotaResult> FetchQuotaAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Handler(cancellationToken);
        }
    }

    private static ChatGptQuotaResult CreateValidChatGptResult(
        double fiveHourPercent = 85.0,
        double weeklyPercent = 70.0,
        DateTimeOffset? fiveHourResetUtc = null,
        DateTimeOffset? weeklyResetUtc = null)
    {
        var fiveHour = new QuotaWindow("codex-5h", "5 Saatlik", 300, fiveHourPercent, fiveHourResetUtc ?? DateTimeOffset.UtcNow.AddHours(4));
        var weekly = new QuotaWindow("codex-weekly", "Haftalık", 10080, weeklyPercent, weeklyResetUtc ?? DateTimeOffset.UtcNow.AddDays(5));
        var group = new QuotaGroup("chatgpt-codex", "ChatGPT Work / Codex", fiveHour, weekly, ProviderStatus.Success);
        return new ChatGptQuotaResult(group, ProviderStatus.Success);
    }

    private static AntigravityQuotaResult CreateValidAntigravityResult()
    {
        var g5h = new QuotaWindow("gemini-5h", "5 Saatlik", 300, 90.0, DateTimeOffset.UtcNow.AddHours(3));
        var gWeekly = new QuotaWindow("gemini-weekly", "Haftalık", 10080, 75.0, DateTimeOffset.UtcNow.AddDays(4));
        var gemini = new QuotaGroup("antigravity-gemini", "Antigravity Gemini", g5h, gWeekly, ProviderStatus.Success);

        var p5h = new QuotaWindow("3p-5h", "5 Saatlik", 300, 100.0, DateTimeOffset.UtcNow.AddHours(5));
        var pWeekly = new QuotaWindow("3p-weekly", "Haftalık", 10080, 95.0, DateTimeOffset.UtcNow.AddDays(6));
        var tp = new QuotaGroup("antigravity-3p", "Antigravity Claude / GPT", p5h, pWeekly, ProviderStatus.Success);

        return new AntigravityQuotaResult(gemini, tp, ProviderStatus.Success, numTurns: 0, totalTokens: 0);
    }

    [Fact]
    public void GetBackoffInterval_FollowsDefinedSequenceStrictly()
    {
        Assert.Equal(TimeSpan.FromMinutes(10), QuotaScheduler.GetBackoffInterval(0));
        Assert.Equal(TimeSpan.FromMinutes(10), QuotaScheduler.GetBackoffInterval(1));
        Assert.Equal(TimeSpan.FromMinutes(15), QuotaScheduler.GetBackoffInterval(2));
        Assert.Equal(TimeSpan.FromMinutes(20), QuotaScheduler.GetBackoffInterval(3));
        Assert.Equal(TimeSpan.FromMinutes(30), QuotaScheduler.GetBackoffInterval(4));
        Assert.Equal(TimeSpan.FromMinutes(30), QuotaScheduler.GetBackoffInterval(10));
    }

    [Fact]
    public async Task RefreshChatGptAsync_ConsecutiveErrors_IncrementsBackoff_AndResetsOnSuccess()
    {
        int call = 0;
        var chatGptStub = new StubChatGptProvider(() =>
        {
            call++;
            if (call <= 3)
            {
                return Task.FromResult(ChatGptQuotaResult.CreateError(ProviderStatus.Timeout, "Zaman aşımı"));
            }
            return Task.FromResult(CreateValidChatGptResult());
        });

        using var scheduler = new QuotaScheduler(chatGptStub, new StubAntigravityProvider(() => Task.FromResult(CreateValidAntigravityResult())));

        // 1. Hata -> 1 hata, aralık 10 dk
        await scheduler.RefreshChatGptAsync();
        Assert.Equal(1, scheduler.ChatGptConsecutiveErrors);

        // 2. Hata -> 2 hata, aralık 15 dk
        await scheduler.RefreshChatGptAsync();
        Assert.Equal(2, scheduler.ChatGptConsecutiveErrors);

        // 3. Hata -> 3 hata, aralık 20 dk
        await scheduler.RefreshChatGptAsync();
        Assert.Equal(3, scheduler.ChatGptConsecutiveErrors);

        // 4. Çağrı Başarılı -> 0 hata, aralık 10 dk'ya sıfırlanır
        await scheduler.RefreshChatGptAsync();
        Assert.Equal(0, scheduler.ChatGptConsecutiveErrors);
        Assert.NotNull(scheduler.LastSuccessfulCheckUtc);
    }

    [Fact]
    public async Task RefreshChatGptAsync_WhenCalledConcurrently_PreventsDuplicateExecution()
    {
        var tcs = new TaskCompletionSource<ChatGptQuotaResult>();
        var chatGptStub = new StubChatGptProvider(() => tcs.Task);

        using var scheduler = new QuotaScheduler(chatGptStub, new StubAntigravityProvider(() => Task.FromResult(CreateValidAntigravityResult())));

        Task task1 = scheduler.RefreshChatGptAsync();
        Task task2 = scheduler.RefreshChatGptAsync(); // 2. eşzamanlı çağrı

        tcs.SetResult(CreateValidChatGptResult());
        await Task.WhenAll(task1, task2);

        // Sağlayıcı kilit mekanizması sayesinde yalnızca 1 kez tetiklenmeli
        Assert.Equal(1, chatGptStub.CallCount);
    }

    [Fact]
    public async Task RefreshChatGptAsync_WhenFailureOccurs_PreservesPreviousDataAsStale()
    {
        int call = 0;
        var chatGptStub = new StubChatGptProvider(() =>
        {
            call++;
            if (call == 1) return Task.FromResult(CreateValidChatGptResult(85.0, 70.0));
            return Task.FromResult(ChatGptQuotaResult.CreateError(ProviderStatus.Timeout, "Zaman aşımı"));
        });

        using var scheduler = new QuotaScheduler(chatGptStub, new StubAntigravityProvider(() => Task.FromResult(CreateValidAntigravityResult())));

        // 1. Başarılı okuma
        ChatGptQuotaResult res1 = await scheduler.RefreshChatGptAsync();
        Assert.True(res1.IsSuccess);
        Assert.False(res1.CodexGroup.IsStale);

        // 2. Başarısız okuma -> Önceki veriler IsStale=true olarak korunmalı
        ChatGptQuotaResult res2 = await scheduler.RefreshChatGptAsync();
        Assert.False(res2.IsSuccess);
        Assert.Equal(ProviderStatus.Timeout, res2.Status);
        Assert.True(res2.CodexGroup.IsStale);
        Assert.Equal(85.0, res2.CodexGroup.FiveHourLimit!.RemainingPercent);
        Assert.Equal(70.0, res2.CodexGroup.WeeklyLimit!.RemainingPercent);
    }

    [Fact]
    public async Task RefreshAntigravityAsync_WhenModelTurnDetected_HaltsFutureRefreshes()
    {
        int call = 0;
        var agyStub = new StubAntigravityProvider(() =>
        {
            call++;
            if (call == 1)
            {
                return Task.FromResult(AntigravityQuotaResult.CreateError(
                    ProviderStatus.ModelTurnDetected,
                    "Model turu algılandı",
                    numTurns: 1,
                    totalTokens: 15));
            }
            return Task.FromResult(CreateValidAntigravityResult());
        });

        using var scheduler = new QuotaScheduler(new StubChatGptProvider(() => Task.FromResult(CreateValidChatGptResult())), agyStub);

        // 1. ModelTurnDetected döner
        AntigravityQuotaResult res1 = await scheduler.RefreshAntigravityAsync();
        Assert.Equal(ProviderStatus.ModelTurnDetected, res1.Status);
        Assert.True(scheduler.IsAntigravityHalted);

        // 2. Bir sonraki yenileme çağrısında sağlayıcı ASLA tetiklenmemeli
        AntigravityQuotaResult res2 = await scheduler.RefreshAntigravityAsync();
        Assert.Equal(ProviderStatus.ModelTurnDetected, res2.Status);
        Assert.Equal(1, agyStub.CallCount); // Sağlayıcı 2. kez çağrılmadı!
    }

    [Fact]
    public async Task RefreshAntigravityAsync_WithPartialFailure_PreservesOnlyFailedGroupAsStale()
    {
        int call = 0;
        var agyStub = new StubAntigravityProvider(() =>
        {
            call++;
            if (call == 1) return Task.FromResult(CreateValidAntigravityResult());

            // 2. çağrıda Gemini başarılı (%95), Claude/GPT grubu ise hata alıyor
            var g5h = new QuotaWindow("gemini-5h", "5 Saatlik", 300, 95.0, DateTimeOffset.UtcNow.AddHours(2));
            var gWeekly = new QuotaWindow("gemini-weekly", "Haftalık", 10080, 80.0, DateTimeOffset.UtcNow.AddDays(3));
            var gemini = new QuotaGroup("antigravity-gemini", "Antigravity Gemini", g5h, gWeekly, ProviderStatus.Success);
            var tpError = QuotaGroup.CreateError("antigravity-3p", "Antigravity Claude / GPT", ProviderStatus.InvalidResponse, "Havuz eksik");

            return Task.FromResult(new AntigravityQuotaResult(gemini, tpError, ProviderStatus.PartialSuccess));
        });

        using var scheduler = new QuotaScheduler(new StubChatGptProvider(() => Task.FromResult(CreateValidChatGptResult())), agyStub);

        // 1. Başarılı okuma
        await scheduler.RefreshAntigravityAsync();

        // 2. Kısmi okuma
        AntigravityQuotaResult res2 = await scheduler.RefreshAntigravityAsync();

        Assert.Equal(ProviderStatus.PartialSuccess, res2.Status);
        // Gemini taze ve güncel
        Assert.False(res2.GeminiGroup.IsStale);
        Assert.Equal(95.0, res2.GeminiGroup.FiveHourLimit!.RemainingPercent);
        // Claude/GPT ise eski veri olarak korundu
        Assert.True(res2.ThirdPartyGroup.IsStale);
        Assert.Equal(100.0, res2.ThirdPartyGroup.FiveHourLimit!.RemainingPercent);
    }

    [Fact]
    public async Task Tick_WhenQuotaExpires_FiresExactlyOneImmediateQuery_AndFollowsBackoffOnFailure()
    {
        DateTimeOffset t0 = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset currentTime = t0;
        DateTimeOffset targetReset = t0.AddSeconds(5); // 5 saniye sonra sıfırlanacak

        int fetchCount = 0;
        var chatGptStub = new StubChatGptProvider(() =>
        {
            fetchCount++;
            if (fetchCount == 1)
            {
                // İlk açılış sorgusu: 5 sn sonra sıfırlanacak kota döner
                return Task.FromResult(CreateValidChatGptResult(
                    fiveHourPercent: 10.0,
                    weeklyPercent: 50.0,
                    fiveHourResetUtc: targetReset));
            }
            // Sıfırlanma zamanı dolunca tetiklenen sorgu: hata döner
            return Task.FromResult(ChatGptQuotaResult.CreateError(ProviderStatus.Timeout, "Ağ zaman aşımı"));
        });

        using var scheduler = new QuotaScheduler(
            chatGptStub,
            new StubAntigravityProvider(() => Task.FromResult(CreateValidAntigravityResult())),
            () => currentTime);

        // 1. Açılış sorgusu
        await scheduler.RefreshChatGptAsync();
        Assert.Equal(1, fetchCount);

        // 2. Henüz süre dolmadı (t0 + 2s)
        currentTime = t0.AddSeconds(2);
        scheduler.Tick(currentTime);
        Assert.Equal(1, fetchCount); // Yeni sorgu yok

        // 3. Süre doldu (t0 + 6s) -> Tek seferlik erken sorgu tetiklenir ve hata alır
        currentTime = t0.AddSeconds(6);
        scheduler.Tick(currentTime);
        Assert.Equal(2, fetchCount); // Erken sorgu tetiklendi

        // 4. Sonraki her saniyede tick atılsa bile mükerrer sorgu olmamalı (backoff 10 dk beklenir)
        for (int s = 7; s <= 30; s++)
        {
            currentTime = t0.AddSeconds(s);
            scheduler.Tick(currentTime);
        }
        Assert.Equal(2, fetchCount); // Kesinlikle 2 olarak kalmalı; saniyelik döngü oluşmamalı!

        // 5. 10 dakikalık backoff dolmadan önce (t0 + 9 dk 59 sn)
        currentTime = t0.AddMinutes(9).AddSeconds(59);
        scheduler.Tick(currentTime);
        Assert.Equal(2, fetchCount);

        // 6. 10 dakikalık backoff dolunca (t0 + 10 dk 10 sn) planlı tekrar sorgusu çalışır
        currentTime = t0.AddMinutes(10).AddSeconds(10);
        scheduler.Tick(currentTime);
        Assert.Equal(3, fetchCount);
    }

    [Fact]
    public async Task Tick_WhenSuccessfulResponseReturnsPastResetTime_DoesNotLoopEverySecond()
    {
        DateTimeOffset t0 = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset currentTime = t0;
        DateTimeOffset pastReset = t0.AddSeconds(-10); // Sunucu saat farkı nedeniyle geçmiş sıfırlanma zamanı

        int fetchCount = 0;
        var chatGptStub = new StubChatGptProvider(() =>
        {
            fetchCount++;
            return Task.FromResult(CreateValidChatGptResult(
                fiveHourPercent: 90.0,
                weeklyPercent: 80.0,
                fiveHourResetUtc: pastReset));
        });

        using var scheduler = new QuotaScheduler(
            chatGptStub,
            new StubAntigravityProvider(() => Task.FromResult(CreateValidAntigravityResult())),
            () => currentTime);

        // İlk sorgu
        await scheduler.RefreshChatGptAsync();
        Assert.Equal(1, fetchCount);

        // Sıfırlanma zamanı geçmişte olduğu için tek seferlik kontrol tetiklenir
        scheduler.Tick(currentTime);
        Assert.Equal(2, fetchCount);

        // Sunucu hâlâ geçmiş bir reset zamanı dönse bile her saniye tekrar sorgulamamalı!
        for (int s = 1; s <= 20; s++)
        {
            currentTime = t0.AddSeconds(s);
            scheduler.Tick(currentTime);
        }
        Assert.Equal(2, fetchCount);
    }

    [Fact]
    public async Task QuotaScheduler_DisposeDuringRunningQuery_CancelsCleanlyWithoutObjectDisposedException()
    {
        var slowChatGptStub = new StubChatGptProvider(async token =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), token);
            return CreateValidChatGptResult();
        });

        var scheduler = new QuotaScheduler(slowChatGptStub, new StubAntigravityProvider(() => Task.FromResult(CreateValidAntigravityResult())));

        // Sorguyu arka planda başlat
        Task<ChatGptQuotaResult> refreshTask = scheduler.RefreshChatGptAsync();

        // Sorgu çalışırken derhal Dispose çağır
        await Task.Delay(50);
        scheduler.Dispose();

        // Görev ObjectDisposedException fırlatmadan güvenle sonlanmalı
        ChatGptQuotaResult result = await refreshTask;
        Assert.NotNull(result);
        Assert.Equal(ProviderStatus.Timeout, result.Status);
    }

    [Fact]
    public async Task DisposeAsync_WhenProviderDelaysCancellation_WaitsCleanlyAndObservesErrors()
    {
        var tcsStarted = new TaskCompletionSource<bool>();
        var tcsFinished = new TaskCompletionSource<bool>();

        var delayedChatGptStub = new StubChatGptProvider(async token =>
        {
            tcsStarted.SetResult(true);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), token);
            }
            catch (OperationCanceledException)
            {
                // İptali geciktiren sağlayıcı simülasyonu (temizlik veya soket kapatma beklemesi)
                await Task.Delay(150);
                tcsFinished.SetResult(true);
                throw;
            }
            return CreateValidChatGptResult();
        });

        var scheduler = new QuotaScheduler(delayedChatGptStub, new StubAntigravityProvider(() => Task.FromResult(CreateValidAntigravityResult())));

        Task queryTask = scheduler.RefreshChatGptAsync();

        // Sağlayıcının çalışmaya başladığından emin ol
        await tcsStarted.Task;
        Assert.True(scheduler.IsChatGptRefreshing);

        // Kapatma başlatılıyor - geciken sağlayıcının iptal sürecini tamamlamasını bekler
        bool clean = await scheduler.DisposeAsync(TimeSpan.FromSeconds(2));

        Assert.True(clean);
        Assert.True(tcsFinished.Task.IsCompletedSuccessfully);
        Assert.False(scheduler.IsChatGptRefreshing);
    }

    [Fact]
    public async Task Dispose_WhenProviderDelaysCancellation_DoesNotBlockUIAndObservesTaskInBackground()
    {
        var tcsStarted = new TaskCompletionSource<bool>();
        var tcsFinished = new TaskCompletionSource<bool>();

        var delayedChatGptStub = new StubChatGptProvider(async token =>
        {
            tcsStarted.SetResult(true);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), token);
            }
            catch (OperationCanceledException)
            {
                await Task.Delay(100);
                tcsFinished.SetResult(true);
                throw;
            }
            return CreateValidChatGptResult();
        });

        var scheduler = new QuotaScheduler(delayedChatGptStub, new StubAntigravityProvider(() => Task.FromResult(CreateValidAntigravityResult())));

        Task queryTask = scheduler.RefreshChatGptAsync();
        await tcsStarted.Task;

        // Senkron dispose UI thread'ini kilitlemeden derhal döner
        var sw = System.Diagnostics.Stopwatch.StartNew();
        scheduler.Dispose();
        sw.Stop();

        // Dispose çağrısının kendisi 100 ms'lik gecikmeyi senkron beklemez (< 50 ms)
        Assert.True(sw.ElapsedMilliseconds < 50);

        // Arka plandaki görev ise güvenle tamamlanır ve hatası gözlemlenir
        await tcsFinished.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(tcsFinished.Task.IsCompletedSuccessfully);
    }
}
