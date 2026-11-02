using System;
using System.Threading;
using System.Threading.Tasks;
using UsageGauge.Models;

namespace UsageGauge.Providers;

/// <summary>
/// ChatGPT ve Antigravity sağlayıcılarının eşzamansız yürütülmesini koordine eder.
/// Sağlayıcılar tamamen yalıtılmıştır; birindeki hata diğer sağlayıcının sonucunu etkilemez.
/// Yeni sorgu başarısız olursa önceki geçerli kota bayat (IsStale = true) olarak korunur.
/// </summary>
public sealed class UsageCoordinator
{
    private readonly IChatGptProvider _chatGptProvider;
    private readonly IAntigravityProvider _antigravityProvider;

    public UsageCoordinator(
        IChatGptProvider? chatGptProvider = null,
        IAntigravityProvider? antigravityProvider = null)
    {
        _chatGptProvider = chatGptProvider ?? new ChatGptProvider();
        _antigravityProvider = antigravityProvider ?? new AntigravityProvider();
    }

    /// <summary>
    /// Her iki sağlayıcıyı paralel olarak sorgular.
    /// Mevcut anlık görüntü verilirse, hata alan sağlayıcının önceki başarılı verisi bayat olarak korunur.
    /// </summary>
    public async Task<OverallUsageSnapshot> RefreshAllAsync(
        OverallUsageSnapshot? currentSnapshot = null,
        CancellationToken cancellationToken = default)
    {
        Task<ChatGptQuotaResult> chatGptTask = SafeFetchChatGptAsync(cancellationToken);
        Task<AntigravityQuotaResult> antigravityTask = SafeFetchAntigravityAsync(cancellationToken);

        await Task.WhenAll(chatGptTask, antigravityTask);

        ChatGptQuotaResult chatGptResult = await chatGptTask;
        AntigravityQuotaResult antigravityResult = await antigravityTask;

        ChatGptQuotaResult finalChatGpt = ApplyStalePreservation(chatGptResult, currentSnapshot?.ChatGpt);
        AntigravityQuotaResult finalAntigravity = ApplyStalePreservation(antigravityResult, currentSnapshot?.Antigravity);

        return new OverallUsageSnapshot(
            chatGpt: finalChatGpt,
            antigravity: finalAntigravity,
            lastUpdatedUtc: DateTimeOffset.UtcNow);
    }

    public async Task<ChatGptQuotaResult> RefreshChatGptAsync(
        OverallUsageSnapshot? currentSnapshot = null,
        CancellationToken cancellationToken = default)
    {
        ChatGptQuotaResult freshResult = await SafeFetchChatGptAsync(cancellationToken);
        return ApplyStalePreservation(freshResult, currentSnapshot?.ChatGpt);
    }

    public async Task<AntigravityQuotaResult> RefreshAntigravityAsync(
        OverallUsageSnapshot? currentSnapshot = null,
        CancellationToken cancellationToken = default)
    {
        AntigravityQuotaResult freshResult = await SafeFetchAntigravityAsync(cancellationToken);
        return ApplyStalePreservation(freshResult, currentSnapshot?.Antigravity);
    }

    private async Task<ChatGptQuotaResult> SafeFetchChatGptAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _chatGptProvider.FetchQuotaAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return ChatGptQuotaResult.CreateError(ProviderStatus.Timeout, "İşlem iptal edildi.");
        }
        catch
        {
            return ChatGptQuotaResult.CreateError(ProviderStatus.ProcessError, "ChatGPT kota sağlayıcısında beklenmeyen hata oluştu.");
        }
    }

    private async Task<AntigravityQuotaResult> SafeFetchAntigravityAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _antigravityProvider.FetchQuotaAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return AntigravityQuotaResult.CreateError(ProviderStatus.Timeout, "İşlem iptal edildi.");
        }
        catch
        {
            return AntigravityQuotaResult.CreateError(ProviderStatus.ProcessError, "Antigravity kota sağlayıcısında beklenmeyen hata oluştu.");
        }
    }

    private static ChatGptQuotaResult ApplyStalePreservation(
        ChatGptQuotaResult freshResult,
        ChatGptQuotaResult? previousResult)
    {
        if (freshResult.IsSuccess || previousResult == null)
        {
            return freshResult;
        }

        if (previousResult.CodexGroup.HasUsableData)
        {
            QuotaGroup staleGroup = previousResult.CodexGroup.ToStale(freshResult.Status, freshResult.ErrorMessage);
            return new ChatGptQuotaResult(
                staleGroup,
                freshResult.Status,
                freshResult.ErrorMessage,
                freshResult.FetchedAtUtc);
        }

        return freshResult;
    }

    private static AntigravityQuotaResult ApplyStalePreservation(
        AntigravityQuotaResult freshResult,
        AntigravityQuotaResult? previousResult)
    {
        if (previousResult == null)
        {
            return freshResult;
        }

        QuotaGroup geminiGroup = freshResult.GeminiGroup;
        if (!freshResult.GeminiGroup.IsSuccess && previousResult.GeminiGroup.HasUsableData)
        {
            geminiGroup = previousResult.GeminiGroup.ToStale(
                freshResult.GeminiGroup.Status,
                freshResult.GeminiGroup.ErrorMessage);
        }

        QuotaGroup tpGroup = freshResult.ThirdPartyGroup;
        if (!freshResult.ThirdPartyGroup.IsSuccess && previousResult.ThirdPartyGroup.HasUsableData)
        {
            tpGroup = previousResult.ThirdPartyGroup.ToStale(
                freshResult.ThirdPartyGroup.Status,
                freshResult.ThirdPartyGroup.ErrorMessage);
        }

        return new AntigravityQuotaResult(
            geminiGroup,
            tpGroup,
            freshResult.Status,
            freshResult.NumTurns,
            freshResult.TotalTokens,
            freshResult.ErrorMessage,
            freshResult.FetchedAtUtc);
    }
}
