using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using UsageGauge.Native;

namespace UsageGauge.Services;

/// <summary>
/// Bildirim alanı olayının karşılık geldiği eylem türü.
/// </summary>
public enum TrayNotificationAction
{
    None,
    Show,
    ContextMenu
}

/// <summary>
/// Windows bildirim alanında (System Tray) UsageGauge simgesini, sol tıkla geri açma
/// ve sağ tık bağlam menüsü (Göster, Şimdi Yenile, Çıkış) etkileşimlerini yöneten sınıf.
/// Win32 Shell_NotifyIcon API'sini kullanır ve uygulama kapandığında ikonun bildirim
/// alanından temizlenmesini (Dispose) garanti eder.
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private const int TrayIconId = 1001;

    public delegate bool ShellNotifyDelegate(int message, ref NativeMethods.NOTIFYICONDATA data);

    private readonly IntPtr _hwnd;
    private readonly Action _showAction;
    private readonly Action _refreshAction;
    private readonly Action _exitAction;
    private readonly Func<bool>? _isWindowHiddenFunc;
    private readonly ContextMenu _contextMenu;
    private readonly int _taskbarCreatedMsg;
    private readonly ShellNotifyDelegate _shellNotifyProc;

    private NativeMethods.NOTIFYICONDATA _nid;
    private IntPtr _hIcon = IntPtr.Zero;
    private bool _isIconCreated;
    private bool _isVersion4Active;
    private bool _isDisposed;
    private long _lastShowTimestampMs;

    public bool IsIconCreated => _isIconCreated;
    public bool IsVersion4Active => _isVersion4Active;
    public bool IsAvailable => _isIconCreated && !_isDisposed;
    public int TaskbarCreatedMessage => _taskbarCreatedMsg;

    public TrayIconManager(
        IntPtr hwnd,
        Action showAction,
        Action refreshAction,
        Action exitAction,
        Func<bool>? isWindowHiddenFunc = null)
        : this(hwnd, showAction, refreshAction, exitAction, isWindowHiddenFunc, NativeMethods.Shell_NotifyIcon)
    {
    }

    internal TrayIconManager(
        IntPtr hwnd,
        Action showAction,
        Action refreshAction,
        Action exitAction,
        ShellNotifyDelegate shellNotifyProc)
        : this(hwnd, showAction, refreshAction, exitAction, null, shellNotifyProc)
    {
    }

    internal TrayIconManager(
        IntPtr hwnd,
        Action showAction,
        Action refreshAction,
        Action exitAction,
        Func<bool>? isWindowHiddenFunc,
        ShellNotifyDelegate shellNotifyProc)
    {
        _hwnd = hwnd;
        _showAction = showAction ?? throw new ArgumentNullException(nameof(showAction));
        _refreshAction = refreshAction ?? throw new ArgumentNullException(nameof(refreshAction));
        _exitAction = exitAction ?? throw new ArgumentNullException(nameof(exitAction));
        _isWindowHiddenFunc = isWindowHiddenFunc;
        _shellNotifyProc = shellNotifyProc ?? throw new ArgumentNullException(nameof(shellNotifyProc));

        _contextMenu = BuildTrayContextMenu();
        _taskbarCreatedMsg = NativeMethods.RegisterWindowMessage("TaskbarCreated");

        InitializeTrayIcon();
    }

    public bool TryEnsureIconCreated()
    {
        if (_isDisposed) return false;
        if (_isIconCreated) return true;

        InitializeTrayIcon();
        return _isIconCreated;
    }

    private void InitializeTrayIcon()
    {
        if (_hIcon == IntPtr.Zero)
        {
            _hIcon = LoadApplicationIcon();
        }

        _nid = new NativeMethods.NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = TrayIconId,
            uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP,
            uCallbackMessage = NativeMethods.WM_TRAYICON,
            hIcon = _hIcon,
            szTip = "UsageGauge - AI Kota Göstergesi"
        };

        bool res = _shellNotifyProc(NativeMethods.NIM_ADD, ref _nid);
        if (res)
        {
            _isIconCreated = true;
            // Version 4 bildirim modeli desteği
            _nid.uVersionOrTimeout = NativeMethods.NOTIFYICON_VERSION_4;
            _isVersion4Active = _shellNotifyProc(NativeMethods.NIM_SETVERSION, ref _nid);
        }
        else
        {
            _isIconCreated = false;
            _isVersion4Active = false;
        }
    }

    /// <summary>
    /// Pencereye ulaşan bildirim mesajını ayrıştırarak hangi eyleme karşılık geldiğini belirler.
    /// V4 modelinde NIN_SELECT/NIN_KEYSELECT, eski modelde ise WM_LBUTTONUP eylemi tetikler.
    /// Tek tıklamada mükerrer tetikleme oluşmaması için v4 modunda legacy mouse mesajları elenir.
    /// </summary>
    public static TrayNotificationAction ParseNotificationAction(int msg, IntPtr wParam, IntPtr lParam, bool isVersion4)
    {
        if (msg != NativeMethods.WM_TRAYICON)
        {
            return TrayNotificationAction.None;
        }

        if (isVersion4)
        {
            // NOTIFYICON_VERSION_4 modelinde LOWORD(lParam) olay kodunu taşır
            int evt = (int)(lParam.ToInt64() & 0xFFFF);
            switch (evt)
            {
                case NativeMethods.NIN_SELECT:
                case NativeMethods.NIN_KEYSELECT:
                    return TrayNotificationAction.Show;

                case NativeMethods.WM_CONTEXTMENU:
                    return TrayNotificationAction.ContextMenu;

                default:
                    // V4 modunda eski WM_LBUTTONUP veya fare ön mesajlarını yoksayarak tek tıklamada mükerrerliği engelle
                    return TrayNotificationAction.None;
            }
        }
        else
        {
            // Klasik modelde lParam doğrudan fare pencere mesajıdır
            int mouseMsg = (int)(lParam.ToInt64() & 0xFFFF);
            switch (mouseMsg)
            {
                case NativeMethods.WM_LBUTTONUP:
                    return TrayNotificationAction.Show;

                case NativeMethods.WM_RBUTTONUP:
                case NativeMethods.WM_CONTEXTMENU:
                    return TrayNotificationAction.ContextMenu;

                default:
                    // WM_LBUTTONDOWN gibi ön mesajlar tek tıklamada mükerrer tetiklememesi için yoksayılır
                    return TrayNotificationAction.None;
            }
        }
    }

    /// <summary>
    /// Windows Explorer çöktükten sonra yeniden başlatıldığında TaskbarCreated mesajını işler.
    /// Simgeyi yeniden eklemeyi dener; başarısızsa simge durumunu kullanılamaz yapar ve widget
    /// gizliyse kullanıcının erişebilmesi için pencereyi güvenli biçimde yeniden gösterir.
    /// Başarılıysa v4 bildirim sürümünü yeniden ayarlar ve gerçek sonucu kaydeder.
    /// </summary>
    internal bool HandleTaskbarCreated()
    {
        if (_isDisposed) return false;

        bool readded = _shellNotifyProc(NativeMethods.NIM_ADD, ref _nid);
        if (readded)
        {
            _isIconCreated = true;
            _nid.uVersionOrTimeout = NativeMethods.NOTIFYICON_VERSION_4;
            _isVersion4Active = _shellNotifyProc(NativeMethods.NIM_SETVERSION, ref _nid);
            return true;
        }
        else
        {
            _isIconCreated = false;
            _isVersion4Active = false;

            // Widget gizliyse erişilebilir kalması için pencereyi güvenli biçimde yeniden göster
            if (_isWindowHiddenFunc?.Invoke() == true)
            {
                _showAction();
            }

            return false;
        }
    }

    /// <summary>
    /// MainWindow'un WndProc kancasından gelen mesajları işler.
    /// </summary>
    public bool ProcessWindowMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (_isDisposed) return false;

        // Windows Explorer yeniden başlatıldığında bildirim alanındaki simgeyi yeniden kaydet
        if (_taskbarCreatedMsg != 0 && msg == _taskbarCreatedMsg)
        {
            HandleTaskbarCreated();
            return true;
        }

        TrayNotificationAction action = ParseNotificationAction(msg, wParam, lParam, _isVersion4Active);
        switch (action)
        {
            case TrayNotificationAction.Show:
                long now = Environment.TickCount64;
                if (now - _lastShowTimestampMs >= 200)
                {
                    _lastShowTimestampMs = now;
                    _showAction();
                }
                return true;

            case TrayNotificationAction.ContextMenu:
                ShowContextMenu();
                return true;

            default:
                return false;
        }
    }

    private void ShowContextMenu()
    {
        if (_contextMenu == null) return;

        // Menü dışına tıklandığında menünün düzgün kapanması için foreground window'u ata
        NativeMethods.SetForegroundWindow(_hwnd);
        _contextMenu.Placement = PlacementMode.MousePoint;
        _contextMenu.IsOpen = true;
    }

    private ContextMenu BuildTrayContextMenu()
    {
        var menu = new ContextMenu();

        var miShow = new MenuItem { Header = "Göster" };
        miShow.Click += (s, e) => _showAction();
        menu.Items.Add(miShow);

        var miRefresh = new MenuItem { Header = "Şimdi Yenile" };
        miRefresh.Click += (s, e) => _refreshAction();
        menu.Items.Add(miRefresh);

        menu.Items.Add(new Separator());

        var miExit = new MenuItem { Header = "Çıkış" };
        miExit.Click += (s, e) => _exitAction();
        menu.Items.Add(miExit);

        return menu;
    }

    private static IntPtr LoadApplicationIcon()
    {
        try
        {
            // 1. Çalışan EXE'nin gömülü ikonunu al
            string? exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                IntPtr hIcon = NativeMethods.ExtractIcon(IntPtr.Zero, exePath, 0);
                if (hIcon != IntPtr.Zero && hIcon != new IntPtr(1))
                {
                    return hIcon;
                }
            }
        }
        catch { }

        try
        {
            // 2. Proje/uygulama dizinindeki Resources\UsageGauge.ico dosyasından yükle
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string localIco = Path.Combine(baseDir, "Resources", "UsageGauge.ico");
            if (File.Exists(localIco))
            {
                IntPtr hIcon = NativeMethods.LoadImage(IntPtr.Zero, localIco, 1 /*IMAGE_ICON*/, 16, 16, 0x0010 /*LR_LOADFROMFILE*/);
                if (hIcon != IntPtr.Zero)
                {
                    return hIcon;
                }
            }
        }
        catch { }

        // 3. Fallback: Standart sistem bilgi ikonu
        return NativeMethods.LoadIcon(IntPtr.Zero, (IntPtr)32516 /*IDI_INFORMATION*/);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_isIconCreated)
        {
            _nid.uFlags = 0;
            _shellNotifyProc(NativeMethods.NIM_DELETE, ref _nid);
            _isIconCreated = false;
            _isVersion4Active = false;
        }

        if (_hIcon != IntPtr.Zero)
        {
            try
            {
                NativeMethods.DestroyIcon(_hIcon);
            }
            catch { }
            _hIcon = IntPtr.Zero;
        }
    }
}
