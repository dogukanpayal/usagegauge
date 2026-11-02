using System;

namespace UsageGauge.Models;

/// <summary>
/// ChatGPT / Codex sağlayıcısından dönen kota sonucu.
/// </summary>
public sealed record ChatGptQuotaResult
{
    public QuotaGroup CodexGroup { get; init; }
    public ProviderStatus Status { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset FetchedAtUtc { get; init; }

    public bool IsSuccess => Status == ProviderStatus.Success && CodexGroup.IsSuccess;

    public ChatGptQuotaResult(
        QuotaGroup codexGroup,
        ProviderStatus status,
        string? errorMessage = null,
        DateTimeOffset? fetchedAtUtc = null)
    {
        CodexGroup = codexGroup ?? throw new ArgumentNullException(nameof(codexGroup));
        Status = status;
        ErrorMessage = errorMessage;
        FetchedAtUtc = fetchedAtUtc?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
    }

    public static ChatGptQuotaResult CreateError(ProviderStatus status, string errorMessage)
    {
        var group = QuotaGroup.CreateError("chatgpt-codex", "ChatGPT Work / Codex", status, errorMessage);
        return new ChatGptQuotaResult(group, status, errorMessage);
    }
}
