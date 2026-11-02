using System;

namespace UsageGauge.Models;

/// <summary>
/// İlgili sağlayıcı grubuna (Codex, Gemini veya Claude/GPT) ait 5 saatlik ve haftalık kotaları temsil eder.
/// </summary>
public sealed record QuotaGroup
{
    public string GroupId { get; init; }
    public string Title { get; init; }
    public QuotaWindow? FiveHourLimit { get; init; }
    public QuotaWindow? WeeklyLimit { get; init; }
    public ProviderStatus Status { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset FetchedAtUtc { get; init; }
    public bool IsStale { get; init; }

    public bool IsSuccess => Status == ProviderStatus.Success && !IsStale && FiveHourLimit != null && WeeklyLimit != null;
    public bool HasUsableData => FiveHourLimit != null && WeeklyLimit != null;

    public QuotaGroup(
        string groupId,
        string title,
        QuotaWindow? fiveHourLimit,
        QuotaWindow? weeklyLimit,
        ProviderStatus status,
        string? errorMessage = null,
        DateTimeOffset? fetchedAtUtc = null,
        bool isStale = false)
    {
        GroupId = groupId ?? throw new ArgumentNullException(nameof(groupId));
        Title = title ?? throw new ArgumentNullException(nameof(title));
        FiveHourLimit = fiveHourLimit;
        WeeklyLimit = weeklyLimit;
        Status = status;
        ErrorMessage = errorMessage;
        FetchedAtUtc = fetchedAtUtc?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
        IsStale = isStale;
    }

    public static QuotaGroup CreateError(string groupId, string title, ProviderStatus status, string errorMessage)
    {
        return new QuotaGroup(groupId, title, null, null, status, errorMessage);
    }

    public QuotaGroup ToStale(ProviderStatus newStatus, string? newErrorMessage)
    {
        return new QuotaGroup(
            GroupId,
            Title,
            FiveHourLimit,
            WeeklyLimit,
            newStatus,
            newErrorMessage,
            FetchedAtUtc,
            isStale: true);
    }
}
