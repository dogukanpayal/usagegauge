using System;
using UsageGauge.Services;
using Xunit;

namespace UsageGauge.Tests.Services;

public class QuotaTimeFormatterTests
{
    [Fact]
    public void FormatCountdown_WithDaysDifference_FormatsCorrectly()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-11-02T12:00:00Z");
        DateTimeOffset reset = DateTimeOffset.Parse("2026-11-08T15:20:00Z"); // 6 gün, 3 saat, 20 dk

        string result = QuotaTimeFormatter.FormatCountdown(reset, now);

        Assert.Equal("Sıfırlanma: 6g 3s", result);
    }

    [Fact]
    public void FormatCountdown_WithHoursDifference_FormatsCorrectly()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-11-02T12:00:00Z");
        DateTimeOffset reset = DateTimeOffset.Parse("2026-11-02T16:27:00Z"); // 4 saat, 27 dk

        string result = QuotaTimeFormatter.FormatCountdown(reset, now);

        Assert.Equal("Sıfırlanma: 4s 27dk", result);
    }

    [Fact]
    public void FormatCountdown_WithMinutesDifference_FormatsCorrectly()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-11-02T12:00:00Z");
        DateTimeOffset reset = DateTimeOffset.Parse("2026-11-02T12:42:15Z"); // 42 dk, 15 sn

        string result = QuotaTimeFormatter.FormatCountdown(reset, now);

        Assert.Equal("Sıfırlanma: 42dk", result);
    }

    [Fact]
    public void FormatCountdown_WithLessThanOneMinute_FormatsCorrectly()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-11-02T12:00:00Z");
        DateTimeOffset reset = DateTimeOffset.Parse("2026-11-02T12:00:25Z"); // 25 sn

        string result = QuotaTimeFormatter.FormatCountdown(reset, now);

        Assert.Equal("Sıfırlanma: < 1 dk", result);
    }

    [Fact]
    public void FormatCountdown_WhenExpired_ReturnsExpiredMessageAndNeverHypothetical100Percent()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-11-02T12:00:00Z");
        DateTimeOffset pastReset = DateTimeOffset.Parse("2026-11-02T11:59:00Z");

        string result = QuotaTimeFormatter.FormatCountdown(pastReset, now);

        Assert.Equal(QuotaTimeFormatter.ExpiredText, result);
    }

    [Fact]
    public void FormatCountdown_WhenNull_ReturnsUncertain()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string result = QuotaTimeFormatter.FormatCountdown(null, now);

        Assert.Equal("Sıfırlanma: Belirsiz", result);
    }

    [Fact]
    public void FormatLastChecked_WhenValid_ReturnsLocalHourMinute()
    {
        DateTimeOffset utcTime = new(2026, 11, 2, 14, 5, 0, TimeSpan.Zero);
        string result = QuotaTimeFormatter.FormatLastChecked(utcTime);

        string expectedLocal = utcTime.ToLocalTime().ToString("HH:mm");
        Assert.Equal(expectedLocal, result);
    }
}
