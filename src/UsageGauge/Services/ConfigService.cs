using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace UsageGauge.Services;

/// <summary>
/// %APPDATA%\UsageGauge\config.json yapılandırmasını güvenle okuyan ve kaydeden servis.
/// Dosya bozuk veya yoksa güvenli varsayılan değerleri döndürür.
/// </summary>
public sealed class ConfigService : IConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _configFilePath;

    public string ConfigFilePath => _configFilePath;

    public ConfigService(string? customFilePath = null)
    {
        if (!string.IsNullOrWhiteSpace(customFilePath))
        {
            _configFilePath = customFilePath;
        }
        else
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _configFilePath = Path.Combine(appData, "UsageGauge", "config.json");
        }
    }

    public AppConfig LoadConfig()
    {
        try
        {
            if (!File.Exists(_configFilePath))
            {
                return new AppConfig();
            }

            string json = File.ReadAllText(_configFilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new AppConfig();
            }

            AppConfig? config = JsonSerializer.Deserialize<AppConfig>(json);
            if (config == null)
            {
                return new AppConfig();
            }

            // Opaklık sınırlarını doğrula (0.2 ile 1.0 arasında)
            double sanitizedOpacity = config.Opacity;
            if (sanitizedOpacity < 0.2 || sanitizedOpacity > 1.0)
            {
                sanitizedOpacity = 1.0;
            }

            // Geriye dönük uyumluluk ve görünüm modunu doğrula:
            // ConfigVersion < 2 ise eski sürüm 1 config'indeki "Mini", 270 px "Orta" görünümüdür.
            // ConfigVersion == 2 ise sürüm 2 config'indeki "Mini", 200 px "Orta-Mini" görünümüdür.
            // ConfigVersion >= 3 ise yeni yatay "Mini" görünümü doğrudan Mini olarak geçerlidir.
            string sanitizedViewMode = SanitizeViewMode(config.ViewMode, config.ConfigVersion);

            return config with
            {
                ConfigVersion = 3,
                Opacity = sanitizedOpacity,
                IsAlwaysOnTop = config.IsAlwaysOnTop,
                ViewMode = sanitizedViewMode
            };
        }
        catch
        {
            // Bozuk JSON veya dosya erişim hatasında güvenli varsayılan
            return new AppConfig { ConfigVersion = 3 };
        }
    }

    /// <summary>
    /// Görünüm modunu doğrular ve geriye dönük uyumluluğu uygular.
    /// - Sürüm 1 (configVersion &lt; 2 veya belirsiz): "Mini" -> "Orta" (270 px)
    /// - Sürüm 2 (configVersion == 2): "Mini" -> "Orta-Mini" (200 px)
    /// - Sürüm 3+ (configVersion &gt;= 3): "Mini" -> "Mini" (340 px yatay kompakt)
    /// "Orta-Mini", "Orta", "Normal" ve varyasyonları doğrudan tanınır.
    /// Geçersiz değerler varsayılan "Normal" moduna temizlenir.
    /// </summary>
    public static string SanitizeViewMode(string? viewMode, int configVersion = 3)
    {
        if (string.IsNullOrWhiteSpace(viewMode))
        {
            return "Normal";
        }

        if (viewMode.Equals("Orta-Mini", StringComparison.OrdinalIgnoreCase) ||
            viewMode.Equals("Medium-Mini", StringComparison.OrdinalIgnoreCase) ||
            viewMode.Equals("OrtaMini", StringComparison.OrdinalIgnoreCase))
        {
            return "Orta-Mini";
        }

        if (viewMode.Equals("Orta", StringComparison.OrdinalIgnoreCase) ||
            viewMode.Equals("Medium", StringComparison.OrdinalIgnoreCase))
        {
            return "Orta";
        }

        if (viewMode.Equals("Mini", StringComparison.OrdinalIgnoreCase))
        {
            if (configVersion < 2)
            {
                return "Orta";
            }
            if (configVersion == 2)
            {
                return "Orta-Mini";
            }
            return "Mini";
        }

        if (viewMode.Equals("Normal", StringComparison.OrdinalIgnoreCase))
        {
            return "Normal";
        }

        return "Normal";
    }

    public void SaveConfig(AppConfig config)
    {
        try
        {
            string? dir = Path.GetDirectoryName(_configFilePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Güvenlik denetimi: Asla hassas veri yazılmaz, yalnızca konum ve görünüm
            var sanitizedConfig = new AppConfig
            {
                ConfigVersion = 3,
                WindowLeft = config.WindowLeft,
                WindowTop = config.WindowTop,
                MonitorDeviceName = config.MonitorDeviceName,
                Opacity = Math.Clamp(config.Opacity, 0.2, 1.0),
                IsAlwaysOnTop = config.IsAlwaysOnTop,
                ViewMode = SanitizeViewMode(config.ViewMode, configVersion: 3)
            };

            string json = JsonSerializer.Serialize(sanitizedConfig, JsonOptions);
            File.WriteAllText(_configFilePath, json);
        }
        catch
        {
            // Kaydetme hatası uygulamanın çökmesine neden olmamalıdır
        }
    }

    /// <summary>
    /// Pencere koordinatlarının geçerli monitör çalışma alanları (DIP) içinde görünür ve çalışılabilir kalmasını garanti eder.
    /// Kaydedilen MonitorDeviceName varsa o monitörü, aksi halde en uygun veya birincil monitörü kullanır.
    /// </summary>
    public static (double Left, double Top) EnsureVisibleBounds(
        double left,
        double top,
        double width,
        double height,
        IReadOnlyList<MonitorDescriptor> monitors,
        string? preferredDeviceName = null)
    {
        if (monitors == null || monitors.Count == 0)
        {
            return (80, 80);
        }

        // Birincil monitörü bul (IsPrimary alanına göre; Enum sırasına güvenilmez)
        MonitorDescriptor primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];

        // 1. Tercih edilen monitör adı belirtilmişse ve monitör hâlâ bağlıysa
        if (!string.IsNullOrWhiteSpace(preferredDeviceName))
        {
            MonitorDescriptor? targetMon = monitors.FirstOrDefault(m =>
                string.Equals(m.DeviceName, preferredDeviceName, StringComparison.OrdinalIgnoreCase));

            if (targetMon != null)
            {
                Rect work = targetMon.WorkAreaDip;
                // Koordinatlar bu monitörün içindeyse veya yakınındaysa o monitör sınırlarına sığdır
                double minL = work.Left;
                double maxL = Math.Max(work.Left, work.Right - Math.Min(width, 240));
                double clampL = Math.Clamp(left, minL, maxL);

                double minT = work.Top;
                double maxT = Math.Max(work.Top, work.Bottom - Math.Min(height, 200));
                double clampT = Math.Clamp(top, minT, maxT);

                return (clampL, clampT);
            }
        }

        // 2. Tercih edilen monitör yoksa veya bulunamadıysa, pencerenin en çok kesiştiği monitörü ara
        Rect windowRect = new(left, top, width, height);
        MonitorDescriptor? bestMonitor = null;
        double maxIntersectionArea = 0;

        foreach (MonitorDescriptor monitor in monitors)
        {
            Rect intersection = Rect.Intersect(windowRect, monitor.WorkAreaDip);
            if (!intersection.IsEmpty)
            {
                double area = intersection.Width * intersection.Height;
                if (area > maxIntersectionArea)
                {
                    maxIntersectionArea = area;
                    bestMonitor = monitor;
                }
            }
        }

        // Pencere en az 15.000 piksel² (~150x100 px çalışılabilir alan) görünürse
        if (maxIntersectionArea >= 15000 && bestMonitor != null)
        {
            Rect work = bestMonitor.WorkAreaDip;
            double clampL = Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - Math.Min(width, 240)));
            double clampT = Math.Clamp(top, work.Top, Math.Max(work.Top, work.Bottom - Math.Min(height, 200)));
            return (clampL, clampT);
        }

        // 3. Hiçbir monitörde yeterince görünür değilse (monitör çıkarılmış, koordinatlar kaymış vb.)
        // Birincil monitörün sağ alt köşesine güvenli yerleştir
        double safeLeft = primary.WorkAreaDip.Right - width - 24;
        double safeTop = primary.WorkAreaDip.Bottom - height - 48;

        if (safeLeft < primary.WorkAreaDip.Left) safeLeft = primary.WorkAreaDip.Left + 20;
        if (safeTop < primary.WorkAreaDip.Top) safeTop = primary.WorkAreaDip.Top + 20;

        return (safeLeft, safeTop);
    }

    public static (double Left, double Top) EnsureVisibleBounds(
        double left,
        double top,
        double width,
        double height,
        IReadOnlyList<Rect> monitorWorkAreas)
    {
        if (monitorWorkAreas == null || monitorWorkAreas.Count == 0) return (80, 80);
        var descriptors = monitorWorkAreas.Select((r, i) =>
            new MonitorDescriptor($"Monitor{i + 1}", $"Monitor {i + 1}", r, i == 0)).ToList();
        return EnsureVisibleBounds(left, top, width, height, descriptors, null);
    }
}
