using System;
using System.Threading;
using System.Threading.Tasks;
using UsageGauge.Models;
using UsageGauge.Providers;
using Xunit;

namespace UsageGauge.Tests.Providers;

public class UsageCoordinatorTests
{
    private sealed class StubChatGptProvider : IChatGptProvider
    {
        public Func<Task<ChatGptQuotaResult>> Handler { get; set; }

        public StubChatGptProvider(Func<Task<ChatGptQuotaResult>> handler)
        {
            Handler = handler;
        }

        public Task<ChatGptQuotaResult> FetchQuotaAsync(CancellationToken cancellationToken = default)
        {
            return Handler();
        }
    }

    private sealed class StubAntigravityProvider : IAntigravityProvider
    {
        public Func<Task<AntigravityQuotaResult>> Handler { get; set; }

        public StubAntigravityProvider(Func<Task<AntigravityQuotaResult>> handler)
        {
            Handler = handler;
        }

        public Task<AntigravityQuotaResult> FetchQuotaAsync(CancellationToken cancellationToken = default)
        {
            return Handler();
        }
    }

    private static ChatGptQuotaResult CreateSuccessfulChatGptResult()
    {
        var fiveHour = new QuotaWindow("codex-5h", "5 Saatlik", 300, 80.0, DateTimeOffset.UtcNow.AddHours(4));
        var weekly = new QuotaWindow("codex-weekly", "Haftalık", 10080, 60.0, DateTimeOffset.UtcNow.AddDays(5));
        var group = new QuotaGroup("chatgpt-codex", "ChatGPT Work / Codex", fiveHour, weekly, ProviderStatus.Success);
        return new ChatGptQuotaResult(group, ProviderStatus.Success);
    }

    private static AntigravityQuotaResult CreateSuccessfulAntigravityResult()
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
    public async Task RefreshAllAsync_WhenBothSucceed_ReturnsCombinedSixWindowsAcrossThreeGroups()
    {
        var chatGptStub = new StubChatGptProvider(() => Task.FromResult(CreateSuccessfulChatGptResult()));
        var agyStub = new StubAntigravityProvider(() => Task.FromResult(CreateSuccessfulAntigravityResult()));

        var coordinator = new UsageCoordinator(chatGptStub, agyStub);
        OverallUsageSnapshot snapshot = await coordinator.RefreshAllAsync();

        Assert.NotNull(snapshot.ChatGpt);
        Assert.NotNull(snapshot.Antigravity);

        // 3 Grup kontrolü
        Assert.True(snapshot.ChatGpt.IsSuccess);
        Assert.True(snapshot.Antigravity.IsSuccess);

        // 6 Kota penceresi kontrolü
        Assert.NotNull(snapshot.ChatGpt.CodexGroup.FiveHourLimit);
        Assert.NotNull(snapshot.ChatGpt.CodexGroup.WeeklyLimit);
        Assert.NotNull(snapshot.Antigravity.GeminiGroup.FiveHourLimit);
        Assert.NotNull(snapshot.Antigravity.GeminiGroup.WeeklyLimit);
        Assert.NotNull(snapshot.Antigravity.ThirdPartyGroup.FiveHourLimit);
        Assert.NotNull(snapshot.Antigravity.ThirdPartyGroup.WeeklyLimit);

        Assert.Equal(80.0, snapshot.ChatGpt.CodexGroup.FiveHourLimit.RemainingPercent);
        Assert.Equal(60.0, snapshot.ChatGpt.CodexGroup.WeeklyLimit.RemainingPercent);
        Assert.Equal(90.0, snapshot.Antigravity.GeminiGroup.FiveHourLimit.RemainingPercent);
        Assert.Equal(75.0, snapshot.Antigravity.GeminiGroup.WeeklyLimit.RemainingPercent);
        Assert.Equal(100.0, snapshot.Antigravity.ThirdPartyGroup.FiveHourLimit.RemainingPercent);
        Assert.Equal(95.0, snapshot.Antigravity.ThirdPartyGroup.WeeklyLimit.RemainingPercent);
    }

