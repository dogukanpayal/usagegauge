using System;

namespace UsageGauge.Models;

/// <summary>
/// Tek bir kota penceresini (5 saatlik veya haftalık) temsil eder.
/// </summary>
public sealed record QuotaWindow
{
    public string Id { get; init; }
    public string DisplayName { get; init; }
    public int WindowDurationMinutes { get; init; }
    public double RemainingPercent { get; init; }
    public DateTimeOffset ResetsAtUtc { get; init; }

    public QuotaWindow(
        string id,
        string displayName,
        int windowDurationMinutes,
        double remainingPercent,
        DateTimeOffset resetsAtUtc)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Kota penceresi ID boş olamaz.", nameof(id));
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Görünen ad boş olamaz.", nameof(displayName));
        if (windowDurationMinutes <= 0)
            throw new ArgumentOutOfRangeException(nameof(windowDurationMinutes), "Pencere süresi pozitif olmalıdır.");
        if (remainingPercent < 0.0 || remainingPercent > 100.0)
            throw new ArgumentOutOfRangeException(nameof(remainingPercent), "Kalan yüzde 0 ile 100 arasında olmalıdır.");

        Id = id;
        DisplayName = displayName;
        WindowDurationMinutes = windowDurationMinutes;
        RemainingPercent = Math.Round(remainingPercent, 2);
        ResetsAtUtc = resetsAtUtc.ToUniversalTime();
    }
}
