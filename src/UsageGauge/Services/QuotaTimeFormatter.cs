using System;

namespace UsageGauge.Services;

/// <summary>
/// Kota sıfırlanma zamanı ve geri sayım formatlayıcı.
/// Mutlak UTC zaman farkından yerel gösterim üretir; asla tahmini yüzde üretmez.
/// </summary>
public static class QuotaTimeFormatter
{
    public const string ExpiredText = "Sıfırlanma zamanı doldu — yeni kota doğrulanıyor";

    public static string FormatCountdown(DateTimeOffset? resetsAtUtc, DateTimeOffset nowUtc)
    {
        if (!resetsAtUtc.HasValue)
        {
            return "Sıfırlanma: Belirsiz";
        }

        TimeSpan diff = resetsAtUtc.Value - nowUtc;
        if (diff <= TimeSpan.Zero)
        {
            return ExpiredText;
        }

        if (diff.TotalDays >= 1.0)
        {
            int days = (int)Math.Floor(diff.TotalDays);
            return $"Sıfırlanma: {days}g {diff.Hours}s";
        }

        if (diff.TotalHours >= 1.0)
        {
            return $"Sıfırlanma: {diff.Hours}s {diff.Minutes}dk";
        }

        if (diff.TotalMinutes >= 1.0)
        {
            return $"Sıfırlanma: {diff.Minutes}dk";
        }

        return "Sıfırlanma: < 1 dk";
    }

    public static string FormatLastChecked(DateTimeOffset? lastCheckedUtc)
    {
        if (!lastCheckedUtc.HasValue)
        {
            return "--:--";
        }

        DateTime localTime = lastCheckedUtc.Value.ToLocalTime().DateTime;
        return localTime.ToString("HH:mm");
    }
}
