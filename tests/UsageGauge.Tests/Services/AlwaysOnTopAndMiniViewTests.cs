using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using UsageGauge.Models;
using UsageGauge.Native;
using UsageGauge.Services;
using UsageGauge.ViewModels;
using Xunit;

namespace UsageGauge.Tests.Services;

public class AlwaysOnTopAndMiniViewTests
{
    private static void RunOnSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception != null)
        {
            throw new AggregateException("STA thread exception", exception);
        }
    }

    [Fact]
    public void ConfigService_BackwardCompatibility_WhenNewFieldsMissing_ReturnsDefaults()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"usagegauge_compat_{Guid.NewGuid():N}.json");
        try
        {
            // Eski sürüm JSON: IsAlwaysOnTop ve ViewMode alanları yok
            string legacyJson = """
            {
              "WindowLeft": 320.0,
              "WindowTop": 180.0,
              "MonitorDeviceName": "\\\\.\\DISPLAY1",
              "Opacity": 0.8
            }
            """;
            File.WriteAllText(tempPath, legacyJson);

            var service = new ConfigService(tempPath);
            AppConfig config = service.LoadConfig();

            Assert.Equal(320.0, config.WindowLeft);
            Assert.Equal(180.0, config.WindowTop);
            Assert.Equal(@"\\.\DISPLAY1", config.MonitorDeviceName);
            Assert.Equal(0.8, config.Opacity);
            Assert.False(config.IsAlwaysOnTop);
            Assert.Equal("Normal", config.ViewMode);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void ConfigService_BackwardCompatibility_WhenLegacyV1MiniInConfig_ConvertsToOrta()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"usagegauge_compat_v1_{Guid.NewGuid():N}.json");
        try
        {
            // Sürüm 1 config dosyasında ConfigVersion yok (veya 1) ve ViewMode "Mini" (eski 270 px görünüm)
            string legacyJson = """
            {
              "WindowLeft": 200.0,
              "WindowTop": 150.0,
              "ViewMode": "Mini"
            }
            """;
            File.WriteAllText(tempPath, legacyJson);

            var service = new ConfigService(tempPath);
            AppConfig config = service.LoadConfig();

            // Sürüm 1'deki "Mini", 270 px "Orta" görünümüne göç etmeli
            Assert.Equal("Orta", config.ViewMode);
            Assert.Equal(3, config.ConfigVersion);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void ConfigService_BackwardCompatibility_WhenV2MiniInConfig_ConvertsToOrtaMini()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"usagegauge_compat_v2_{Guid.NewGuid():N}.json");
        try
        {
            // Sürüm 2 config dosyasında ConfigVersion: 2 ve ViewMode "Mini" (200 px kompakt görünüm)
            string v2Json = """
            {
              "ConfigVersion": 2,
              "WindowLeft": 200.0,
              "WindowTop": 150.0,
              "ViewMode": "Mini"
            }
            """;
            File.WriteAllText(tempPath, v2Json);

            var service = new ConfigService(tempPath);
            AppConfig config = service.LoadConfig();

            // Sürüm 2'deki "Mini", 200 px "Orta-Mini" görünümüne göç etmeli
            Assert.Equal("Orta-Mini", config.ViewMode);
            Assert.Equal(3, config.ConfigVersion);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void ConfigService_V3Mini_LoadsDirectlyAsMini()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"usagegauge_compat_v3_{Guid.NewGuid():N}.json");
        try
        {
            // Sürüm 3 config dosyasında ConfigVersion: 3 ve ViewMode "Mini" (yeni yatay Mini görünüm)
            string v3Json = """
            {
              "ConfigVersion": 3,
              "WindowLeft": 200.0,
              "WindowTop": 150.0,
              "ViewMode": "Mini"
            }
            """;
            File.WriteAllText(tempPath, v3Json);

            var service = new ConfigService(tempPath);
            AppConfig config = service.LoadConfig();

            // Sürüm 3'teki "Mini", doğrudan "Mini" olarak yüklenmeli
            Assert.Equal("Mini", config.ViewMode);
            Assert.Equal(3, config.ConfigVersion);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Theory]
    [InlineData("mini", 3, "Mini")]
    [InlineData("Mini", 3, "Mini")]
    [InlineData("MINI", 3, "Mini")]
    [InlineData("mini", 2, "Orta-Mini")]
    [InlineData("Mini", 2, "Orta-Mini")]
    [InlineData("mini", 1, "Orta")]
    [InlineData("Mini", 1, "Orta")]
    [InlineData("orta-mini", 3, "Orta-Mini")]
    [InlineData("Orta-Mini", 3, "Orta-Mini")]
    [InlineData("Medium-Mini", 3, "Orta-Mini")]
    [InlineData("orta", 3, "Orta")]
    [InlineData("Orta", 3, "Orta")]
    [InlineData("ORTA", 3, "Orta")]
    [InlineData("medium", 3, "Orta")]
    [InlineData("Medium", 3, "Orta")]
    [InlineData("normal", 3, "Normal")]
    [InlineData("Normal", 3, "Normal")]
    [InlineData("NORMAL", 3, "Normal")]
    [InlineData("InvalidValue", 3, "Normal")]
    [InlineData("", 3, "Normal")]
    [InlineData(null, 3, "Normal")]
    public void ConfigService_SanitizeViewMode_HandlesVariationsAndInvalidInputs(string? inputMode, int version, string expectedMode)
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"usagegauge_mode_{Guid.NewGuid():N}.json");
        try
        {
            string json = $$"""
            {
              "ConfigVersion": {{version}},
              "WindowLeft": 100.0,
              "WindowTop": 100.0,
              "ViewMode": {{(inputMode == null ? "null" : $"\"{inputMode}\"")}}
            }
            """;
            File.WriteAllText(tempPath, json);

            var service = new ConfigService(tempPath);
            AppConfig config = service.LoadConfig();

            Assert.Equal(expectedMode, config.ViewMode);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void ConfigService_SaveAndLoad_PreservesAlwaysOnTopAndViewMode()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"usagegauge_persist_{Guid.NewGuid():N}.json");
        try
        {
            var service = new ConfigService(tempPath);
            var initial = new AppConfig
            {
                WindowLeft = 450,
                WindowTop = 220,
                MonitorDeviceName = @"\\.\DISPLAY2",
                Opacity = 0.6,
                IsAlwaysOnTop = true,
                ViewMode = "Mini"
            };

            service.SaveConfig(initial);

            AppConfig loaded = service.LoadConfig();
            Assert.True(loaded.IsAlwaysOnTop);
            Assert.Equal("Mini", loaded.ViewMode);
            Assert.Equal(3, loaded.ConfigVersion);
            Assert.Equal(450, loaded.WindowLeft);
            Assert.Equal(220, loaded.WindowTop);
            Assert.Equal(@"\\.\DISPLAY2", loaded.MonitorDeviceName);
            Assert.Equal(0.6, loaded.Opacity);

            // Şimdi Orta modunu kaydet ve yükle
            var mediumConfig = loaded with { ViewMode = "Orta" };
            service.SaveConfig(mediumConfig);

            AppConfig loadedMedium = service.LoadConfig();
            Assert.Equal("Orta", loadedMedium.ViewMode);
            Assert.Equal(3, loadedMedium.ConfigVersion);

            // Şimdi Orta-Mini modunu kaydet ve yükle
            var mediumMiniConfig = loaded with { ViewMode = "Orta-Mini" };
            service.SaveConfig(mediumMiniConfig);

            AppConfig loadedMediumMini = service.LoadConfig();
            Assert.Equal("Orta-Mini", loadedMediumMini.ViewMode);
            Assert.Equal(3, loadedMediumMini.ConfigVersion);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void QuotaTimeFormatter_FormatShortCountdown_FormatsExpectedCompactStrings()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-11-04T12:00:00Z");

        // Null
        Assert.Equal("—", QuotaTimeFormatter.FormatShortCountdown(null, now));

        // Expired (geçmişte)
        DateTimeOffset past = now.AddMinutes(-5);
        Assert.Equal("Doldu", QuotaTimeFormatter.FormatShortCountdown(past, now));

        // Gün ve Saat (6 gün, 3 saat, 20 dk)
        DateTimeOffset daysHours = now.AddDays(6).AddHours(3).AddMinutes(20);
        Assert.Equal("6g 3s", QuotaTimeFormatter.FormatShortCountdown(daysHours, now));

        // Saat ve Dakika (4 saat, 27 dk)
        DateTimeOffset hoursMinutes = now.AddHours(4).AddMinutes(27);
        Assert.Equal("4s 27d", QuotaTimeFormatter.FormatShortCountdown(hoursMinutes, now));

        // Sadece Dakika (42 dk)
        DateTimeOffset minutesOnly = now.AddMinutes(42).AddSeconds(15);
        Assert.Equal("42d", QuotaTimeFormatter.FormatShortCountdown(minutesOnly, now));

        // 1 dakikadan az (25 sn)
        DateTimeOffset lessThanOneMin = now.AddSeconds(25);
        Assert.Equal("<1d", QuotaTimeFormatter.FormatShortCountdown(lessThanOneMin, now));
    }

    [Fact]
    public void QuotaWindowViewModel_ShortCountdownText_UpdatesOnWindowChangeAndTick()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-11-04T12:00:00Z");
        var vm = new QuotaWindowViewModel("5 Saat");

        var window = new QuotaWindow(
            "codex_5h",
            "5 Saat",
            300,
            80.0,
            now.AddHours(2).AddMinutes(15));

        vm.UpdateFromWindow(window, isStale: false, now);
        Assert.Equal("2s 15d", vm.ShortCountdownText);
        Assert.Contains("Sıfırlanma: 2s 15dk", vm.CountdownText);

        // Zaman ilerlediğinde (1 saat sonra)
        DateTimeOffset later = now.AddHours(1);
        vm.UpdateCountdown(later);
        Assert.Equal("1s 15d", vm.ShortCountdownText);

        // Sıfırlanma zamanı geçtiğinde
        DateTimeOffset afterExpiry = now.AddHours(3);
        vm.UpdateCountdown(afterExpiry);
        Assert.Equal("Doldu", vm.ShortCountdownText);
    }

    [Fact]
    public void EnsureVisibleBounds_PreservesDominantMonitor_WhenSwitchingModesOnSecondaryMonitor()
    {
        var monitors = new List<MonitorDescriptor>
        {
            new MonitorDescriptor(@"\\.\DISPLAY1", "Display 1 (Primary)", new Rect(0, 0, 1920, 1080), IsPrimary: true),
            new MonitorDescriptor(@"\\.\DISPLAY2", "Display 2 (Secondary)", new Rect(1920, 0, 1920, 1080), IsPrimary: false)
        };

        // İkinci ekranda yer alan Normal görünüm penceresi (340x460)
        double initialLeft = 2100;
        double initialTop = 150;

        // Orta görünüme geçiş (270x170) -> İkinci ekranda kalmalı
        var (ortaLeft, ortaTop) = ConfigService.EnsureVisibleBounds(
            left: initialLeft,
            top: initialTop,
            width: MainWindow.MediumWidth,
            height: 170,
            monitors: monitors,
            preferredDeviceName: @"\\.\DISPLAY2");

        Assert.InRange(ortaLeft, 1920, 1920 + 1920 - 240);
        Assert.Equal(initialLeft, ortaLeft);
        Assert.Equal(initialTop, ortaTop);

        // Mini görünüme geçiş (200x140) -> İkinci ekranda kalmalı
        var (miniLeft, miniTop) = ConfigService.EnsureVisibleBounds(
            left: ortaLeft,
            top: ortaTop,
            width: MainWindow.MiniWidth,
            height: 140,
            monitors: monitors,
            preferredDeviceName: @"\\.\DISPLAY2");

        Assert.InRange(miniLeft, 1920, 1920 + 1920 - 200);
        Assert.Equal(initialLeft, miniLeft);
        Assert.Equal(initialTop, miniTop);

        // Tekrar Normal görünüme geçiş (340x460) -> İkinci ekranda kalmalı
        var (normLeft, normTop) = ConfigService.EnsureVisibleBounds(
            left: miniLeft,
            top: miniTop,
            width: MainWindow.NormalWidth,
            height: 460,
            monitors: monitors,
            preferredDeviceName: @"\\.\DISPLAY2");

        Assert.InRange(normLeft, 1920, 1920 + 1920 - 240);
        Assert.Equal(initialLeft, normLeft);
    }

    [Fact]
    public void MainWindow_ApplyViewMode_TogglesLayoutWidthAndCheckmarks()
    {
        RunOnSta(() =>
        {
            var window = new MainWindow();
            try
            {
                // 1. Varsayılan mod Normal olmalı (340 px)
                Assert.True(window.IsNormalView);
                Assert.False(window.IsMediumView);
                Assert.False(window.IsMediumMiniView);
                Assert.False(window.IsMiniView);
                Assert.Equal(MainWindow.NormalWidth, window.Width);
                Assert.Equal(Visibility.Visible, window.NormalViewPanel.Visibility);
                Assert.Equal(Visibility.Collapsed, window.MediumViewPanel.Visibility);
                Assert.Equal(Visibility.Collapsed, window.MediumMiniViewPanel.Visibility);
                Assert.Equal(Visibility.Collapsed, window.MiniViewPanel.Visibility);
                Assert.True(window.MenuViewNormal.IsChecked);
                Assert.False(window.MenuViewMedium.IsChecked);
                Assert.False(window.MenuViewMediumMini.IsChecked);
                Assert.False(window.MenuViewMini.IsChecked);

                // 2. Orta görünüme geç (270 px)
                window.ApplyViewMode("Orta", saveConfig: false);
                Assert.False(window.IsNormalView);
                Assert.True(window.IsMediumView);
                Assert.False(window.IsMediumMiniView);
                Assert.False(window.IsMiniView);
                Assert.Equal(MainWindow.MediumWidth, window.Width);
                Assert.Equal(Visibility.Collapsed, window.NormalViewPanel.Visibility);
                Assert.Equal(Visibility.Visible, window.MediumViewPanel.Visibility);
                Assert.Equal(Visibility.Collapsed, window.MediumMiniViewPanel.Visibility);
                Assert.Equal(Visibility.Collapsed, window.MiniViewPanel.Visibility);
                Assert.False(window.MenuViewNormal.IsChecked);
                Assert.True(window.MenuViewMedium.IsChecked);
                Assert.False(window.MenuViewMediumMini.IsChecked);
                Assert.False(window.MenuViewMini.IsChecked);

                // 3. Orta-Mini görünüme geç (200 px)
                window.ApplyViewMode("Orta-Mini", saveConfig: false);
                Assert.False(window.IsNormalView);
                Assert.False(window.IsMediumView);
                Assert.True(window.IsMediumMiniView);
                Assert.False(window.IsMiniView);
                Assert.Equal(MainWindow.MediumMiniWidth, window.Width);
                Assert.Equal(Visibility.Collapsed, window.NormalViewPanel.Visibility);
                Assert.Equal(Visibility.Collapsed, window.MediumViewPanel.Visibility);
                Assert.Equal(Visibility.Visible, window.MediumMiniViewPanel.Visibility);
                Assert.Equal(Visibility.Collapsed, window.MiniViewPanel.Visibility);
                Assert.False(window.MenuViewNormal.IsChecked);
                Assert.False(window.MenuViewMedium.IsChecked);
                Assert.True(window.MenuViewMediumMini.IsChecked);
                Assert.False(window.MenuViewMini.IsChecked);

                // 4. Yeni Mini görünüme geç (340 px yatay kompakt)
                window.ApplyViewMode("Mini", saveConfig: false);
                Assert.False(window.IsNormalView);
                Assert.False(window.IsMediumView);
                Assert.False(window.IsMediumMiniView);
                Assert.True(window.IsMiniView);
                Assert.Equal(MainWindow.MiniWidth, window.Width);
                Assert.Equal(Visibility.Collapsed, window.NormalViewPanel.Visibility);
                Assert.Equal(Visibility.Collapsed, window.MediumViewPanel.Visibility);
                Assert.Equal(Visibility.Collapsed, window.MediumMiniViewPanel.Visibility);
                Assert.Equal(Visibility.Visible, window.MiniViewPanel.Visibility);
                Assert.False(window.MenuViewNormal.IsChecked);
                Assert.False(window.MenuViewMedium.IsChecked);
                Assert.False(window.MenuViewMediumMini.IsChecked);
                Assert.True(window.MenuViewMini.IsChecked);

                // 5. Tekrar Normal görünüme dön (340 px)
                window.ApplyViewMode("Normal", saveConfig: false);
                Assert.True(window.IsNormalView);
                Assert.False(window.IsMediumView);
                Assert.False(window.IsMediumMiniView);
                Assert.False(window.IsMiniView);
                Assert.Equal(MainWindow.NormalWidth, window.Width);
                Assert.Equal(Visibility.Visible, window.NormalViewPanel.Visibility);
                Assert.Equal(Visibility.Collapsed, window.MediumViewPanel.Visibility);
                Assert.Equal(Visibility.Collapsed, window.MediumMiniViewPanel.Visibility);
                Assert.Equal(Visibility.Collapsed, window.MiniViewPanel.Visibility);
                Assert.True(window.MenuViewNormal.IsChecked);
                Assert.False(window.MenuViewMedium.IsChecked);
                Assert.False(window.MenuViewMediumMini.IsChecked);
                Assert.False(window.MenuViewMini.IsChecked);
            }
            finally
            {
                window.ExitApplication();
            }
        });
    }

    [Fact]
    public void MainWindow_MiniView_MeasureActualHeightAndWidth_FitsWithinTargetDimensions()
    {
        RunOnSta(() =>
        {
            var window = new MainWindow();
            try
            {
                // Mini görünümü uygula (340 px)
                window.ApplyViewMode("Mini", saveConfig: false);

                if (window.Content is FrameworkElement root)
                {
                    // MiniWidth (340 px) ile ölç
                    root.Measure(new Size(MainWindow.MiniWidth, double.PositiveInfinity));
                    double desiredH = root.DesiredSize.Height;

                    // Gerçek ölçüm değerlerini konsola yazdır
                    Console.WriteLine($"[MiniView] Width: {MainWindow.MiniWidth:F2} px, DesiredHeight: {desiredH:F2} px");

                    // Genişlik MainWindow.MiniWidth (340 px) olarak sabitlenir, 320–360 px aralığındadır.
                    Assert.Equal(MainWindow.MiniWidth, window.Width);
                    Assert.InRange(window.Width, 320, 360);

                    // Yükseklik hedefi: Tek satır kota alanlı ince yatay panel ~65–88 px
                    Assert.InRange(desiredH, 65, 88);
                }
                else
                {
                    Assert.Equal(MainWindow.MiniWidth, window.Width);
                }
            }
            finally
            {
                window.ExitApplication();
            }
        });
    }

    [Fact]
    public void DesktopWindowManager_SetAlwaysOnTop_WhenActiveWindowIsWidget_ImmediatelyAppliesNotopmost()
    {
        RunOnSta(() =>
        {
            var window = new Window();
            using var manager = new DesktopWindowManager(window);
            IntPtr widgetHwnd = new(0x123456);
            manager.SetHandleForTesting(widgetHwnd);

            var setWindowPosCalls = new List<(IntPtr hwnd, IntPtr insertAfter, uint flags)>();
            manager.SetWindowPosFunc = (hwnd, insertAfter, x, y, cx, cy, flags) =>
            {
                setWindowPosCalls.Add((hwnd, insertAfter, flags));
                return true;
            };

            // Aktif pencere widget'ın kendisi olsun
            manager.GetForegroundWindowFunc = () => widgetHwnd;

            // 1. Her zaman üstte aç
            bool onResult = manager.SetAlwaysOnTop(true);
            Assert.True(onResult);
            Assert.True(manager.IsAlwaysOnTop);
            Assert.Contains(setWindowPosCalls, c => c.hwnd == widgetHwnd && c.insertAfter == NativeMethods.HWND_TOPMOST);

            setWindowPosCalls.Clear();

            // 2. Aktif pencere widget iken her zaman üstteyi kapat
            bool offResult = manager.SetAlwaysOnTop(false);
            Assert.True(offResult);
            Assert.False(manager.IsAlwaysOnTop);

            // HWND_NOTOPMOST hemen ve öncelikli olarak çağrılmış olmalı
            Assert.Contains(setWindowPosCalls, c => c.hwnd == widgetHwnd && c.insertAfter == NativeMethods.HWND_NOTOPMOST);
        });
    }

    [Fact]
    public void MainWindow_SetAlwaysOnTop_WhenWin32Fails_RevertsCheckmarkAndDoesNotShowFakeSuccess()
    {
        RunOnSta(() =>
        {
            var window = new MainWindow();
            try
            {
                IntPtr fakeHwnd = new(0x987654);
                window.WindowManager.SetHandleForTesting(fakeHwnd);

                // SetWindowPos çağrıları başarılıyken önce üstte tutmayı aç
                window.WindowManager.SetWindowPosFunc = (hwnd, insertAfter, x, y, cx, cy, flags) => true;
                bool enableResult = window.SetAlwaysOnTop(true, saveConfig: false);
                Assert.True(enableResult);
                Assert.True(window.IsAlwaysOnTop);
                Assert.True(window.MenuAlwaysOnTop.IsChecked);

                // Şimdi Win32 SetWindowPos başarısızlık üretsin
                window.WindowManager.SetWindowPosFunc = (hwnd, insertAfter, x, y, cx, cy, flags) => false;

                // Kapatma işlemi başarısız olduğunda
                bool disableResult = window.SetAlwaysOnTop(false, saveConfig: false);
                Assert.False(disableResult);

                // Menü işareti ve durum sahte başarı göstermemeli; açık (true) kalmalı
                Assert.True(window.IsAlwaysOnTop);
                Assert.True(window.MenuAlwaysOnTop.IsChecked);
                Assert.True(window.WindowManager.IsAlwaysOnTop);
            }
            finally
            {
                window.ExitApplication();
            }
        });
    }

    [Fact]
    public void MainWindow_ApplyViewMode_WhenWindowStraddlesDualMonitorBoundary_PreservesMonitorAndDoesNotJump()
    {
        RunOnSta(() =>
        {
            var window = new MainWindow();
            try
            {
                var monitors = new List<MonitorDescriptor>
                {
                    new MonitorDescriptor(@"\\.\DISPLAY1", "Display 1 (Primary)", new Rect(0, 0, 1920, 1080), IsPrimary: true),
                    new MonitorDescriptor(@"\\.\DISPLAY2", "Display 2 (Secondary)", new Rect(1920, 0, 1920, 1080), IsPrimary: false)
                };
                window.MonitorsProvider = () => monitors;

                // SENARYO A: Pencere iki monitör sınırını kesiyor, ancak çoğunlukla Monitör 2'de (Left = 1880, NormalWidth = 340)
                // 1880..1920 (40 px) Monitör 1'de; 1920..2220 (300 px) Monitör 2'de -> Baskın monitör DISPLAY2
                window.Left = 1880;
                window.Top = 100;
                window.Width = MainWindow.NormalWidth;

                // 1. Orta görünüme geç (270 px)
                window.ApplyViewMode("Orta", saveConfig: false);
                Assert.True(window.IsMediumView);
                Assert.Equal(MainWindow.MediumWidth, window.Width);
                Assert.True(window.Left >= 1920, $"Expected Left >= 1920, but was {window.Left}");
                Assert.InRange(window.Left, 1920, 3840 - 240);

                // 2. Orta-Mini görünüme geç (200 px)
                window.ApplyViewMode("Orta-Mini", saveConfig: false);
                Assert.True(window.IsMediumMiniView);
                Assert.Equal(MainWindow.MediumMiniWidth, window.Width);
                Assert.True(window.Left >= 1920, $"Expected Left >= 1920, but was {window.Left}");
                Assert.InRange(window.Left, 1920, 3840 - 200);

                // 3. Mini görünüme geç (340 px yatay kompakt)
                window.ApplyViewMode("Mini", saveConfig: false);
                Assert.True(window.IsMiniView);
                Assert.Equal(MainWindow.MiniWidth, window.Width);
                Assert.True(window.Left >= 1920, $"Expected Left >= 1920, but was {window.Left}");
                Assert.InRange(window.Left, 1920, 3840 - 240);

                // 4. Tekrar Normal görünüme dön (340 px)
                window.ApplyViewMode("Normal", saveConfig: false);
                Assert.True(window.IsNormalView);
                Assert.Equal(MainWindow.NormalWidth, window.Width);
                Assert.True(window.Left >= 1920, $"Expected Left >= 1920, but was {window.Left}");
                Assert.InRange(window.Left, 1920, 3840 - 240);

                // SENARYO B: Pencere iki monitör sınırını kesiyor, ancak çoğunlukla Monitör 1'de (Left = 1700, NormalWidth = 340)
                // 1700..1920 (220 px) Monitör 1'de; 1920..2040 (120 px) Monitör 2'de -> Baskın monitör DISPLAY1
                window.Left = 1700;
                window.Top = 100;

                // 1. Orta görünüme geç (270 px)
                window.ApplyViewMode("Orta", saveConfig: false);
                Assert.True(window.IsMediumView);
                Assert.Equal(MainWindow.MediumWidth, window.Width);
                // Orta görünüm (270 px) için sınır: 1920 - Math.Min(270, 240) = 1680
                Assert.True(window.Left <= 1920 - 240, $"Expected Left <= 1680, but was {window.Left}");
                Assert.InRange(window.Left, 0, 1920 - 240);

                // 2. Orta-Mini görünüme geç (200 px)
                window.ApplyViewMode("Orta-Mini", saveConfig: false);
                Assert.True(window.IsMediumMiniView);
                Assert.Equal(MainWindow.MediumMiniWidth, window.Width);
                // Orta-Mini görünüm (200 px) için sınır: 1920 - Math.Min(200, 240) = 1720
                Assert.True(window.Left <= 1920 - 200, $"Expected Left <= 1720, but was {window.Left}");
                Assert.InRange(window.Left, 0, 1920 - 200);

                // 3. Mini görünüme geç (340 px yatay kompakt)
                window.ApplyViewMode("Mini", saveConfig: false);
                Assert.True(window.IsMiniView);
                Assert.Equal(MainWindow.MiniWidth, window.Width);
                Assert.True(window.Left <= 1920 - 240, $"Expected Left <= 1680, but was {window.Left}");
                Assert.InRange(window.Left, 0, 1920 - 240);

                // 4. Tekrar Normal görünüme dön (340 px)
                window.ApplyViewMode("Normal", saveConfig: false);
                Assert.True(window.IsNormalView);
                Assert.Equal(MainWindow.NormalWidth, window.Width);
                Assert.True(window.Left <= 1920 - 240, $"Expected Left <= 1680, but was {window.Left}");
                Assert.InRange(window.Left, 0, 1920 - 240);
            }
            finally
            {
                window.ExitApplication();
            }
        });
    }

    [Fact]
    public void MainWindow_SetAlwaysOnTop_TogglesStateCheckmarkAndWindowManager()
    {
        RunOnSta(() =>
        {
            var window = new MainWindow();
            try
            {
                // Varsayılan kapalı olmalı
                Assert.False(window.IsAlwaysOnTop);
                Assert.False(window.MenuAlwaysOnTop.IsChecked);
                Assert.False(window.WindowManager.IsAlwaysOnTop);

                // Her Zaman Üstte aç
                window.SetAlwaysOnTop(true, saveConfig: false);
                Assert.True(window.IsAlwaysOnTop);
                Assert.True(window.MenuAlwaysOnTop.IsChecked);
                Assert.True(window.WindowManager.IsAlwaysOnTop);

                // Her Zaman Üstte kapat
                window.SetAlwaysOnTop(false, saveConfig: false);
                Assert.False(window.IsAlwaysOnTop);
                Assert.False(window.MenuAlwaysOnTop.IsChecked);
                Assert.False(window.WindowManager.IsAlwaysOnTop);
            }
            finally
            {
                window.ExitApplication();
            }
        });
    }

    [Fact]
    public void SettingsWindow_Singleton_ClickingSettingsAgainDoesNotCreateDuplicate()
    {
        RunOnSta(() =>
        {
            var window = new MainWindow();
            try
            {
                Assert.Null(window.ActiveSettingsWindow);

                // 1. Ayarlar penceresini aç
                window.OpenOrActivateSettingsWindow();
                var firstInstance = window.ActiveSettingsWindow;
                Assert.NotNull(firstInstance);
                Assert.True(firstInstance.IsLoaded || firstInstance.IsVisible);

                // 2. Tekrar açmaya çalış - ikinci kopya oluşmamalı
                window.OpenOrActivateSettingsWindow();
                var secondInstance = window.ActiveSettingsWindow;
                Assert.Same(firstInstance, secondInstance);

                // 3. Pencereyi kapatınca referans temizlenmeli
                firstInstance.Close();
                Assert.Null(window.ActiveSettingsWindow);
            }
            finally
            {
                window.ExitApplication();
            }
        });
    }

    [Fact]
    public void SettingsWindow_ClosesWhenWidgetHidesToTrayOrExits()
    {
        RunOnSta(() =>
        {
            var window = new MainWindow();
            try
            {
                // Sahte tray delegate ile tepsinin başarıyla açılmasını sağla
                window.TrayManager = new TrayIconManager(
                    new IntPtr(0x123),
                    () => {},
                    () => {},
                    () => {},
                    (int msg, ref NativeMethods.NOTIFYICONDATA d) => true);
                window.TrayManager.TryEnsureIconCreated();
                Assert.True(window.IsTrayAvailable);

                // Ayarlar penceresini aç
                window.OpenOrActivateSettingsWindow();
                Assert.NotNull(window.ActiveSettingsWindow);

                // Widget tepsiye gizlenince ayarlar penceresi de kapanmalı
                window.HideToTray();
                Assert.Null(window.ActiveSettingsWindow);
            }
            finally
            {
                window.ExitApplication();
            }
        });
    }

    [Fact]
    public void QuotaCardViewModel_StatusTooltipText_IncludesStatusAndError()
    {
        var card = new UsageGauge.ViewModels.QuotaCardViewModel("chatgpt", "ChatGPT");
        card.StatusText = "Taze";
        card.ErrorMessage = null;
        Assert.Equal("Durum: Taze", card.StatusTooltipText);

        card.ErrorMessage = "API 429 Hata Limiti";
        Assert.Equal("Durum: Taze\nDetay: API 429 Hata Limiti", card.StatusTooltipText);
    }

    [Fact]
    public void SettingsWindow_RadioButtonsAndChecks_SyncAndDelegateProperly()
    {
        RunOnSta(() =>
        {
            var window = new MainWindow();
            try
            {
                window.ApplyViewMode("Normal", saveConfig: false);
                window.SetAlwaysOnTop(false, saveConfig: false);
                window.SetOpacity(1.0, saveConfig: false);

                window.OpenOrActivateSettingsWindow();
                var settings = window.ActiveSettingsWindow;
                Assert.NotNull(settings);

                // Kontrollerin başlangıç durumlarını doğrula
                Assert.True(settings.RadioNormal.IsChecked);
                Assert.False(settings.CheckAlwaysOnTop.IsChecked);
                Assert.True(settings.RadioOpacity100.IsChecked);

                // Ayarlar penceresinden Görünüm Modu değiştir (Mini)
                settings.RadioMini.IsChecked = true;
                settings.RadioMini.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.True(window.IsMiniView);
                Assert.Equal(MainWindow.MiniWidth, window.Width);

                // Ayarlar penceresinden Her Zaman Üstte aç
                settings.CheckAlwaysOnTop.IsChecked = true;
                settings.CheckAlwaysOnTop.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.True(window.IsAlwaysOnTop);

                // Ayarlar penceresinden Opaklık değiştir (%80)
                settings.RadioOpacity80.IsChecked = true;
                settings.RadioOpacity80.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.True(Math.Abs(window.Opacity - 0.8) < 0.05);

                // Kapat
                settings.Close();
                Assert.Null(window.ActiveSettingsWindow);
            }
            finally
            {
                window.ExitApplication();
            }
        });
    }

    [Fact]
    public void MiniView_With100PercentAndLongStatus_MeasuresProperlyWithoutClipping()
    {
        RunOnSta(() =>
        {
            var window = new MainWindow();
            try
            {
                // Mini moda geç
                window.ApplyViewMode("Mini", saveConfig: false);

                var vm = window.DataContext as UsageGauge.ViewModels.MainWindowViewModel;
                Assert.NotNull(vm);

                var miniPanel = window.FindName("MiniViewPanel") as Grid;
                Assert.NotNull(miniPanel);

                // Test edilecek durum metinleri (kısa, uzun, hatalı, özel karakterli)
                string[] testStatuses = new[]
                {
                    "✓ Güncel",
                    "🔑 Oturum Gerekli",
                    "✕ CLI Bulunamadı",
                    "⛔ Model Turu Engellendi",
                    "⏱ Zaman Aşımı",
                    "⚡ Kısmi Veri",
                    "⚠ Okuma Hatası",
                    "⚠ Eski Veri (12:30)",
                    "⚠ Veri Yok",
                    "⟳ Yenileniyor..."
                };

                // Test edilecek sayaç metinleri (%100 ile birlikte en uzun durumlar)
                string[] testCountdowns = new[]
                {
                    "23s 59d", // En uzun saat+dakika
                    "29g 23s", // En uzun gün+saat
                    "4s 25d",
                    "6g 23s",
                    "Doldu",   // Süre dolduğunda
                    "—"        // Sıfırlanma zamanı yokken
                };

                string[] testPercents = new[]
                {
                    "%100", // En geniş 4 karakterli yüzde
                    "%69",
                    "%0"
                };

                foreach (var status in testStatuses)
                {
                    foreach (var countdown in testCountdowns)
                    {
                        foreach (var percent in testPercents)
                        {
                            vm.ChatGptCard.StatusText = status;
                            vm.ChatGptCard.FiveHourWindow.RemainingPercentText = percent;
                            vm.ChatGptCard.FiveHourWindow.ShortCountdownText = countdown;
                            vm.ChatGptCard.WeeklyWindow.RemainingPercentText = percent;
                            vm.ChatGptCard.WeeklyWindow.ShortCountdownText = countdown;

                            vm.GeminiCard.StatusText = status;
                            vm.GeminiCard.FiveHourWindow.RemainingPercentText = percent;
                            vm.GeminiCard.FiveHourWindow.ShortCountdownText = countdown;
                            vm.GeminiCard.WeeklyWindow.RemainingPercentText = percent;
                            vm.GeminiCard.WeeklyWindow.ShortCountdownText = countdown;

                            if (window.Content is FrameworkElement root)
                            {
                                root.Measure(new Size(MainWindow.MiniWidth, double.PositiveInfinity));
                                root.Arrange(new Rect(0, 0, MainWindow.MiniWidth, root.DesiredSize.Height));
                                root.UpdateLayout();

                                double desiredW = root.DesiredSize.Width;
                                double desiredH = root.DesiredSize.Height;

                                // Genişlik ve yükseklik aralığı kontrolü
                                Assert.Equal(MainWindow.MiniWidth, window.Width);
                                Assert.True(desiredW <= MainWindow.MiniWidth, $"Root DesiredSize.Width ({desiredW:F2}) > MiniWidth ({MainWindow.MiniWidth})");
                                Assert.InRange(desiredH, 65, 88);

                                // Sütun genişlikleri ve koordinat sınırları (MiniViewPanel yerel koordinatları)
                                double col0W = miniPanel.ColumnDefinitions[0].ActualWidth;
                                double col1W = miniPanel.ColumnDefinitions[1].ActualWidth;
                                double col2W = miniPanel.ColumnDefinitions[2].ActualWidth;
                                double col3W = miniPanel.ColumnDefinitions[3].ActualWidth;

                                double col0Left = 0;
                                double col0Right = col0W;
                                double col1Left = col0Right;
                                double col1Right = col1Left + col1W;
                                double col2Left = col1Right;
                                double col2Right = col2Left + col2W;
                                double col3Left = col2Right;
                                double col3Right = col3Left + col3W;

                                double panelTotalW = miniPanel.ActualWidth;
                                Assert.True(col3Right <= panelTotalW + 0.5, $"Panel sütun toplamı ({col3Right:F1}) panel genişliğini ({panelTotalW:F1}) aştı!");

                                // Pencere içi sınır: Sol kenar 11 px (1 border + 10 margin), Sağ kenar: MiniWidth - 11
                                double windowInnerRightLimit = MainWindow.MiniWidth - 11;

                                foreach (UIElement child in miniPanel.Children)
                                {
                                    int row = Grid.GetRow(child);
                                    int col = Grid.GetColumn(child);
                                    double colW = miniPanel.ColumnDefinitions[col].ActualWidth;
                                    double colLeft = col switch
                                    {
                                        0 => col0Left,
                                        1 => col1Left,
                                        2 => col2Left,
                                        _ => col3Left
                                    };
                                    double colRight = col switch
                                    {
                                        0 => col0Right,
                                        1 => col1Right,
                                        2 => col2Right,
                                        _ => col3Right
                                    };

                                    double elementContentW;
                                    if (child is StackPanel sp)
                                    {
                                        double sumChildren = 0;
                                        foreach (UIElement spChild in sp.Children)
                                        {
                                            sumChildren += spChild.DesiredSize.Width;
                                        }
                                        elementContentW = sumChildren;
                                    }
                                    else
                                    {
                                        elementContentW = child.DesiredSize.Width;
                                    }

                                    double elementRightInPanel = colLeft + elementContentW;
                                    double elementRightInWindow = 11 + elementRightInPanel;

                                    // 1. Sütun sınır denetimi: Öğenin sağ kenarı kendi sütununun sağ kenarını aşmamalı
                                    Assert.True(elementContentW <= colW + 0.1,
                                        $"Row {row} Col {col} içeriği ({elementContentW:F2} px), sütun sınırını ({colW:F2} px) aştı! Status='{status}', Countdown='{countdown}', Percent='{percent}'");

                                    Assert.True(elementRightInPanel <= colRight + 0.1,
                                        $"Row {row} Col {col} sağ kenarı ({elementRightInPanel:F2} px), sütun sağ sınırını ({colRight:F2} px) aştı!");

                                    // 2. Panel ve Pencere sınır denetimi: Öğenin sağ kenarı pencere iç sınırını aşmamalı
                                    Assert.True(elementRightInPanel <= panelTotalW + 0.1,
                                        $"Row {row} Col {col} sağ kenarı ({elementRightInPanel:F2} px), panel sınırını ({panelTotalW:F2} px) aştı!");

                                    Assert.True(elementRightInWindow <= windowInnerRightLimit + 0.1,
                                        $"Row {row} Col {col} pencere içi sağ kenarı ({elementRightInWindow:F2} px), pencere sınırını ({windowInnerRightLimit:F2} px) aştı!");
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                window.ExitApplication();
            }
        });
    }

    [Fact]
    public void MiniView_DynamicStatusAndAlignment_MixedShortAndLongScenarios()
    {
        RunOnSta(() =>
        {
            var window = new MainWindow();
            try
            {
                window.ApplyViewMode("Mini", saveConfig: false);

                var vm = window.DataContext as UsageGauge.ViewModels.MainWindowViewModel;
                Assert.NotNull(vm);

                var miniPanel = window.FindName("MiniViewPanel") as Grid;
                Assert.NotNull(miniPanel);

                // Kısa ve uzun durumların karışık olduğu senaryolar
                (string chatGptStatus, string geminiStatus)[] mixedScenarios = new[]
                {
                    ("✓ Güncel", "✓ Güncel"),                                // İkisi de kısa (normal günlük kullanım)
                    ("✓ Güncel", "⛔ Model Turu Engellendi"),                // ChatGPT kısa, Gemini en uzun
                    ("⛔ Model Turu Engellendi", "✓ Güncel"),                // ChatGPT en uzun, Gemini kısa
                    ("✓ Güncel", "🔑 Oturum Gerekli"),                        // ChatGPT kısa, Gemini orta
                    ("🔑 Oturum Gerekli", "✓ Güncel"),                        // ChatGPT orta, Gemini kısa
                    ("✕ CLI Bulunamadı", "✓ Güncel"),                        // ChatGPT hata, Gemini kısa
                    ("✓ Güncel", "⚠ Okuma Hatası"),                          // ChatGPT kısa, Gemini hata
                    ("🔑 Oturum Gerekli", "⛔ Model Turu Engellendi"),        // İkisi de farklı uzunlukta
                    ("⛔ Model Turu Engellendi", "⛔ Model Turu Engellendi")  // İkisi de en uzun
                };

                foreach (var (cgStatus, gmStatus) in mixedScenarios)
                {
                    vm.ChatGptCard.StatusText = cgStatus;
                    vm.ChatGptCard.FiveHourWindow.RemainingPercentText = "%100";
                    vm.ChatGptCard.FiveHourWindow.ShortCountdownText = "23s 59d";
                    vm.ChatGptCard.WeeklyWindow.RemainingPercentText = "%100";
                    vm.ChatGptCard.WeeklyWindow.ShortCountdownText = "29g 23s";

                    vm.GeminiCard.StatusText = gmStatus;
                    vm.GeminiCard.FiveHourWindow.RemainingPercentText = "%100";
                    vm.GeminiCard.FiveHourWindow.ShortCountdownText = "4s 25d";
                    vm.GeminiCard.WeeklyWindow.RemainingPercentText = "%100";
                    vm.GeminiCard.WeeklyWindow.ShortCountdownText = "6g 23s";

                    if (window.Content is FrameworkElement root)
                    {
                        root.Measure(new Size(MainWindow.MiniWidth, double.PositiveInfinity));
                        root.Arrange(new Rect(0, 0, MainWindow.MiniWidth, root.DesiredSize.Height));
                        root.UpdateLayout();

                        // 1. Sütun genişlikleri
                        double col0W = miniPanel.ColumnDefinitions[0].ActualWidth;
                        double col1W = miniPanel.ColumnDefinitions[1].ActualWidth;
                        double col2W = miniPanel.ColumnDefinitions[2].ActualWidth;
                        double col3W = miniPanel.ColumnDefinitions[3].ActualWidth;

                        double col0Left = 0;
                        double col1Left = col0W;
                        double col2Left = col1Left + col1W;
                        double col3Left = col2Left + col2W;

                        // Alt öğeleri bul:
                        // Row 0 Col 1: ChatGPT status, Row 0 Col 2: ChatGPT 5h, Row 0 Col 3: ChatGPT weekly
                        // Row 1 Col 1: Gemini status,  Row 1 Col 2: Gemini 5h,  Row 1 Col 3: Gemini weekly
                        FrameworkElement? chatGptStatusPanel = null;
                        FrameworkElement? geminiStatusPanel = null;
                        FrameworkElement? chatGpt5hPanel = null;
                        FrameworkElement? gemini5hPanel = null;
                        FrameworkElement? chatGptWeeklyPanel = null;
                        FrameworkElement? geminiWeeklyPanel = null;

                        foreach (UIElement child in miniPanel.Children)
                        {
                            int r = Grid.GetRow(child);
                            int c = Grid.GetColumn(child);
                            if (r == 0 && c == 1) chatGptStatusPanel = child as FrameworkElement;
                            if (r == 1 && c == 1) geminiStatusPanel = child as FrameworkElement;
                            if (r == 0 && c == 2) chatGpt5hPanel = child as FrameworkElement;
                            if (r == 1 && c == 2) gemini5hPanel = child as FrameworkElement;
                            if (r == 0 && c == 3) chatGptWeeklyPanel = child as FrameworkElement;
                            if (r == 1 && c == 3) geminiWeeklyPanel = child as FrameworkElement;
                        }

                        Assert.NotNull(chatGptStatusPanel);
                        Assert.NotNull(geminiStatusPanel);
                        Assert.NotNull(chatGpt5hPanel);
                        Assert.NotNull(gemini5hPanel);
                        Assert.NotNull(chatGptWeeklyPanel);
                        Assert.NotNull(geminiWeeklyPanel);

                        // 2. KRİTİK KURAL 1: 5 Saatlik ve Haftalık sütunları her iki satırda da AYNI X koordinatından başlamalı
                        // Hem sütun sol koordinatları hem de gerçek WPF TranslatePoint görsel X koordinatları tam eşit olmalı
                        Assert.True(col0Left == 0);
                        double chatGpt5hX = chatGpt5hPanel.TranslatePoint(new Point(0, 0), miniPanel).X;
                        double gemini5hX = gemini5hPanel.TranslatePoint(new Point(0, 0), miniPanel).X;
                        Assert.Equal(chatGpt5hX, gemini5hX, 2);
                        Assert.Equal(col2Left, chatGpt5hX, 2);

                        double chatGptWeeklyX = chatGptWeeklyPanel.TranslatePoint(new Point(0, 0), miniPanel).X;
                        double geminiWeeklyX = geminiWeeklyPanel.TranslatePoint(new Point(0, 0), miniPanel).X;
                        Assert.Equal(chatGptWeeklyX, geminiWeeklyX, 2);
                        Assert.Equal(col3Left, chatGptWeeklyX, 2);

                        // 3. KRİTİK KURAL 2: Durum ile kota arasında yaklaşık 8–12 DIP görsel boşluk olmalı
                        // Kontrol eden (daha geniş olan) durum satırı için görsel boşluk tam 10 DIP olmalıdır.
                        double cgContentW = (chatGptStatusPanel as StackPanel)?.Children.Cast<UIElement>().Sum(x => x.DesiredSize.Width) ?? chatGptStatusPanel.DesiredSize.Width;
                        double gmContentW = (geminiStatusPanel as StackPanel)?.Children.Cast<UIElement>().Sum(x => x.DesiredSize.Width) ?? geminiStatusPanel.DesiredSize.Width;

                        double maxStatusContentW = Math.Max(cgContentW, gmContentW);
                        double visualGapToQuota = col2Left - (col1Left + maxStatusContentW);

                        // Boşluk 8 ile 12 DIP aralığında (hedef 10 DIP)
                        Assert.InRange(visualGapToQuota, 8.0, 12.0);

                        // İkisi de "✓ Güncel" iken eski 120 DIP sabit durum sütunundaki 75 DIP boşluk oluşmamalı
                        if (cgStatus == "✓ Güncel" && gmStatus == "✓ Güncel")
                        {
                            double gapWhenBothShort = col2Left - (col1Left + cgContentW);
                            Assert.InRange(gapWhenBothShort, 8.0, 12.0);
                            Assert.True(col1W < 70, $"Güncel durumundayken Col 1 gereksiz geniş ({col1W:F1} px), 70 px altında olmalı!");
                        }

                        // 4. KRİTİK KURAL 3: Hiçbir kota veya sayaç pencere dışına taşmamalı
                        double windowInnerRightLimit = MainWindow.MiniWidth - 11;
                        double cgWeeklyRight = 11 + col3Left + chatGptWeeklyPanel.DesiredSize.Width;
                        double gmWeeklyRight = 11 + col3Left + geminiWeeklyPanel.DesiredSize.Width;

                        Assert.True(cgWeeklyRight <= windowInnerRightLimit + 0.5,
                            $"ChatGPT Haftalık sağ kenarı ({cgWeeklyRight:F1}) pencere sınırını ({windowInnerRightLimit:F1}) aştı!");
                        Assert.True(gmWeeklyRight <= windowInnerRightLimit + 0.5,
                            $"Gemini Haftalık sağ kenarı ({gmWeeklyRight:F1}) pencere sınırını ({windowInnerRightLimit:F1}) aştı!");

                        // 5. Yükseklik ve pencere genişliği
                        Assert.Equal(MainWindow.MiniWidth, window.Width);
                        Assert.InRange(root.DesiredSize.Height, 65, 88);
                    }
                }
            }
            finally
            {
                window.ExitApplication();
            }
        });
    }

    [Fact]
    public void MiniView_Countdowns_LiveBindingAndDataIntegrity()
    {
        var now = DateTimeOffset.Parse("2026-11-04T12:00:00Z");
        var window = new UsageGauge.ViewModels.QuotaWindowViewModel("5 Saatlik");

        // 1. Sıfırlanma zamanı yoksa: "—" ve "--%"
        window.UpdateFromWindow(null, false, now);
        Assert.Equal("—", window.ShortCountdownText);
        Assert.Equal("Sıfırlanma: --", window.CountdownText);
        Assert.Equal(0, window.RemainingPercent);

        // 2. Canlı sayaç: 4 saat 25 dakika sonra (örn. %69)
        var quota = new UsageGauge.Models.QuotaWindow(
            "5h",
            "5 Saatlik",
            300,
            69.0,
            now.AddHours(4).AddMinutes(25));
        window.UpdateFromWindow(quota, false, now);
        Assert.Equal("%69", window.RemainingPercentText);
        Assert.Equal("4s 25d", window.ShortCountdownText);
        Assert.Contains("4s 25dk", window.CountdownText);

        // 3. Zaman ilerlesin (1 saat sonra -> 3s 25d)
        window.UpdateCountdown(now.AddHours(1), false);
        Assert.Equal("3s 25d", window.ShortCountdownText);

        // 4. Süre dolduysa: "Doldu" göster, ama yüzdeyi kendiliğinden %100 yapma!
        window.UpdateCountdown(now.AddHours(5), false);
        Assert.Equal("Doldu", window.ShortCountdownText);
        Assert.Equal(QuotaTimeFormatter.ExpiredText, window.CountdownText);
        Assert.Equal(69.0, window.RemainingPercent); // Veri dürüstlüğü: yüzde değişmemeli!
        Assert.Equal("%69", window.RemainingPercentText);
    }
}
