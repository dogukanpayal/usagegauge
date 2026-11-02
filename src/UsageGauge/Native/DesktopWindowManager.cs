using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace UsageGauge.Native;

/// <summary>
/// UsageGauge penceresinin masaüstü z-order ve görünürlük davranışını yöneten sınıf.
/// WispDock'un kanıtlanmış dinamik Z-Order mimarisini temel alır:
/// 1. WS_EX_TOOLWINDOW: Görev çubuğundan ve Alt+Tab'dan gizleme.
/// 2. WS_EX_NOACTIVATE + WM_MOUSEACTIVATE (MA_NOACTIVATE): Odak çalmama.
/// 3. WM_SYSCOMMAND (SC_MINIMIZE): Shell'in pencereyi simge durumuna küçültmesini engelleme.
/// 4. EVENT_SYSTEM_FOREGROUND WinEvent Kancası:
///    - Masaüstü öndeyken (Win+D dahil): SW_SHOWNOACTIVATE ile görünürlük teyidi ve HWND_TOPMOST katmanına alma (tek seferlik 100ms doğrulama ile).
///    - Normal uygulama öndeyken: HWND_NOTOPMOST ve aktif uygulamanın hemen arkasına yerleştirme.
/// </summary>
public sealed class DesktopWindowManager : IDisposable
{
    private readonly Window _window;
    private readonly Dispatcher _dispatcher;
    private IntPtr _hwnd = IntPtr.Zero;
    private HwndSource? _hwndSource;
    private IntPtr _hookHandle = IntPtr.Zero;
    private NativeMethods.WinEventDelegate? _winEventDelegate;
    private bool _isDisposed;
    private bool _hasPendingDesktopVerify;
    private bool _isDesktopActive;
    private bool _wasDesktopLastUpdate;
    private int _zeroFgRetryCount;
    private const int MaxZeroFgRetries = 4; // Toplam en fazla 200 ms (4 * 50ms)

    public IntPtr Handle => _hwnd;
    public bool IsDesktopActive => _isDesktopActive;

    public event Action<string>? StatusChanged;

    public DesktopWindowManager(Window window, Dispatcher? dispatcher = null)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    /// <summary>
    /// Pencere tanıtıcısı (HWND) oluşturulduğunda (SourceInitialized) çağrılır.
    /// Genişletilmiş stilleri uygular, Win32 mesaj kancasını ve global WinEvent kancasını bağlar.
    /// Herhangi bir adım başarısız olursa pencere TopMost'ta takılı bırakılmaz ve false döner.
    /// </summary>
    public bool Initialize()
    {
        _hwnd = new WindowInteropHelper(_window).Handle;
        if (_hwnd == IntPtr.Zero)
        {
            StatusChanged?.Invoke("Kritik Hata: Pencere HWND tanıtıcısı alınamadı.");
            return false;
        }

        _hwndSource = HwndSource.FromHwnd(_hwnd);
        if (_hwndSource == null)
        {
            StatusChanged?.Invoke("Kritik Hata: HwndSource kaynağı oluşturulamadı.");
            return false;
        }

        _hwndSource.AddHook(WndProcHook);

        if (!ApplyDesktopExtendedStyles())
        {
            return false;
        }

        if (!InitializeHook())
        {
            return false;
        }

        _dispatcher.BeginInvoke(new Action(() => UpdateZOrder(isVerification: false)));

        DesktopDiagnostics.LogSnapshot(_hwnd, "Initialized-WispDockDynamic", forceLog: true);
        StatusChanged?.Invoke("DesktopWindowManager aktif: WispDock Dinamik Z-Order modeli devrede.");
        return true;
    }

