using System;
using System.Threading;
using System.Threading.Tasks;
using UsageGauge.Models;
using UsageGauge.Providers;
using UsageGauge.Services;
using UsageGauge.ViewModels;
using Xunit;

namespace UsageGauge.Tests.Services;

/// <summary>
/// T07.2 — Saatler boyunca kota ve sayaç doğruluğu simülasyon testleri.
/// Kontrol edilebilir sanal saat ve sahte sağlayıcılar kullanarak:
/// 1. Sağlayıcıdan yeni yanıt gelmedikçe yüzde değerinin kesinlikle sabit kaldığını (asla yapay azaltılmadığını),
/// 2. Geri sayımın mutlak UTC sıfırlanma zamanından yerel saate bağımsız hesaplandığını,
/// 3. Sıfırlanma zamanı dolduğunda yüzdenin kendiliğinden %100'e fırlamadığını (eski verinin korunduğunu),
/// 4. Yalnızca izin verilen 10 dakikalık periyot ve 10/15/20/30 dakikalık geri çekilme (backoff) kurallarıyla sorgu yapıldığını
/// doğrular.
/// </summary>
public class MultiHourLifecycleSimulationTests
{
    private sealed class ControllableChatGptProvider : IChatGptProvider
    {
        public Func<CancellationToken, Task<ChatGptQuotaResult>>? FetchHandler { get; set; }
        public int CallCount { get; private set; }

        public Task<ChatGptQuotaResult> FetchQuotaAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (FetchHandler != null)
            {
                return FetchHandler(cancellationToken);
            }

