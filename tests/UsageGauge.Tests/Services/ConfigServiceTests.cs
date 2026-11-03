using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using UsageGauge.Services;
using Xunit;

namespace UsageGauge.Tests.Services;

public class ConfigServiceTests
{
    [Fact]
    public void LoadConfig_WhenFileDoesNotExist_ReturnsSafeDefaults()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"usagegauge_test_{Guid.NewGuid():N}.json");
        var service = new ConfigService(tempPath);

        AppConfig config = service.LoadConfig();

        Assert.NotNull(config);
        Assert.Equal(80, config.WindowLeft);
        Assert.Equal(80, config.WindowTop);
        Assert.Equal(1.0, config.Opacity);
    }

    [Fact]
    public void LoadConfig_WhenFileIsMalformedJson_ReturnsSafeDefaults()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"usagegauge_test_{Guid.NewGuid():N}.json");
        File.WriteAllText(tempPath, "{ corrupted json syntax !!!");
        var service = new ConfigService(tempPath);

        try
        {
            AppConfig config = service.LoadConfig();
            Assert.NotNull(config);
            Assert.Equal(80, config.WindowLeft);
            Assert.Equal(80, config.WindowTop);
            Assert.Equal(1.0, config.Opacity);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void SaveAndLoadConfig_PreservesCoordinatesAndOpacity()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"usagegauge_test_{Guid.NewGuid():N}.json");
        var service = new ConfigService(tempPath);

        try
        {
            var initial = new AppConfig
            {
                WindowLeft = 240,
                WindowTop = 360,
                MonitorDeviceName = "\\\\.\\DISPLAY2",
                Opacity = 0.8
            };

            service.SaveConfig(initial);

            AppConfig loaded = service.LoadConfig();
            Assert.Equal(240, loaded.WindowLeft);
            Assert.Equal(360, loaded.WindowTop);
            Assert.Equal("\\\\.\\DISPLAY2", loaded.MonitorDeviceName);
            Assert.Equal(0.8, loaded.Opacity);

            // Gizlilik doğrulaması: Asla kota, token veya kimlik bilgisi yazılmamalı
            string rawJson = File.ReadAllText(tempPath);
            Assert.DoesNotContain("token", rawJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("quota", rawJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("email", rawJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password", rawJson, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void EnsureVisibleBounds_WhenWithinScreen_MaintainsPosition()
    {
        List<Rect> monitors = new()
        {
            new Rect(0, 0, 1920, 1080)
        };

        var (left, top) = ConfigService.EnsureVisibleBounds(200, 300, 340, 460, monitors);

        Assert.Equal(200, left);
        Assert.Equal(300, top);
    }

    [Fact]
    public void EnsureVisibleBounds_WhenOffscreenFarRight_ClampsToPrimarySafeBounds()
    {
        List<Rect> monitors = new()
        {
            new Rect(0, 0, 1920, 1080)
        };

        // 2. monitör çıkarılmış ve pencere 3500 pikselde kalmış simülasyonu
        var (left, top) = ConfigService.EnsureVisibleBounds(3500, 500, 340, 460, monitors);

        Assert.InRange(left, 0, 1920 - 340);
        Assert.InRange(top, 0, 1080 - 460);
    }

    [Fact]
    public void EnsureVisibleBounds_WhenOnSecondMonitor_PreservesSecondMonitorPlacement()
    {
        List<Rect> monitors = new()
        {
            new Rect(0, 0, 1920, 1080),        // Monitör 1
            new Rect(1920, 0, 1920, 1080)      // Monitör 2
        };

        // 2. monitörün ortasında bir konum
        var (left, top) = ConfigService.EnsureVisibleBounds(2200, 200, 340, 460, monitors);

        Assert.Equal(2200, left);
        Assert.Equal(200, top);
    }

    [Fact]
    public void EnsureVisibleBounds_DualMonitorsDifferentDpi_ClampsCorrectlyToTargetMonitor()
    {
        // Monitör 1: 100% DPI (96 DPI) -> 1920x1080 DIP
        // Monitör 2: 150% DPI (144 DPI) -> 3840/1.5 = 2560 DIP, 2160/1.5 = 1440 DIP -> Rect(1920, 0, 2560, 1440)
        var monitors = new List<MonitorDescriptor>
        {
            new MonitorDescriptor(@"\\.\DISPLAY1", "Display 1", new Rect(0, 0, 1920, 1080), IsPrimary: true),
            new MonitorDescriptor(@"\\.\DISPLAY2", "Display 2", new Rect(1920, 0, 2560, 1440), IsPrimary: false)
        };

        // Monitör 2'ye yerleştirilmiş ve sağ alt sınırın dışına taşmış pencere
        var (left, top) = ConfigService.EnsureVisibleBounds(
            left: 4400,
            top: 1400,
            width: 340,
            height: 460,
            monitors: monitors,
            preferredDeviceName: @"\\.\DISPLAY2");

        // Monitör 2 içinde kalmalı ve en az 240x200 px çalışılabilir alan sunmalı
        Assert.True(left >= 1920);
        Assert.True(left <= (1920 + 2560 - 240));
        Assert.True(top >= 0);
        Assert.True(top <= (1440 - 200));
    }

    [Fact]
    public void EnsureVisibleBounds_WhenPreferredMonitorDisconnected_FallsBackToPrimary()
    {
        var monitors = new List<MonitorDescriptor>
        {
            new MonitorDescriptor(@"\\.\DISPLAY1", "Display 1", new Rect(0, 0, 1920, 1080), IsPrimary: true)
        };

        // Tercih edilen DISPLAY2 artık yok; koordinatlar da eski monitörde kalmış (3000, 500)
        var (left, top) = ConfigService.EnsureVisibleBounds(
            left: 3000,
            top: 500,
            width: 340,
            height: 460,
            monitors: monitors,
            preferredDeviceName: @"\\.\DISPLAY2");

        // Birincil monitör sınırları içinde güvenli konuma yerleşmeli
        Assert.InRange(left, 0, 1920 - 340);
        Assert.InRange(top, 0, 1080 - 460);
    }

    [Fact]
    public void EnsureVisibleBounds_WhenPrimaryIsNotIndexZero_SelectsPrimaryCorrectly()
    {
        // İkincil monitör sol tarafta (Index 0), Birincil monitör sağ tarafta (Index 1)
        var monitors = new List<MonitorDescriptor>
        {
            new MonitorDescriptor(@"\\.\DISPLAY2", "Display 2 (Left)", new Rect(-1920, 0, 1920, 1080), IsPrimary: false),
            new MonitorDescriptor(@"\\.\DISPLAY1", "Display 1 (Primary)", new Rect(0, 0, 1920, 1080), IsPrimary: true)
        };

        // Tamamen ekran dışı koordinat
        var (left, top) = ConfigService.EnsureVisibleBounds(
            left: 8000,
            top: 8000,
            width: 340,
            height: 460,
            monitors: monitors);

        // Birincil monitörün (DISPLAY1: 0..1920) sınırları içinde olmalıdır, DISPLAY2 (-1920..0) değil
        Assert.InRange(left, 0, 1920);
        Assert.InRange(top, 0, 1080);
    }

    [Fact]
    public void EnsureVisibleBounds_WorkableAreaEnforcement_GuaranteesAtLeast240x200Visible()
    {
        var monitors = new List<MonitorDescriptor>
        {
            new MonitorDescriptor(@"\\.\DISPLAY1", "Display 1", new Rect(0, 0, 1920, 1080), IsPrimary: true)
        };

        // Pencere sağ alt köşeden dışarı itilmiş: width 340, height 460. Sadece 60x40 piksel kalmasın!
        var (left, top) = ConfigService.EnsureVisibleBounds(
            left: 1900,
            top: 1060,
            width: 340,
            height: 460,
            monitors: monitors);

        // left <= 1920 - 240 = 1680 olmalıdır (böylece ekranda en az 240px kalır)
        Assert.True(left <= 1920 - 240, $"Left was {left}, expected <= {1920 - 240}");
        // top <= 1080 - 200 = 880 olmalıdır (böylece ekranda en az 200px kalır)
        Assert.True(top <= 1080 - 200, $"Top was {top}, expected <= {1080 - 200}");
    }

    [Fact]
    public void FindDominantMonitor_WhenSpanningTwoMonitors_SelectsMonitorWithLargerIntersection()
    {
        var monitors = new List<MonitorDescriptor>
        {
            new MonitorDescriptor(@"\\.\DISPLAY1", "Display 1 (Primary)", new Rect(0, 0, 1920, 1080), IsPrimary: true),
            new MonitorDescriptor(@"\\.\DISPLAY2", "Display 2 (Secondary)", new Rect(1920, 0, 1920, 1080), IsPrimary: false)
        };

        // Pencere 1800'de: 120 px DISPLAY1'de, 220 px DISPLAY2'de
        Rect windowRect = new(1800, 100, 340, 460);

        MonitorDescriptor? dominant = ScreenHelper.FindDominantMonitor(windowRect, monitors);

        Assert.NotNull(dominant);
        Assert.Equal(@"\\.\DISPLAY2", dominant.DeviceName);
    }

    [Fact]
    public void FindDominantMonitor_WhenMostlyOnFirstMonitor_SelectsFirstMonitor()
    {
        var monitors = new List<MonitorDescriptor>
        {
            new MonitorDescriptor(@"\\.\DISPLAY1", "Display 1 (Primary)", new Rect(0, 0, 1920, 1080), IsPrimary: true),
            new MonitorDescriptor(@"\\.\DISPLAY2", "Display 2 (Secondary)", new Rect(1920, 0, 1920, 1080), IsPrimary: false)
        };

        // Pencere 1700'de: 220 px DISPLAY1'de, 120 px DISPLAY2'de
        Rect windowRect = new(1700, 100, 340, 460);

        MonitorDescriptor? dominant = ScreenHelper.FindDominantMonitor(windowRect, monitors);

        Assert.NotNull(dominant);
        Assert.Equal(@"\\.\DISPLAY1", dominant.DeviceName);
    }

    [Fact]
    public void FindDominantMonitor_WhenCompletelyOffscreen_FallsBackToPrimary()
    {
        var monitors = new List<MonitorDescriptor>
        {
            new MonitorDescriptor(@"\\.\DISPLAY2", "Display 2 (Left)", new Rect(-1920, 0, 1920, 1080), IsPrimary: false),
            new MonitorDescriptor(@"\\.\DISPLAY1", "Display 1 (Primary)", new Rect(0, 0, 1920, 1080), IsPrimary: true)
        };

        Rect windowRect = new(8000, 8000, 340, 460);

        MonitorDescriptor? dominant = ScreenHelper.FindDominantMonitor(windowRect, monitors);

        Assert.NotNull(dominant);
        Assert.Equal(@"\\.\DISPLAY1", dominant.DeviceName);
    }

    [Fact]
    public void EnsureVisibleBounds_WhenResolutionShrinksOnDualMonitors_ClampsWithinVisibleWorkArea()
    {
        // 2. monitörün çözünürlüğü 4K'dan (3840x2160 -> 2560x1440 DIP) Full HD'ye (1920x1080 DIP) düşürüldü
        // Monitör 1: Rect(0, 0, 1920, 1080)
        // Monitör 2: Rect(1920, 0, 1920, 1080) -> Sağ sınır 3840
        var monitors = new List<MonitorDescriptor>
        {
            new MonitorDescriptor(@"\\.\DISPLAY1", "Display 1 (Primary)", new Rect(0, 0, 1920, 1080), IsPrimary: true),
            new MonitorDescriptor(@"\\.\DISPLAY2", "Display 2 (Secondary)", new Rect(1920, 0, 1920, 1080), IsPrimary: false)
        };

        // Eski 4K çözünürlükte sağ alt köşede kaydedilmiş konum (left: 4200, top: 1300)
        var (clampedLeft, clampedTop) = ConfigService.EnsureVisibleBounds(
            left: 4200,
            top: 1300,
            width: 340,
            height: 330,
            monitors: monitors,
            preferredDeviceName: @"\\.\DISPLAY2");

        // Pencere yeni Monitör 2 sınırları içinde kalmalı (1920 <= Left <= 3840 - 240)
        Assert.InRange(clampedLeft, 1920, 1920 + 1920 - 240);
        Assert.InRange(clampedTop, 0, 1080 - 200);
    }
}
