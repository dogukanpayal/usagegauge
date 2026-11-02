using System;

namespace UsageGauge.Models;

/// <summary>
/// Tüm sağlayıcılardan (ChatGPT/Codex ve Antigravity) gelen kotaların anlık görüntüsü.
/// İki sağlayıcının verileri tamamen yalıtılmıştır.
/// </summary>
public sealed record OverallUsageSnapshot
{
    public ChatGptQuotaResult? ChatGpt { get; init; }
    public AntigravityQuotaResult? Antigravity { get; init; }
    public DateTimeOffset LastUpdatedUtc { get; init; }

    public OverallUsageSnapshot(
        ChatGptQuotaResult? chatGpt = null,
        AntigravityQuotaResult? antigravity = null,
        DateTimeOffset? lastUpdatedUtc = null)
    {
        ChatGpt = chatGpt;
        Antigravity = antigravity;
        LastUpdatedUtc = lastUpdatedUtc?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
    }

    public OverallUsageSnapshot WithChatGpt(ChatGptQuotaResult result)
    {
        return new OverallUsageSnapshot(result, Antigravity, DateTimeOffset.UtcNow);
    }

    public OverallUsageSnapshot WithAntigravity(AntigravityQuotaResult result)
    {
        return new OverallUsageSnapshot(ChatGpt, result, DateTimeOffset.UtcNow);
    }
}