            var fiveHour = new QuotaWindow("codex-5h", "5 Saatlik", 300, 50.0, DateTimeOffset.UtcNow.AddHours(4));
            var weekly = new QuotaWindow("codex-weekly", "Haftalık", 10080, 75.0, DateTimeOffset.UtcNow.AddDays(5));
            var group = new QuotaGroup("chatgpt-codex", "ChatGPT Work / Codex", fiveHour, weekly, ProviderStatus.Success);
            return Task.FromResult(new ChatGptQuotaResult(group, ProviderStatus.Success));
        }
    }

    private sealed class ControllableAntigravityProvider : IAntigravityProvider
    {
        public Func<CancellationToken, Task<AntigravityQuotaResult>>? FetchHandler { get; set; }
        public int CallCount { get; private set; }

        public Task<AntigravityQuotaResult> FetchQuotaAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (FetchHandler != null)
            {
                return FetchHandler(cancellationToken);
            }

            var g5h = new QuotaWindow("gemini-5h", "5 Saatlik", 300, 80.0, DateTimeOffset.UtcNow.AddHours(3));
            var gWeekly = new QuotaWindow("gemini-weekly", "Haftalık", 10080, 85.0, DateTimeOffset.UtcNow.AddDays(4));
            var gemini = new QuotaGroup("antigravity-gemini", "Antigravity Gemini", g5h, gWeekly, ProviderStatus.Success);
            var tp = new QuotaGroup("antigravity-3p", "Antigravity Claude / GPT", g5h, gWeekly, ProviderStatus.Success);

            return Task.FromResult(new AntigravityQuotaResult(gemini, tp, ProviderStatus.Success, 0, 0));
        }
    }

    [Fact]
    public async Task MultiHourProgression_MaintainsStrictPercentage_AndCalculatesUtcCountdownAccurately()
    {
        // T0: 2026-11-03 10:00:00 UTC
        DateTimeOffset t0 = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset currentTime = t0;

        DateTimeOffset resetTime5h = t0.AddHours(4).AddMinutes(30); // 14:30:00 UTC (4 saat 30 dk sonra)
        DateTimeOffset resetTimeWeekly = t0.AddDays(5);             // 5 gün sonra

        var chatProvider = new ControllableChatGptProvider();
        var agyProvider = new ControllableAntigravityProvider();

        int chatCallCount = 0;
        double currentReportedPercent = 42.0;
        bool shouldFailAtReset = false;

        chatProvider.FetchHandler = _ =>
        {
            chatCallCount++;
            if (shouldFailAtReset)
            {
                // Reset anında veya sonrasında hata dön (zaman aşımı simülasyonu)
                return Task.FromResult(ChatGptQuotaResult.CreateError(ProviderStatus.Timeout, "Ağ zaman aşımı"));
            }

            var fiveHour = new QuotaWindow("codex-5h", "5 Saatlik", 300, currentReportedPercent, resetTime5h);
            var weekly = new QuotaWindow("codex-weekly", "Haftalık", 10080, 88.0, resetTimeWeekly);
            var group = new QuotaGroup("chatgpt-codex", "ChatGPT Work / Codex", fiveHour, weekly, ProviderStatus.Success);
            return Task.FromResult(new ChatGptQuotaResult(group, ProviderStatus.Success));
        };

        using var scheduler = new QuotaScheduler(chatProvider, agyProvider, () => currentTime);
        using var viewModel = new MainWindowViewModel(scheduler, () => currentTime);

        // 1. T0'da ilk açılış sorgusu
        await viewModel.StartInitialRefreshAsync();

        Assert.Equal(1, chatCallCount);
        Assert.Equal(42.0, viewModel.ChatGptCard.FiveHourWindow.RemainingPercent);
        Assert.Equal("%42", viewModel.ChatGptCard.FiveHourWindow.RemainingPercentText);
        Assert.Equal("Sıfırlanma: 4s 30dk", viewModel.ChatGptCard.FiveHourWindow.CountdownText);

        // 2. T0 ile T0 + 9 dakika arasında her dakika tick atılır:
        // 10 dakikalık periyot dolmadığı için ASLA yeni sorgu yapılmamalı, yüzde KESİNLİKLE %42 kalmalı
        for (int m = 1; m <= 9; m++)
        {
            currentTime = t0.AddMinutes(m);
            viewModel.Tick(currentTime);

            Assert.Equal(1, chatCallCount); // Yeni sorgu yok
            Assert.Equal(42.0, viewModel.ChatGptCard.FiveHourWindow.RemainingPercent); // Yüzde sabit
            Assert.Equal($"Sıfırlanma: 4s {30 - m}dk", viewModel.ChatGptCard.FiveHourWindow.CountdownText);
        }

        // 3. T0 + 10 dakika (10:10:00): 10 dakikalık planlı periyodik sorgu tetiklenir
        currentTime = t0.AddMinutes(10);
        viewModel.Tick(currentTime);

        Assert.Equal(2, chatCallCount); // 10. dakikada 2. sorgu yapıldı
        Assert.Equal(42.0, viewModel.ChatGptCard.FiveHourWindow.RemainingPercent);
        Assert.Equal("Sıfırlanma: 4s 20dk", viewModel.ChatGptCard.FiveHourWindow.CountdownText);

        // 4. Zamanı 4 saat 29 dakikaya ilerlet (14:29:00):
        // Sayaç 1dk olmalı, yüzde hâlâ %42
        currentTime = resetTime5h.AddMinutes(-1);
        viewModel.Tick(currentTime);

        Assert.Equal(42.0, viewModel.ChatGptCard.FiveHourWindow.RemainingPercent);
        Assert.Equal("Sıfırlanma: 1dk", viewModel.ChatGptCard.FiveHourWindow.CountdownText);

        // 5. 14:30:00: SIFIRLANMA ZAMANI DOLDU!
        shouldFailAtReset = true; // Sağlayıcı sorgusu başarısız olacak
        int callsBeforeReset = chatCallCount;

        currentTime = resetTime5h;
        viewModel.Tick(currentTime);

        // Erken sorgu tetiklenmiş olmalı
        Assert.Equal(callsBeforeReset + 1, chatCallCount);

        // Sorgu başarısız olduğu için:
        // - Yüzde ASLA kendiliğinden %100'e çekilmemeli, önceki geçerli %42 değeri korunmalı (IsStale=true)
        Assert.Equal(42.0, viewModel.ChatGptCard.FiveHourWindow.RemainingPercent);
        Assert.True(viewModel.ChatGptCard.IsStale);
        Assert.Contains("Eski Veri", viewModel.ChatGptCard.StatusText);
        Assert.Equal(QuotaTimeFormatter.ExpiredText, viewModel.ChatGptCard.FiveHourWindow.CountdownText);

        // 6. 14:30:01 ile 14:39:59 arası (sonraki 10 dakika):
        // Her saniye tick atılsa dahi mükerrer sorgu OLMAMALI (backoff 10 dk beklenir)
        for (int m = 1; m < 10; m++)
        {
            currentTime = resetTime5h.AddMinutes(m);
            viewModel.Tick(currentTime);
        }
        Assert.Equal(callsBeforeReset + 1, chatCallCount); // Kesinlikle sabit kalmalı
        Assert.Equal(42.0, viewModel.ChatGptCard.FiveHourWindow.RemainingPercent);

        // 7. 14:40:00 (10 dakikalık backoff doldu): Sağlayıcı düzelir ve %100 döner
        shouldFailAtReset = false;
        currentReportedPercent = 100.0;
        resetTime5h = resetTime5h.AddHours(5); // Yeni 5 saatlik periyot

        currentTime = resetTime5h.AddHours(-5).AddMinutes(10);
        viewModel.Tick(currentTime);

        Assert.Equal(callsBeforeReset + 2, chatCallCount);
        // Yeni veriyle birlikte kota güncellendi
        Assert.False(viewModel.ChatGptCard.IsStale);
        Assert.Equal("✓ Güncel", viewModel.ChatGptCard.StatusText);
        Assert.Equal(100.0, viewModel.ChatGptCard.FiveHourWindow.RemainingPercent);
        Assert.Equal("%100", viewModel.ChatGptCard.FiveHourWindow.RemainingPercentText);
        Assert.Equal("Sıfırlanma: 4s 50dk", viewModel.ChatGptCard.FiveHourWindow.CountdownText);
    }

    [Fact]
    public async Task ManualRefresh_DuringMultiHourRun_UpdatesCleanlyWithoutAffectingCountdownIntegrity()
    {
        DateTimeOffset t0 = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset currentTime = t0;
        DateTimeOffset resetTime5h = t0.AddHours(4);

        var chatProvider = new ControllableChatGptProvider();
        var agyProvider = new ControllableAntigravityProvider();

        int chatCallCount = 0;
        double dynamicPercent = 60.0;

        chatProvider.FetchHandler = _ =>
        {
            chatCallCount++;
            var fiveHour = new QuotaWindow("codex-5h", "5 Saatlik", 300, dynamicPercent, resetTime5h);
            var weekly = new QuotaWindow("codex-weekly", "Haftalık", 10080, 90.0, resetTime5h.AddDays(4));
            var group = new QuotaGroup("chatgpt-codex", "ChatGPT Work / Codex", fiveHour, weekly, ProviderStatus.Success);
            return Task.FromResult(new ChatGptQuotaResult(group, ProviderStatus.Success));
        };

        using var scheduler = new QuotaScheduler(chatProvider, agyProvider, () => currentTime);
        using var viewModel = new MainWindowViewModel(scheduler, () => currentTime);

        // 1. T0'da başlat
        await viewModel.StartInitialRefreshAsync();
        Assert.Equal(1, chatCallCount);
        Assert.Equal(60.0, viewModel.ChatGptCard.FiveHourWindow.RemainingPercent);
        Assert.Equal("Sıfırlanma: 4s 0dk", viewModel.ChatGptCard.FiveHourWindow.CountdownText);

        // 2. T0 + 5 dakika: 10 dakikalık periyot dolmadığı için tick ile sorgu tetiklenmez
        currentTime = t0.AddMinutes(5);
        viewModel.Tick(currentTime);

        Assert.Equal(1, chatCallCount);
        Assert.Equal("Sıfırlanma: 3s 55dk", viewModel.ChatGptCard.FiveHourWindow.CountdownText);
        Assert.Equal(60.0, viewModel.ChatGptCard.FiveHourWindow.RemainingPercent);

        // 3. Kullanıcı elle yenilemeye bastı (model kullanıldı, kota %50'ye düştü)
        dynamicPercent = 50.0;
        await viewModel.TriggerManualRefreshAsync();

        Assert.Equal(2, chatCallCount);
        Assert.Equal(50.0, viewModel.ChatGptCard.FiveHourWindow.RemainingPercent);
        Assert.Equal("%50", viewModel.ChatGptCard.FiveHourWindow.RemainingPercentText);
        Assert.Equal("Sıfırlanma: 3s 55dk", viewModel.ChatGptCard.FiveHourWindow.CountdownText);
    }

    [Fact]
    public async Task MultiHour_WhenAntigravityHalted_PreservesGeminiStatusWhileChatGptAdvancesAcrossHours()
    {
        DateTimeOffset t0 = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset currentTime = t0;

        var chatProvider = new ControllableChatGptProvider();
        var agyProvider = new ControllableAntigravityProvider
        {
            FetchHandler = _ => Task.FromResult(AntigravityQuotaResult.CreateError(
                ProviderStatus.ModelTurnDetected,
                "Model turu algılandı",
                numTurns: 1,
                totalTokens: 25))
        };

        using var scheduler = new QuotaScheduler(chatProvider, agyProvider, () => currentTime);
        using var viewModel = new MainWindowViewModel(scheduler, () => currentTime);

        await viewModel.StartInitialRefreshAsync();

        Assert.True(scheduler.IsAntigravityHalted);
        Assert.Equal("⛔ Model Turu Engellendi", viewModel.GeminiCard.StatusText);
        int agyCallsAtStart = agyProvider.CallCount;

        // 2 saat boyunca her 10 dakikada tick at
        for (int m = 10; m <= 120; m += 10)
        {
            currentTime = t0.AddMinutes(m);
            viewModel.Tick(currentTime);
        }

        // Antigravity ASLA tekrar tetiklenmemeli
        Assert.Equal(agyCallsAtStart, agyProvider.CallCount);
        Assert.Equal("⛔ Model Turu Engellendi", viewModel.GeminiCard.StatusText);

        // ChatGPT ise normal şekilde periyodik olarak sorgulanmaya devam etmeli
        Assert.True(chatProvider.CallCount > 1);
        Assert.Equal("✓ Güncel", viewModel.ChatGptCard.StatusText);
    }
}
