using System;

namespace UsageGauge.Models;

/// <summary>
/// Antigravity sağlayıcısından dönen kota sonucu (Gemini ve Claude/GPT havuzları).
/// </summary>
public sealed record AntigravityQuotaResult
{
    public QuotaGroup GeminiGroup { get; init; }
    public QuotaGroup ThirdPartyGroup { get; init; }
    public ProviderStatus Status { get; init; }
    public string? ErrorMessage { get; init; }
    public int NumTurns { get; init; }
    public long TotalTokens { get; init; }
    public DateTimeOffset FetchedAtUtc { get; init; }

    public bool IsSuccess => Status == ProviderStatus.Success && GeminiGroup.IsSuccess && ThirdPartyGroup.IsSuccess;
    public bool HasAnySuccess => GeminiGroup.IsSuccess || ThirdPartyGroup.IsSuccess;

    public AntigravityQuotaResult(
        QuotaGroup geminiGroup,
        QuotaGroup thirdPartyGroup,
        ProviderStatus status,
        int numTurns = 0,
        long totalTokens = 0,
        string? errorMessage = null,
        DateTimeOffset? fetchedAtUtc = null)
    {
        GeminiGroup = geminiGroup ?? throw new ArgumentNullException(nameof(geminiGroup));
        ThirdPartyGroup = thirdPartyGroup ?? throw new ArgumentNullException(nameof(thirdPartyGroup));
        Status = status;
        NumTurns = numTurns;
        TotalTokens = totalTokens;
        ErrorMessage = errorMessage;
        FetchedAtUtc = fetchedAtUtc?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
    }

    public static AntigravityQuotaResult CreateError(
        ProviderStatus status,
        string errorMessage,
        int numTurns = 0,
        long totalTokens = 0)
    {
        var gemini = QuotaGroup.CreateError("antigravity-gemini", "Antigravity Gemini", status, errorMessage);
        var thirdParty = QuotaGroup.CreateError("antigravity-3p", "Antigravity Claude / GPT", status, errorMessage);
        return new AntigravityQuotaResult(gemini, thirdParty, status, numTurns, totalTokens, errorMessage);
    }
}
