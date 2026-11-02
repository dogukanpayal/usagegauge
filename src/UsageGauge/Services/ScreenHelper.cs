using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using UsageGauge.Native;

namespace UsageGauge.Services;

public sealed record MonitorDescriptor(
    string DeviceName,
    string DisplayName,
    Rect WorkAreaDip,
    bool IsPrimary);

public static class ScreenHelper
{
    /// <summary>
    /// Sisteme bağlı tüm monitörleri sorgular ve koordinatlarını tek, tutarlı
    /// WPF DIP (Device Independent Pixels) uzayına dönüştürür.
    /// WPF süreç System DPI Aware çalıştığı için, sanal masaüstündeki tüm monitörler
    /// sistem DPI ölçeğiyle dönüştürülerek pencereler arası kesintisiz koordinat sürekliliği sağlanır.
    /// </summary>
    public static IReadOnlyList<MonitorDescriptor> GetMonitors(DpiScale systemDpi)
    {
        List<MonitorDescriptor> monitors = new();

        double scaleX = systemDpi.DpiScaleX > 0 ? systemDpi.DpiScaleX : 1.0;
        double scaleY = systemDpi.DpiScaleY > 0 ? systemDpi.DpiScaleY : 1.0;

        NativeMethods.MonitorEnumProc callback = (IntPtr hMonitor, IntPtr hdcMonitor, ref NativeMethods.RECT lprcMonitor, IntPtr dwData) =>
        {
            NativeMethods.MONITORINFOEX info = new();
            info.cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>();

            if (NativeMethods.GetMonitorInfo(hMonitor, ref info))
            {
                bool isPrimary = (info.dwFlags & 1) != 0;
                string device = info.szDevice ?? $"Monitor{monitors.Count + 1}";

                // Sistem DPI uzayında tüm monitörlerin fiziksel koordinatları,
                // tek ve tutarlı WPF DIP koordinat sistemine sistem ölçeğiyle dönüştürülür.
                double left = info.rcWork.Left / scaleX;
                double top = info.rcWork.Top / scaleY;
                double width = (info.rcWork.Right - info.rcWork.Left) / scaleX;
                double height = (info.rcWork.Bottom - info.rcWork.Top) / scaleY;

                Rect workArea = new(left, top, Math.Max(100, width), Math.Max(100, height));
                string displayName = isPrimary ? $"Monitör {monitors.Count + 1} (Birincil)" : $"Monitör {monitors.Count + 1}";

                monitors.Add(new MonitorDescriptor(device, displayName, workArea, isPrimary));
            }
            return true;
        };

        try
        {
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        }
        catch
        {
            // P/Invoke hatasında boş döner
        }

        // Eğer hiçbir monitör bulunamadıysa WPF SystemParameters yedeği
        if (monitors.Count == 0)
        {
            Rect primary = SystemParameters.WorkArea;
            monitors.Add(new MonitorDescriptor(@"\\.\DISPLAY1", "Monitör 1 (Birincil)", primary, true));
        }

        return monitors;
    }

    /// <summary>
    /// Belirtilen pencere dikdörtgeninin en büyük bölümünü içeren baskın monitörü bulur.
    /// Pencere iki monitör arasındaysa en büyük kesişim alanına sahip monitörü,
    /// hiç kesişmiyorsa birincil monitörü döner.
    /// </summary>
    public static MonitorDescriptor? FindDominantMonitor(Rect windowRect, IReadOnlyList<MonitorDescriptor> monitors)
    {
        if (monitors == null || monitors.Count == 0) return null;

        MonitorDescriptor? dominant = null;
        double maxArea = 0;

        foreach (var m in monitors)
        {
            Rect intersection = Rect.Intersect(m.WorkAreaDip, windowRect);
            if (!intersection.IsEmpty)
            {
                double area = intersection.Width * intersection.Height;
                if (area > maxArea)
                {
                    maxArea = area;
                    dominant = m;
                }
            }
        }

        return dominant ?? monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
    }
}