    [Fact]
    public async Task RefreshAllAsync_WhenChatGptFails_AntigravitySucceedsIndependently()
    {
        var chatGptStub = new StubChatGptProvider(() => Task.FromResult(
            ChatGptQuotaResult.CreateError(ProviderStatus.Timeout, "Zaman aşımı")));
        var agyStub = new StubAntigravityProvider(() => Task.FromResult(CreateSuccessfulAntigravityResult()));

        var coordinator = new UsageCoordinator(chatGptStub, agyStub);
        OverallUsageSnapshot snapshot = await coordinator.RefreshAllAsync();

        // ChatGPT hata durumunda
        Assert.NotNull(snapshot.ChatGpt);
        Assert.False(snapshot.ChatGpt.IsSuccess);
        Assert.Equal(ProviderStatus.Timeout, snapshot.ChatGpt.Status);

        // Antigravity bağımsız olarak başarılı ve verileri tam
        Assert.NotNull(snapshot.Antigravity);
        Assert.True(snapshot.Antigravity.IsSuccess);
        Assert.Equal(90.0, snapshot.Antigravity.GeminiGroup.FiveHourLimit!.RemainingPercent);
        Assert.Equal(100.0, snapshot.Antigravity.ThirdPartyGroup.FiveHourLimit!.RemainingPercent);
    }

    [Fact]
    public async Task RefreshAllAsync_WhenAntigravityFails_ChatGptSucceedsIndependently()
    {
        var chatGptStub = new StubChatGptProvider(() => Task.FromResult(CreateSuccessfulChatGptResult()));
        var agyStub = new StubAntigravityProvider(() => Task.FromResult(
            AntigravityQuotaResult.CreateError(ProviderStatus.CliNotFound, "agy bulunamadı")));

        var coordinator = new UsageCoordinator(chatGptStub, agyStub);
        OverallUsageSnapshot snapshot = await coordinator.RefreshAllAsync();

        // ChatGPT bağımsız olarak başarılı
        Assert.NotNull(snapshot.ChatGpt);
        Assert.True(snapshot.ChatGpt.IsSuccess);
        Assert.Equal(80.0, snapshot.ChatGpt.CodexGroup.FiveHourLimit!.RemainingPercent);

        // Antigravity hata durumunda
        Assert.NotNull(snapshot.Antigravity);
        Assert.False(snapshot.Antigravity.IsSuccess);
        Assert.Equal(ProviderStatus.CliNotFound, snapshot.Antigravity.Status);
    }

    [Fact]
    public async Task RefreshAllAsync_WhenBothFail_PreservesErrorStatusesIndependently()
    {
        var chatGptStub = new StubChatGptProvider(() => Task.FromResult(
            ChatGptQuotaResult.CreateError(ProviderStatus.AuthenticationRequired, "Oturum gerekli")));
        var agyStub = new StubAntigravityProvider(() => Task.FromResult(
            AntigravityQuotaResult.CreateError(ProviderStatus.Timeout, "Zaman aşımı")));

        var coordinator = new UsageCoordinator(chatGptStub, agyStub);
        OverallUsageSnapshot snapshot = await coordinator.RefreshAllAsync();

        Assert.Equal(ProviderStatus.AuthenticationRequired, snapshot.ChatGpt!.Status);
        Assert.Equal(ProviderStatus.Timeout, snapshot.Antigravity!.Status);
    }

    [Fact]
    public async Task RefreshAllAsync_WhenChatGptFailsWithExistingSnapshot_PreservesStaleChatGptData()
    {
        var initialSnapshot = new OverallUsageSnapshot(
            chatGpt: CreateSuccessfulChatGptResult(),
            antigravity: CreateSuccessfulAntigravityResult());

        // Yeni çağrıda ChatGPT zaman aşımına uğruyor, Antigravity yine başarılı
        var chatGptStub = new StubChatGptProvider(() => Task.FromResult(
            ChatGptQuotaResult.CreateError(ProviderStatus.Timeout, "Zaman aşımı")));
        var agyStub = new StubAntigravityProvider(() => Task.FromResult(CreateSuccessfulAntigravityResult()));

        var coordinator = new UsageCoordinator(chatGptStub, agyStub);
        OverallUsageSnapshot updatedSnapshot = await coordinator.RefreshAllAsync(initialSnapshot);

        // ChatGPT durumu hata, ancak önceki kotalar IsStale=true olarak korunmalı
        Assert.NotNull(updatedSnapshot.ChatGpt);
        Assert.False(updatedSnapshot.ChatGpt.IsSuccess);
        Assert.Equal(ProviderStatus.Timeout, updatedSnapshot.ChatGpt.Status);

        QuotaGroup codexGroup = updatedSnapshot.ChatGpt.CodexGroup;
        Assert.True(codexGroup.IsStale);
        Assert.True(codexGroup.HasUsableData);
        Assert.Equal(80.0, codexGroup.FiveHourLimit!.RemainingPercent);
        Assert.Equal(60.0, codexGroup.WeeklyLimit!.RemainingPercent);

        // Antigravity ise güncel ve bayat değil
        Assert.True(updatedSnapshot.Antigravity!.IsSuccess);
        Assert.False(updatedSnapshot.Antigravity.GeminiGroup.IsStale);
    }

