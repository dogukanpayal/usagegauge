using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using UsageGauge.Native;

namespace UsageGauge;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly DesktopWindowManager _windowManager;
    private readonly DispatcherTimer _foregroundTimer;
    private int _heartbeatCounter;

    // Sürükleme durumu
    private bool _isDragging;
    private NativeMethods.POINT _dragStartCursor;
    private double _initialLeft;
    private double _initialTop;

    // Odak karşılaştırma durumu (Test 3)
    private IntPtr _baselineHwnd = IntPtr.Zero;
    private string _baselineTitle = string.Empty;

    public MainWindow()
    {
        InitializeComponent();

        _windowManager = new DesktopWindowManager(this, Dispatcher);
        _windowManager.StatusChanged += OnWindowManagerStatusChanged;

        _foregroundTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _foregroundTimer.Tick += ForegroundTimer_Tick;

        SourceInitialized += MainWindow_SourceInitialized;
        Closed += MainWindow_Closed;

        // Masaüstü widget'ı asla simge durumuna geçmemelidir
        StateChanged += (s, e) =>
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
        };
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        bool initOk = _windowManager.Initialize();
        if (initOk)
        {
            TxtHwnd.Text = $"0x{_windowManager.Handle.ToInt64():X8}";
            UpdateCoordinatesDisplay();
            _foregroundTimer.Start();
        }
        else
        {
            TxtStatus.Text = "Hata: DesktopWindowManager başlatılamadı.";
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _foregroundTimer.Stop();
        _windowManager.Dispose();
    }

    private void OnWindowManagerStatusChanged(string message)
    {
        Dispatcher.Invoke(() =>
        {
            TxtStatus.Text = message;
            TxtZOrderStatus.Text = _windowManager.IsDesktopActive
                ? "Masaüstü (HWND_TOPMOST)"
                : "Normal Uygulama (HWND_NOTOPMOST)";
            TxtZOrderStatus.Foreground = _windowManager.IsDesktopActive
                ? new SolidColorBrush(Color.FromRgb(56, 189, 248))
                : new SolidColorBrush(Color.FromRgb(74, 222, 128));
        });
    }

    #region Canlı Odak & Teşhis İzleme

    private void ForegroundTimer_Tick(object? sender, EventArgs e)
    {
        IntPtr fgHwnd = NativeMethods.GetForegroundWindow();
        string currentTitle = GetWindowTitle(fgHwnd);
        TxtCurrentWindow.Text = $"{currentTitle} (0x{fgHwnd.ToInt64():X8})";

        // Odak durumunu değerlendir
        if (_baselineHwnd == IntPtr.Zero)
        {
            TxtFocusResult.Text = "Referans pencere henüz kilitlenmedi";
            TxtFocusResult.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)); // Gri
        }
        else if (fgHwnd == _windowManager.Handle)
        {
            TxtFocusResult.Text = "HATA: UsageGauge odağı çaldı! ✕";
            TxtFocusResult.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Kırmızı
        }
        else if (fgHwnd == _baselineHwnd)
        {
            TxtFocusResult.Text = "BAŞARILI: Odak referansta korundu ✓";
            TxtFocusResult.Foreground = new SolidColorBrush(Color.FromRgb(74, 222, 128)); // Yeşil
        }
        else
        {
            TxtFocusResult.Text = "UYARI: Farklı 3. pencereye geçildi (Geçersiz!)";
            TxtFocusResult.Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Turuncu
        }

        // Periyodik kalp atışı teşhis kaydı (yaklaşık 10 saniyede bir kontrollü örnekleme)
        if (++_heartbeatCounter % 30 == 0)
        {
            DesktopDiagnostics.LogSnapshot(_windowManager.Handle, "Heartbeat");
        }
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "(Boş)";
        var sb = new StringBuilder(256);
        NativeMethods.GetWindowText(hwnd, sb, 256);
        string text = sb.ToString();
        return string.IsNullOrWhiteSpace(text) ? "(Başlıksız)" : text;
    }

    private void BtnLockBaseline_Click(object sender, RoutedEventArgs e)
    {
        IntPtr fgHwnd = NativeMethods.GetForegroundWindow();
        if (fgHwnd == _windowManager.Handle)
        {
            TxtStatus.Text = "Uyarı: Odak şu an UsageGauge üzerinde, kilitlenemez.";
            return;
        }

        _baselineHwnd = fgHwnd;
        _baselineTitle = GetWindowTitle(fgHwnd);
        TxtBaselineWindow.Text = $"{_baselineTitle} (0x{_baselineHwnd.ToInt64():X8})";
        TxtBaselineWindow.Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248));
        TxtStatus.Text = $"Referans pencere kilitlendi: {_baselineTitle}.";
    }

    #endregion

    #region Sürükleme ve Taşıma (Odak Çalmadan, Çoklu Monitör DPI Uyumlu)

    private void DragHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject depObj && FindVisualParent<Button>(depObj) != null)
        {
            return;
        }

        if (_baselineHwnd == IntPtr.Zero)
        {
            IntPtr fgHwnd = NativeMethods.GetForegroundWindow();
            if (fgHwnd != _windowManager.Handle && fgHwnd != IntPtr.Zero)
            {
                _baselineHwnd = fgHwnd;
                _baselineTitle = GetWindowTitle(fgHwnd);
                TxtBaselineWindow.Text = $"{_baselineTitle} (0x{_baselineHwnd.ToInt64():X8})";
                TxtBaselineWindow.Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248));
            }
        }

        NativeMethods.GetCursorPos(out _dragStartCursor);
        _initialLeft = Left;
        _initialTop = Top;

        _isDragging = true;
        DragHandle.CaptureMouse();
        e.Handled = true;
    }

    private void DragHandle_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (NativeMethods.GetCursorPos(out NativeMethods.POINT currentCursor))
        {
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            double deltaX = (currentCursor.X - _dragStartCursor.X) / dpi.DpiScaleX;
            double deltaY = (currentCursor.Y - _dragStartCursor.Y) / dpi.DpiScaleY;

            Left = _initialLeft + deltaX;
            Top = _initialTop + deltaY;

            UpdateCoordinatesDisplay();
            e.Handled = true;
        }
    }

    private void DragHandle_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            DragHandle.ReleaseMouseCapture();
            e.Handled = true;

            // Sürükleme bitince Z-sırasını tazele
            _windowManager.UpdateZOrder();
            UpdateCoordinatesDisplay();
            DesktopDiagnostics.LogSnapshot(_windowManager.Handle, "DragEnded");
        }
    }

    private void DragHandle_LostMouseCapture(object sender, MouseEventArgs e)
    {
        _isDragging = false;
    }

    private void UpdateCoordinatesDisplay()
    {
        TxtCoords.Text = $"X: {(int)Left}, Y: {(int)Top}";
    }

    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        DependencyObject? parentObject = VisualTreeHelper.GetParent(child);
        if (parentObject == null) return null;
        if (parentObject is T parent) return parent;
        return FindVisualParent<T>(parentObject);
    }

    #endregion

    #region Buton Olayları

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void BtnRefreshZOrder_Click(object sender, RoutedEventArgs e)
    {
        _windowManager.UpdateZOrder();
        TxtStatus.Text = "Z-Sırası el ile güncellendi.";
    }

    private void BtnOpenLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (File.Exists(DesktopDiagnostics.LogFilePath))
            {
                Process.Start(new ProcessStartInfo("notepad.exe", DesktopDiagnostics.LogFilePath) { UseShellExecute = true });
            }
            else
            {
                TxtStatus.Text = "Teşhis günlüğü henüz oluşturulmadı.";
            }
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Log açma hatası: {ex.Message}";
        }
    }

    #endregion
}
