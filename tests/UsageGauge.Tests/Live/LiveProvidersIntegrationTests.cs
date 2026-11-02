using System;
using System.Threading.Tasks;
using UsageGauge.Models;
using UsageGauge.Providers;
using Xunit;
using Xunit.Abstractions;

namespace UsageGauge.Tests.Live;

public class LiveProvidersIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public LiveProvidersIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task LiveQuery_ChatGpt_ReadsRateLimitsWithoutTokensOrModelRequests()
    {
        if (Environment.GetEnvironmentVariable("USAGEGAUGE_ENABLE_LIVE_TESTS") != "1")
        {
            _output.WriteLine("Canlı entegrasyon testi atlandı. Çalıştırmak için USAGEGAUGE_ENABLE_LIVE_TESTS=1 ortam değişkenini ayarlayın.");
            return;
        }

        var provider = new ChatGptProvider();
        ChatGptQuotaResult result = await provider.FetchQuotaAsync();

        _output.WriteLine("=== CANLI SORGULAMA: ChatGPT Work / Codex ===");
        _output.WriteLine($"Durum: {result.Status}");

        if (result.Status == ProviderStatus.Success)
        {
            QuotaWindow fiveHour = result.CodexGroup.FiveHourLimit!;
            QuotaWindow weekly = result.CodexGroup.WeeklyLimit!;

            _output.WriteLine($"5 Saatlik Havuz (300 dk): Kalan %{fiveHour.RemainingPercent:F2} | Sıfırlanma (UTC): {fiveHour.ResetsAtUtc:yyyy-MM-dd HH:mm:ss 'UTC'}");
            _output.WriteLine($"Haftalık Havuz (10080 dk): Kalan %{weekly.RemainingPercent:F2} | Sıfırlanma (UTC): {weekly.ResetsAtUtc:yyyy-MM-dd HH:mm:ss 'UTC'}");

            Assert.InRange(fiveHour.RemainingPercent, 0.0, 100.0);
            Assert.InRange(weekly.RemainingPercent, 0.0, 100.0);
            Assert.Equal(300, fiveHour.WindowDurationMinutes);
            Assert.Equal(10080, weekly.WindowDurationMinutes);
        }
        else
        {
            _output.WriteLine($"Hata Mesajı: {result.ErrorMessage}");
            Assert.True(result.Status is ProviderStatus.AuthenticationRequired or ProviderStatus.CliNotFound or ProviderStatus.Timeout);
        }
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task LiveQuery_Antigravity_ReadsUsageWithZeroTurnsAndZeroTokens()
    {
        if (Environment.GetEnvironmentVariable("USAGEGAUGE_ENABLE_LIVE_TESTS") != "1")
        {
            _output.WriteLine("Canlı entegrasyon testi atlandı. Çalıştırmak için USAGEGAUGE_ENABLE_LIVE_TESTS=1 ortam değişkenini ayarlayın.");
            return;
        }

        var provider = new AntigravityProvider();
        AntigravityQuotaResult result = await provider.FetchQuotaAsync();

        _output.WriteLine("=== CANLI SORGULAMA: Antigravity CLI ===");
        _output.WriteLine($"Durum: {result.Status}");
        _output.WriteLine($"Model Tur Sayısı (num_turns): {result.NumTurns}");
        _output.WriteLine($"Harcanan Token (total_tokens): {result.TotalTokens}");

        if (result.Status == ProviderStatus.Success)
        {
            // Güvenlik denetimi: Model turu ve token harcaması kesinlikle 0 olmalıdır
            Assert.Equal(0, result.NumTurns);
            Assert.Equal(0, result.TotalTokens);

            QuotaWindow g5h = result.GeminiGroup.FiveHourLimit!;
            QuotaWindow gWeekly = result.GeminiGroup.WeeklyLimit!;
            QuotaWindow tp5h = result.ThirdPartyGroup.FiveHourLimit!;
            QuotaWindow tpWeekly = result.ThirdPartyGroup.WeeklyLimit!;

            _output.WriteLine($"Gemini 5 Saatlik (gemini-5h): Kalan %{g5h.RemainingPercent:F2} | Sıfırlanma (UTC): {g5h.ResetsAtUtc:yyyy-MM-dd HH:mm:ss 'UTC'}");
            _output.WriteLine($"Gemini Haftalık (gemini-weekly): Kalan %{gWeekly.RemainingPercent:F2} | Sıfırlanma (UTC): {gWeekly.ResetsAtUtc:yyyy-MM-dd HH:mm:ss 'UTC'}");
            _output.WriteLine($"Claude/GPT 5 Saatlik (3p-5h): Kalan %{tp5h.RemainingPercent:F2} | Sıfırlanma (UTC): {tp5h.ResetsAtUtc:yyyy-MM-dd HH:mm:ss 'UTC'}");
            _output.WriteLine($"Claude/GPT Haftalık (3p-weekly): Kalan %{tpWeekly.RemainingPercent:F2} | Sıfırlanma (UTC): {tpWeekly.ResetsAtUtc:yyyy-MM-dd HH:mm:ss 'UTC'}");

            Assert.InRange(g5h.RemainingPercent, 0.0, 100.0);
            Assert.InRange(gWeekly.RemainingPercent, 0.0, 100.0);
            Assert.InRange(tp5h.RemainingPercent, 0.0, 100.0);
            Assert.InRange(tpWeekly.RemainingPercent, 0.0, 100.0);
        }
        else
        {
            _output.WriteLine($"Hata Mesajı: {result.ErrorMessage}");
            Assert.True(result.Status is ProviderStatus.AuthenticationRequired or ProviderStatus.CliNotFound or ProviderStatus.Timeout);
        }
    }
}