    [Fact]
    public async Task RefreshAllAsync_WhenAntigravityPartiallyFailsWithExistingSnapshot_PreservesOnlyFailedGroupAsStale()
    {
        var initialSnapshot = new OverallUsageSnapshot(
            chatGpt: CreateSuccessfulChatGptResult(),
            antigravity: CreateSuccessfulAntigravityResult());

        // Yeni çağrıda Gemini başarılı ama 3p grubu hata alıyor
        var freshGemini = new QuotaGroup("antigravity-gemini", "Antigravity Gemini",
            new QuotaWindow("gemini-5h", "5 Saatlik", 300, 95.0, DateTimeOffset.UtcNow.AddHours(2)),
            new QuotaWindow("gemini-weekly", "Haftalık", 10080, 80.0, DateTimeOffset.UtcNow.AddDays(3)),
            ProviderStatus.Success);
        var failed3p = QuotaGroup.CreateError("antigravity-3p", "Antigravity Claude / GPT", ProviderStatus.InvalidResponse, "Havuz eksik");

        var partialAgyResult = new AntigravityQuotaResult(freshGemini, failed3p, ProviderStatus.PartialSuccess);

        var chatGptStub = new StubChatGptProvider(() => Task.FromResult(CreateSuccessfulChatGptResult()));
        var agyStub = new StubAntigravityProvider(() => Task.FromResult(partialAgyResult));

        var coordinator = new UsageCoordinator(chatGptStub, agyStub);
        OverallUsageSnapshot updatedSnapshot = await coordinator.RefreshAllAsync(initialSnapshot);

        Assert.NotNull(updatedSnapshot.Antigravity);
        // Gemini taze ve güncel değerde
        Assert.False(updatedSnapshot.Antigravity.GeminiGroup.IsStale);
        Assert.Equal(95.0, updatedSnapshot.Antigravity.GeminiGroup.FiveHourLimit!.RemainingPercent);

        // 3p grubu ise önceki snapshot'tan bayat olarak korundu
        Assert.True(updatedSnapshot.Antigravity.ThirdPartyGroup.IsStale);
        Assert.True(updatedSnapshot.Antigravity.ThirdPartyGroup.HasUsableData);
        Assert.Equal(100.0, updatedSnapshot.Antigravity.ThirdPartyGroup.FiveHourLimit!.RemainingPercent);
    }

    [Fact]
    public async Task RefreshAllAsync_WhenProviderThrowsUnhandledException_IsolatesAndReflectsProcessError()
    {
        var crashingChatGptStub = new StubChatGptProvider(() => throw new InvalidOperationException("Beklenmeyen çökme"));
        var agyStub = new StubAntigravityProvider(() => Task.FromResult(CreateSuccessfulAntigravityResult()));

        var coordinator = new UsageCoordinator(crashingChatGptStub, agyStub);
        OverallUsageSnapshot snapshot = await coordinator.RefreshAllAsync();

        // ChatGPT beklenmeyen hatayı yakalayıp ProcessError döndürmeli
        Assert.NotNull(snapshot.ChatGpt);
        Assert.False(snapshot.ChatGpt.IsSuccess);
        Assert.Equal(ProviderStatus.ProcessError, snapshot.ChatGpt.Status);

        // Antigravity hiç etkilenmeden başarılı tamamlanmalı
        Assert.NotNull(snapshot.Antigravity);
        Assert.True(snapshot.Antigravity.IsSuccess);
        Assert.Equal(90.0, snapshot.Antigravity.GeminiGroup.FiveHourLimit!.RemainingPercent);
    }
}
