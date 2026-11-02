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
}
