using System;
using System.Threading;
using System.Threading.Tasks;
using UsageGauge.Models;
using UsageGauge.Providers;
using UsageGauge.Services;
using UsageGauge.ViewModels;
using Xunit;

namespace UsageGauge.Tests.ViewModels;

public class MainWindowViewModelTests
{
    private sealed class StubChatGptProvider : IChatGptProvider
    {
        public int CallCount { get; private set; }
        public TaskCompletionSource<ChatGptQuotaResult>? DelayTcs { get; set; }

        public async Task<ChatGptQuotaResult> FetchQuotaAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (DelayTcs != null)
            {
                return await DelayTcs.Task;
            }

            var fiveHour = new QuotaWindow("codex-5h", "5 Saatlik", 300, 85.0, DateTimeOffset.UtcNow.AddHours(4));
            var weekly = new QuotaWindow("codex-weekly", "Haftalık", 10080, 70.0, DateTimeOffset.UtcNow.AddDays(5));
            var group = new QuotaGroup("chatgpt-codex", "ChatGPT Work / Codex", fiveHour, weekly, ProviderStatus.Success);
            return new ChatGptQuotaResult(group, ProviderStatus.Success);
        }
    }

    private sealed class StubAntigravityProvider : IAntigravityProvider
    {
        public int CallCount { get; private set; }
        public bool ReturnModelTurnDetected { get; set; } = true;

        public Task<AntigravityQuotaResult> FetchQuotaAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (ReturnModelTurnDetected)
            {
                return Task.FromResult(AntigravityQuotaResult.CreateError(
                    ProviderStatus.ModelTurnDetected,
                    "Model turu algılandı",
                    numTurns: 1,
                    totalTokens: 10));
            }

            var g5h = new QuotaWindow("gemini-5h", "5 Saatlik", 300, 90.0, DateTimeOffset.UtcNow.AddHours(3));
            var gWeekly = new QuotaWindow("gemini-weekly", "Haftalık", 10080, 75.0, DateTimeOffset.UtcNow.AddDays(4));
            var gemini = new QuotaGroup("antigravity-gemini", "Antigravity Gemini", g5h, gWeekly, ProviderStatus.Success);
            var tp = new QuotaGroup("antigravity-3p", "Antigravity Claude / GPT", g5h, gWeekly, ProviderStatus.Success);

            return Task.FromResult(new AntigravityQuotaResult(gemini, tp, ProviderStatus.Success, 0, 0));
        }
    }

    [Fact]
    public async Task TriggerManualRefreshAsync_WhenAntigravityHalted_OnlyRefreshesChatGpt_AndKeepsHaltedStatusVisible()
    {
        var chatGptStub = new StubChatGptProvider();
        var agyStub = new StubAntigravityProvider { ReturnModelTurnDetected = true };

        using var scheduler = new QuotaScheduler(chatGptStub, agyStub);
        using var viewModel = new MainWindowViewModel(scheduler);

        // 1. İlk açılış sorgusu: Antigravity ModelTurnDetected döner ve durdurulur
        await viewModel.StartInitialRefreshAsync();

        Assert.True(scheduler.IsAntigravityHalted);
        Assert.Contains("Engellendi", viewModel.GeminiCard.StatusText);
        Assert.False(viewModel.GeminiCard.IsLoading);

        int agyCallCountBefore = agyStub.CallCount;
        int chatCallCountBefore = chatGptStub.CallCount;

        var tcs = new TaskCompletionSource<ChatGptQuotaResult>();
        chatGptStub.DelayTcs = tcs;

        // 2. Kullanıcı elle yenileme düğmesine basar
        Task manualRefreshTask = viewModel.TriggerManualRefreshAsync();

        // ChatGPT yükleniyor durumuna geçmeli, ancak Gemini kartı "Yenileniyor..." YAPILMAMALI!
        Assert.True(viewModel.ChatGptCard.IsLoading);
        Assert.False(viewModel.GeminiCard.IsLoading);
        Assert.Contains("Engellendi", viewModel.GeminiCard.StatusText);

        var fiveHour = new QuotaWindow("codex-5h", "5 Saatlik", 300, 85.0, DateTimeOffset.UtcNow.AddHours(4));
        var weekly = new QuotaWindow("codex-weekly", "Haftalık", 10080, 70.0, DateTimeOffset.UtcNow.AddDays(5));
        var group = new QuotaGroup("chatgpt-codex", "ChatGPT Work / Codex", fiveHour, weekly, ProviderStatus.Success);
        tcs.SetResult(new ChatGptQuotaResult(group, ProviderStatus.Success));

        await manualRefreshTask;

        // ChatGPT başarıyla güncellendi
        Assert.Equal(chatCallCountBefore + 1, chatGptStub.CallCount);
        Assert.False(viewModel.ChatGptCard.IsLoading);
        Assert.Equal("✓ Güncel", viewModel.ChatGptCard.StatusText);

        // Antigravity'ye KESİNLİKLE yeni çağrı yapılmadı!
        Assert.Equal(agyCallCountBefore, agyStub.CallCount);
        Assert.False(viewModel.GeminiCard.IsLoading);
        Assert.Contains("Engellendi", viewModel.GeminiCard.StatusText);
    }

    [Fact]
    public void ViewModel_DisplaysOnlyTwoCards_AndNoThirdPartyCard()
    {
        using var viewModel = new MainWindowViewModel(new QuotaScheduler(new StubChatGptProvider(), new StubAntigravityProvider()));

        // Yalnızca 2 kart bulunmalı: ChatGPT ve Gemini
        Assert.NotNull(viewModel.ChatGptCard);
        Assert.NotNull(viewModel.GeminiCard);

        // ThirdPartyCard (Claude/GPT) özelliği kaldırılmış olmalı
        var prop = typeof(MainWindowViewModel).GetProperty("ThirdPartyCard");
        Assert.Null(prop);

        // Toplam 4 kota penceresi bulunmalı (2 kart x 2 pencere)
        Assert.NotNull(viewModel.ChatGptCard.FiveHourWindow);
        Assert.NotNull(viewModel.ChatGptCard.WeeklyWindow);
        Assert.NotNull(viewModel.GeminiCard.FiveHourWindow);
        Assert.NotNull(viewModel.GeminiCard.WeeklyWindow);
    }

    [Fact]
    public async Task StartInitialRefreshAsync_UpdatesBothCardsAndAllFourQuotasSuccessfully()
    {
        var chatGptStub = new StubChatGptProvider();
        var agyStub = new StubAntigravityProvider { ReturnModelTurnDetected = false };

        using var scheduler = new QuotaScheduler(chatGptStub, agyStub);
        using var viewModel = new MainWindowViewModel(scheduler);

        await viewModel.StartInitialRefreshAsync();

        // 1. ChatGPT kartı ve 2 kotası
        Assert.False(viewModel.ChatGptCard.IsLoading);
        Assert.Equal("✓ Güncel", viewModel.ChatGptCard.StatusText);
        Assert.Equal(85.0, viewModel.ChatGptCard.FiveHourWindow.RemainingPercent);
        Assert.Equal(70.0, viewModel.ChatGptCard.WeeklyWindow.RemainingPercent);

        // 2. Gemini kartı ve 2 kotası
        Assert.False(viewModel.GeminiCard.IsLoading);
        Assert.Equal("✓ Güncel", viewModel.GeminiCard.StatusText);
        Assert.Equal(90.0, viewModel.GeminiCard.FiveHourWindow.RemainingPercent);
        Assert.Equal(75.0, viewModel.GeminiCard.WeeklyWindow.RemainingPercent);
    }

    [Fact]
    public async Task StartInitialRefreshAsync_WhenCodexCliNotFound_AntigravitySucceeds_IsolatesErrorAndDisplaysAppropriateStatuses()
    {
        var chatGptStub = new StubChatGptProviderWithResult(
            ChatGptQuotaResult.CreateError(ProviderStatus.CliNotFound, "Codex CLI (codex.exe) bulunamadı."));

        var agyStub = new StubAntigravityProvider { ReturnModelTurnDetected = false };

        using var scheduler = new QuotaScheduler(chatGptStub, agyStub);
        using var viewModel = new MainWindowViewModel(scheduler);

        await viewModel.StartInitialRefreshAsync();

        // ChatGPT kartı CliNotFound rozeti göstermeli ve kotaları boş/varsayılan olmalı
        Assert.False(viewModel.ChatGptCard.IsLoading);
        Assert.Equal("✕ CLI Bulunamadı", viewModel.ChatGptCard.StatusText);
        Assert.Equal("Codex CLI (codex.exe) bulunamadı.", viewModel.ChatGptCard.ErrorMessage);
        Assert.Equal("--%", viewModel.ChatGptCard.FiveHourWindow.RemainingPercentText);
        Assert.Equal("--%", viewModel.ChatGptCard.WeeklyWindow.RemainingPercentText);

        // Antigravity (Gemini) kartı ise ChatGPT'den tamamen bağımsız olarak başarılı ve güncel kalmalı
        Assert.False(viewModel.GeminiCard.IsLoading);
        Assert.Equal("✓ Güncel", viewModel.GeminiCard.StatusText);
        Assert.Equal(90.0, viewModel.GeminiCard.FiveHourWindow.RemainingPercent);
        Assert.Equal(75.0, viewModel.GeminiCard.WeeklyWindow.RemainingPercent);
    }

    [Fact]
    public async Task StartInitialRefreshAsync_WhenAuthRequiredAndTimeout_DisplaysSpecificStatusBadges()
    {
        var chatGptStub = new StubChatGptProviderWithResult(
            ChatGptQuotaResult.CreateError(ProviderStatus.AuthenticationRequired, "Codex oturumu açık değil veya yetkilendirme gerekiyor."));

        var agyStub = new StubAntigravityProviderWithResult(
            AntigravityQuotaResult.CreateError(ProviderStatus.Timeout, "Antigravity zaman aşımına uğradı."));

        using var scheduler = new QuotaScheduler(chatGptStub, agyStub);
        using var viewModel = new MainWindowViewModel(scheduler);

        await viewModel.StartInitialRefreshAsync();

        // ChatGPT AuthenticationRequired
        Assert.False(viewModel.ChatGptCard.IsLoading);
        Assert.Equal("🔑 Oturum Gerekli", viewModel.ChatGptCard.StatusText);
        Assert.Equal("Codex oturumu açık değil veya yetkilendirme gerekiyor.", viewModel.ChatGptCard.ErrorMessage);

        // Antigravity Timeout
        Assert.False(viewModel.GeminiCard.IsLoading);
        Assert.Equal("⏱ Zaman Aşımı", viewModel.GeminiCard.StatusText);
        Assert.Equal("Antigravity zaman aşımına uğradı.", viewModel.GeminiCard.ErrorMessage);
    }

    [Fact]
    public async Task StartInitialRefreshAsync_WhenAntigravityCliNotFound_ChatGptSucceeds_IsolatesErrorAndDisplaysAppropriateStatuses()
    {
        var chatGptStub = new StubChatGptProvider();
        var agyStub = new StubAntigravityProviderWithResult(
            AntigravityQuotaResult.CreateError(ProviderStatus.CliNotFound, "Antigravity CLI (agy.exe) bulunamadı."));

        using var scheduler = new QuotaScheduler(chatGptStub, agyStub);
        using var viewModel = new MainWindowViewModel(scheduler);

        await viewModel.StartInitialRefreshAsync();

        // ChatGPT kartı başarılı ve güncel olmalı
        Assert.False(viewModel.ChatGptCard.IsLoading);
        Assert.Equal("✓ Güncel", viewModel.ChatGptCard.StatusText);
        Assert.Equal(85.0, viewModel.ChatGptCard.FiveHourWindow.RemainingPercent);
        Assert.Equal(70.0, viewModel.ChatGptCard.WeeklyWindow.RemainingPercent);

        // Gemini kartı CliNotFound rozeti göstermeli
        Assert.False(viewModel.GeminiCard.IsLoading);
        Assert.Equal("✕ CLI Bulunamadı", viewModel.GeminiCard.StatusText);
        Assert.Equal("Antigravity CLI (agy.exe) bulunamadı.", viewModel.GeminiCard.ErrorMessage);
        Assert.Equal("--%", viewModel.GeminiCard.FiveHourWindow.RemainingPercentText);
        Assert.Equal("--%", viewModel.GeminiCard.WeeklyWindow.RemainingPercentText);
    }

    [Fact]
    public async Task StartInitialRefreshAsync_WhenAntigravityReturnsMalformedResponse_ChatGptSucceeds_AndDisplaysAppropriateStatus()
    {
        var chatGptStub = new StubChatGptProvider();
        var agyStub = new StubAntigravityProviderWithResult(
            AntigravityQuotaResult.CreateError(ProviderStatus.InvalidResponse, "Antigravity JSON ayrıştırma hatası."));

        using var scheduler = new QuotaScheduler(chatGptStub, agyStub);
        using var viewModel = new MainWindowViewModel(scheduler);

        await viewModel.StartInitialRefreshAsync();

        // ChatGPT kartı bağımsız olarak güncel olmalı
        Assert.False(viewModel.ChatGptCard.IsLoading);
        Assert.Equal("✓ Güncel", viewModel.ChatGptCard.StatusText);

        // Gemini kartı InvalidResponse için Okuma Hatası göstermeli
        Assert.False(viewModel.GeminiCard.IsLoading);
        Assert.Equal("⚠ Okuma Hatası", viewModel.GeminiCard.StatusText);
        Assert.Equal("Antigravity JSON ayrıştırma hatası.", viewModel.GeminiCard.ErrorMessage);
    }

    [Fact]
    public async Task StartInitialRefreshAsync_WhenBothRequireAuthentication_DisplaysSessionRequiredOnBothCards()
    {
        var chatGptStub = new StubChatGptProviderWithResult(
            ChatGptQuotaResult.CreateError(ProviderStatus.AuthenticationRequired, "Codex oturumu açık değil."));
        var agyStub = new StubAntigravityProviderWithResult(
            AntigravityQuotaResult.CreateError(ProviderStatus.AuthenticationRequired, "Antigravity oturumu açık değil."));

        using var scheduler = new QuotaScheduler(chatGptStub, agyStub);
        using var viewModel = new MainWindowViewModel(scheduler);

        await viewModel.StartInitialRefreshAsync();

        // Her iki kart da bağımsız olarak Oturum Gerekli rozeti göstermeli
        Assert.False(viewModel.ChatGptCard.IsLoading);
        Assert.Equal("🔑 Oturum Gerekli", viewModel.ChatGptCard.StatusText);
        Assert.Equal("Codex oturumu açık değil.", viewModel.ChatGptCard.ErrorMessage);

        Assert.False(viewModel.GeminiCard.IsLoading);
        Assert.Equal("🔑 Oturum Gerekli", viewModel.GeminiCard.StatusText);
        Assert.Equal("Antigravity oturumu açık değil.", viewModel.GeminiCard.ErrorMessage);
    }

    [Fact]
    public async Task TriggerManualRefreshAsync_WhenOneProviderThrowsUnexpectedException_OtherCardUpdatesNormally()
    {
        var throwingChatGpt = new StubChatGptThrowing();
        var agyStub = new StubAntigravityProvider { ReturnModelTurnDetected = false };

        using var scheduler = new QuotaScheduler(throwingChatGpt, agyStub);
        using var viewModel = new MainWindowViewModel(scheduler);

        await viewModel.TriggerManualRefreshAsync();

        // ChatGPT istisna fırlatmasına rağmen çökme veya askıda kalma olmamalı
        Assert.False(viewModel.ChatGptCard.IsLoading);
        Assert.Equal("⚠ Okuma Hatası", viewModel.ChatGptCard.StatusText);
        Assert.Equal("ChatGPT kota sorgusu sırasında beklenmeyen bir hata oluştu.", viewModel.ChatGptCard.ErrorMessage);
        Assert.DoesNotContain("Beklenmeyen hata simülasyonu", viewModel.ChatGptCard.ErrorMessage);

        // Gemini normal şekilde güncellenmeli
        Assert.False(viewModel.GeminiCard.IsLoading);
        Assert.Equal("✓ Güncel", viewModel.GeminiCard.StatusText);
    }

    [Fact]
    public async Task TriggerManualRefreshAsync_WhenExceptionContainsSensitiveEmailOrToken_DoesNotExposeSensitiveDataToViewModelOrTooltip()
    {
        var sensitiveThrowing = new StubChatGptSensitiveThrowing();
        var agyStub = new StubAntigravityProvider { ReturnModelTurnDetected = false };

        using var scheduler = new QuotaScheduler(sensitiveThrowing, agyStub);
        using var viewModel = new MainWindowViewModel(scheduler);

        await viewModel.TriggerManualRefreshAsync();

        // Kart hataya düşmeli ama hassas kullanıcı veya token verisini tooltip/ErrorMessage'a asla aktarmamalı
        Assert.False(viewModel.ChatGptCard.IsLoading);
        Assert.Equal("⚠ Okuma Hatası", viewModel.ChatGptCard.StatusText);
        Assert.Equal("ChatGPT kota sorgusu sırasında beklenmeyen bir hata oluştu.", viewModel.ChatGptCard.ErrorMessage);
        Assert.DoesNotContain("alice.secret@acme.org", viewModel.ChatGptCard.ErrorMessage);
        Assert.DoesNotContain("super_secret_bearer_key_777", viewModel.ChatGptCard.ErrorMessage);
    }

    private sealed class StubChatGptSensitiveThrowing : IChatGptProvider
    {
        public Task<ChatGptQuotaResult> FetchQuotaAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Failed auth for alice.secret@acme.org with key super_secret_bearer_key_777");
    }

    private sealed class StubChatGptProviderWithResult : IChatGptProvider
    {
        private readonly ChatGptQuotaResult _result;
        public StubChatGptProviderWithResult(ChatGptQuotaResult result) => _result = result;
        public Task<ChatGptQuotaResult> FetchQuotaAsync(CancellationToken cancellationToken = default) => Task.FromResult(_result);
    }

    private sealed class StubAntigravityProviderWithResult : IAntigravityProvider
    {
        private readonly AntigravityQuotaResult _result;
        public StubAntigravityProviderWithResult(AntigravityQuotaResult result) => _result = result;
        public Task<AntigravityQuotaResult> FetchQuotaAsync(CancellationToken cancellationToken = default) => Task.FromResult(_result);
    }

    private sealed class StubChatGptThrowing : IChatGptProvider
    {
        public Task<ChatGptQuotaResult> FetchQuotaAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Beklenmeyen hata simülasyonu.");
    }
}