    /// <summary>
    /// Genişletilmiş pencere stillerini (WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) uygular ve WS_EX_APPWINDOW stilini temizler.
    /// </summary>
    public bool ApplyDesktopExtendedStyles()
    {
        if (_hwnd == IntPtr.Zero)
        {
            StatusChanged?.Invoke("Hata: Geçersiz HWND, stil uygulanamadı.");
            return false;
        }

        NativeMethods.SetLastError(0);
        IntPtr curExStylePtr = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE);
        int err = Marshal.GetLastWin32Error();
        if (curExStylePtr == IntPtr.Zero && err != 0)
        {
            StatusChanged?.Invoke($"Hata: GetWindowLongPtr(GWL_EXSTYLE) başarısız oldu (Win32 Kodu: {err}).");
            return false;
        }

        long curExStyle = curExStylePtr.ToInt64();
        long newExStyle = (curExStyle | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE) & ~NativeMethods.WS_EX_APPWINDOW;

        NativeMethods.SetLastError(0);
        IntPtr setResult = NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(newExStyle));
        err = Marshal.GetLastWin32Error();
        if (setResult == IntPtr.Zero && err != 0)
        {
            StatusChanged?.Invoke($"Hata: SetWindowLongPtr(GWL_EXSTYLE) başarısız oldu (Win32 Kodu: {err}).");
            return false;
        }

        // SWP_FRAMECHANGED ile sistem önbelleğindeki pencere stilini yenile
        NativeMethods.SetLastError(0);
        bool posSuccess = NativeMethods.SetWindowPos(
            _hwnd,
            IntPtr.Zero,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED
        );

        if (!posSuccess)
        {
            err = Marshal.GetLastWin32Error();
            StatusChanged?.Invoke($"Hata: SetWindowPos(SWP_FRAMECHANGED) başarısız oldu (Win32 Kodu: {err}).");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Global EVENT_SYSTEM_FOREGROUND kancasını kurar.
    /// Sıfır HWND dönerse hata açıkça bildirilir, pencere TopMost'ta bırakılmaz ve false döner.
    /// </summary>
    private bool InitializeHook()
    {
        _winEventDelegate = OnWinEvent;
        _hookHandle = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _winEventDelegate,
            0,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT);

        if (_hookHandle == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();

            // Pencere TopMost katmanında takılı kalmamalıdır; güvenli şekilde NOTOPMOST ve tabana al
            NativeMethods.SetWindowPos(
                _hwnd,
                NativeMethods.HWND_NOTOPMOST,
                0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE
            );

            NativeMethods.SetWindowPos(
                _hwnd,
                NativeMethods.HWND_BOTTOM,
                0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE
            );

            StatusChanged?.Invoke($"Kritik Hata: SetWinEventHook kurulamadı (Win32 Kodu: {err}). Dinamik z-order devre dışı.");
            return false;
        }

        return true;
    }

    private void OnWinEvent(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hWnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (eventType == NativeMethods.EVENT_SYSTEM_FOREGROUND && !_isDisposed)
        {
            _dispatcher.BeginInvoke(new Action(() => UpdateZOrder(isVerification: false)));
        }
    }

    /// <summary>
    /// Ön plandaki pencereye göre Z-sırasını dinamik olarak senkronize eder:
    /// - Masaüstü öndeyken: Widget masaüstü üzerinde görünür kalması için HWND_TOPMOST katmanına alınır.
    ///   Yeni bir masaüstü geçişinde tek seferlik 100 ms doğrulama planlanır. Doğrulama çağrısı kendini tekrar planlamaz.
    /// - Normal uygulama öndeyken: HWND_NOTOPMOST katmanına alınarak aktif uygulamanın hemen arkasına yerleştirilir.
    /// </summary>
    public void UpdateZOrder(bool isVerification = false)
    {
        if (_isDisposed || _hwnd == IntPtr.Zero) return;

        IntPtr currentFgHwnd = NativeMethods.GetForegroundWindow();

        if (currentFgHwnd == IntPtr.Zero)
        {
            // Sınırlı tekrar (en fazla 4 defa = 200ms); sonsuz döngüye girmesi engellenir
            if (_zeroFgRetryCount < MaxZeroFgRetries)
            {
                _zeroFgRetryCount++;
                ScheduleDelayedZOrder(50);
            }
            return;
        }

        _zeroFgRetryCount = 0;

        if (currentFgHwnd == _hwnd)
        {
            // Ön plan penceresi widget'ın kendisi ise Z-sırası değişimi gerekmez
            return;
        }

        string fgClass = GetWindowClassName(currentFgHwnd);
        IntPtr shellHwnd = NativeMethods.GetShellWindow();
        NativeMethods.GetWindowThreadProcessId(shellHwnd, out uint shellProcId);
        NativeMethods.GetWindowThreadProcessId(currentFgHwnd, out uint fgProcId);

        bool isDesktop = IsDefinitelyDesktop(currentFgHwnd, fgClass, shellHwnd, shellProcId, fgProcId);
        _isDesktopActive = isDesktop;

        bool wasIconic = NativeMethods.IsIconic(_hwnd);
        bool wasVisible = NativeMethods.IsWindowVisible(_hwnd);

        if (isDesktop)
        {
            // Masaüstü öndeyken (Win+D dahil):
            // 1. Eğer pencere Windows Shell tarafından küçültülmüş veya gizlenmişse SW_SHOWNOACTIVATE ile geri getir
            if (wasIconic || !wasVisible)
            {
                NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNOACTIVATE);
            }

            // 2. Masaüstü üzerinde görünür ve tıklanabilir kalması için Topmost katmanına al (odağı çalmadan)
            NativeMethods.SetLastError(0);
            bool topRes = NativeMethods.SetWindowPos(
                _hwnd,
                NativeMethods.HWND_TOPMOST,
                0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW
            );

            if (!topRes)
            {
                int err = Marshal.GetLastWin32Error();
                StatusChanged?.Invoke($"Hata: HWND_TOPMOST ayarlanamadı (Win32 Kodu: {err}).");
            }
            else
            {
                StatusChanged?.Invoke("Masaüstü aktif: HWND_TOPMOST (Masaüstü üzerinde görünür ✓)");
            }

            // Yalnızca yeni bir masaüstü geçişinde tek seferlik 100 ms doğrulama planla.
            // Doğrulama çağrısının kendisi (isVerification=true) asla tekrar planlama yapmaz.
            if (!isVerification && !_wasDesktopLastUpdate && !_hasPendingDesktopVerify)
            {
                _hasPendingDesktopVerify = true;
                ScheduleDelayedVerification(100);
            }

            _wasDesktopLastUpdate = true;
        }
        else
        {
            // Normal uygulama öndeyken (Chrome, Notepad, VS Code vb.):
            _wasDesktopLastUpdate = false;
            _hasPendingDesktopVerify = false;

            // 1. Topmost'tan çıkar
            NativeMethods.SetLastError(0);
            bool notopRes = NativeMethods.SetWindowPos(
                _hwnd,
                NativeMethods.HWND_NOTOPMOST,
                0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE
            );

            if (!notopRes)
            {
                int err = Marshal.GetLastWin32Error();
                StatusChanged?.Invoke($"Kritik Hata: HWND_NOTOPMOST katmanına geçilemedi (Win32 Kodu: {err})! Pencere TopMost'ta takılı kalabilir.");
                return;
            }

            // 2. Aktif uygulamanın hemen arkasına yerleştir
            NativeMethods.SetLastError(0);
            bool behindRes = NativeMethods.SetWindowPos(
                _hwnd,
                currentFgHwnd,
                0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE
            );

            if (!behindRes)
            {
                int err = Marshal.GetLastWin32Error();
                StatusChanged?.Invoke($"Hata: Aktif uygulamanın arkasına yerleştirilemedi (Win32 Kodu: {err}).");
            }
            else
            {
                StatusChanged?.Invoke("Normal uygulama aktif: HWND_NOTOPMOST (Uygulamanın arkasında ✓)");
            }
        }

        // Anlamlı durum değişimini teşhis günlüğüne kaydet
        DesktopDiagnostics.LogSnapshot(_hwnd, isVerification
            ? "UpdateZOrder-Verified"
            : $"UpdateZOrder(isDesktop={isDesktop}, fgClass={fgClass})");
    }

    private void ScheduleDelayedVerification(int delayMs)
    {
        if (_isDisposed) return;
        _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(async () =>
        {
            await Task.Delay(delayMs);
            if (!_isDisposed)
            {
                _hasPendingDesktopVerify = false; // Doğrulama tetiklendi
                UpdateZOrder(isVerification: true);
            }
        }));
    }

    private void ScheduleDelayedZOrder(int delayMs)
    {
        if (_isDisposed) return;
        _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(async () =>
        {
            await Task.Delay(delayMs);
            if (!_isDisposed)
            {
                UpdateZOrder(isVerification: false);
            }
        }));
    }

    /// <summary>
    /// Verilen pencerenin Windows masaüstü yüzeyi (Progman / WorkerW / Shell) olup olmadığını kesinleştirir.
    /// File Explorer klasör pencerelerini (CabinetWClass) ve görev çubuğunu (Shell_TrayWnd) masaüstü saymaz.
    /// </summary>
    private static bool IsDefinitelyDesktop(IntPtr hwnd, string className, IntPtr shellHwnd, uint shellProcId, uint fgProcId)
    {
        if (hwnd == IntPtr.Zero) return false;

        if (shellHwnd != IntPtr.Zero && hwnd == shellHwnd)
        {
            return true;
        }

        if (shellProcId == 0 || fgProcId != shellProcId)
        {
            return false;
        }

        if (className.Equals("CabinetWClass", StringComparison.OrdinalIgnoreCase) ||
            className.Equals("ExploreWClass", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (className.Equals("Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) ||
            className.Equals("Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (className.Equals("Progman", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (className.Equals("WorkerW", StringComparison.OrdinalIgnoreCase))
        {
            IntPtr defView = NativeMethods.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView != IntPtr.Zero)
            {
                return true;
            }
        }

        return false;
    }

    private static string GetWindowClassName(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return string.Empty;
        var sb = new StringBuilder(256);
        int len = NativeMethods.GetClassName(hWnd, sb, 256);
        return len > 0 ? sb.ToString() : string.Empty;
    }

    /// <summary>
    /// Win32 mesaj döngüsü filtresi (WndProc).
    /// </summary>
    private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            // Tıklamada odak almayı engelle
            case NativeMethods.WM_MOUSEACTIVATE:
                handled = true;
                return new IntPtr(NativeMethods.MA_NOACTIVATE);

            // Win+D / Shell kaynaklı simge durumuna küçültme komutunu engelle
            case NativeMethods.WM_SYSCOMMAND when ((wParam.ToInt32() & 0xFFF0) == NativeMethods.SC_MINIMIZE):
                handled = true;
                return IntPtr.Zero;

            // Teşhis logu için pencere mesajlarını izle (sadece anlamlı değişimler)
            case NativeMethods.WM_SHOWWINDOW:
                DesktopDiagnostics.LogSnapshot(hwnd, $"WM_SHOWWINDOW(wParam={wParam.ToInt32()}, lParam={lParam.ToInt32()})");
                break;

            case NativeMethods.WM_SIZE when wParam.ToInt32() == NativeMethods.SIZE_MINIMIZED:
                DesktopDiagnostics.LogSnapshot(hwnd, "WM_SIZE(SIZE_MINIMIZED)");
                break;

            case NativeMethods.WM_SIZE when wParam.ToInt32() == NativeMethods.SIZE_RESTORED:
                DesktopDiagnostics.LogSnapshot(hwnd, "WM_SIZE(SIZE_RESTORED)");
                break;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }

        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(WndProcHook);
            _hwndSource = null;
        }
    }
}
