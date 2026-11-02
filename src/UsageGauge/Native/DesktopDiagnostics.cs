using System;
using System.IO;
using System.Text;

namespace UsageGauge.Native;

/// <summary>
/// Win+D kaybolma ve görünürlük davranışını teşhis eden kayıt katmanı.
/// IsIconic, IsWindowVisible, pencere koordinatları ve z-order komşularını
/// kişisel pencere başlıklarını içermeksizin 'usagegauge-diagnostics.log' dosyasına yazar.
/// Mükerrer ve anlamsız kayıtları filtreleyerek dosyanın kontrolsüz büyümesini engeller.
/// </summary>
public static class DesktopDiagnostics
{
    private static readonly object _fileLock = new();
    private static readonly string _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "usagegauge-diagnostics.log");

    // Mükerrer kayıt süzgeci
    private static string _lastLoggedSignature = string.Empty;
    private static DateTime _lastLogTime = DateTime.MinValue;

    public static string LogFilePath => _logFilePath;
    public static string LatestSummary { get; private set; } = "Henüz teşhis kaydı yok.";

    public static void LogSnapshot(IntPtr hwnd, string trigger, bool forceLog = false)
    {
        if (hwnd == IntPtr.Zero) return;

        try
        {
            bool isIconic = NativeMethods.IsIconic(hwnd);
            bool isVisible = NativeMethods.IsWindowVisible(hwnd);

            NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect);

            IntPtr prevHwnd = NativeMethods.GetWindow(hwnd, NativeMethods.GW_HWNDPREV);
            string prevClass = GetHwndClassName(prevHwnd);

            IntPtr nextHwnd = NativeMethods.GetWindow(hwnd, NativeMethods.GW_HWNDNEXT);
            string nextClass = GetHwndClassName(nextHwnd);

            IntPtr fgHwnd = NativeMethods.GetForegroundWindow();
            string fgClass = GetHwndClassName(fgHwnd);

            DateTime now = DateTime.Now;
            string timestamp = now.ToString("yyyy-MM-dd HH:mm:ss.fff");

            LatestSummary = $"Iconic:{isIconic} | Vis:{isVisible} | AboveHint:{prevClass} | Fg:{fgClass}";

            // Durum imzası: Trigger, Iconic, Visible, Rect, Foreground, Above
            string currentSignature = $"{trigger}|{isIconic}|{isVisible}|{rect}|0x{fgHwnd.ToInt64():X8}|0x{prevHwnd.ToInt64():X8}";

            // Filtre: Eğer durum değişmediyse ve zorunlu değilse ve son kayıttan 10 saniye geçmediyse dosyaya mükerrer yazma
            if (!forceLog && currentSignature == _lastLoggedSignature && (now - _lastLogTime).TotalSeconds < 10)
            {
                return;
            }

            _lastLoggedSignature = currentSignature;
            _lastLogTime = now;

            string line = $"[{timestamp}] [{trigger}] Iconic={isIconic} | Visible={isVisible} | Rect={rect} | " +
                          $"ZOrderHint_Above=0x{prevHwnd.ToInt64():X8}({prevClass}) | " +
                          $"ZOrderHint_Below=0x{nextHwnd.ToInt64():X8}({nextClass}) | " +
                          $"Foreground=0x{fgHwnd.ToInt64():X8}({fgClass})";

            lock (_fileLock)
            {
                File.AppendAllText(_logFilePath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Teşhis kaydının ana pencere akışını etkilemesine asla izin verilmez
        }
    }

    public static string GetHwndClassName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "None";
        var sb = new StringBuilder(128);
        NativeMethods.GetClassName(hwnd, sb, 128);
        string name = sb.ToString();
        return string.IsNullOrWhiteSpace(name) ? "Unknown" : name;
    }
}
