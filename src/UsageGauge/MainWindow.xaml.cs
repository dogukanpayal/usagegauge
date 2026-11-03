using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using UsageGauge.Native;
using UsageGauge.Services;
using UsageGauge.ViewModels;

namespace UsageGauge;

/// <summary>
/// UsageGauge ana masaüstü widget penceresi.
/// Masaüstü katmanında kalır, Win+D'de görünürlüğünü korur, odak çalmaz ve iki monitör arasında serbestçe taşınabilir.
/// </summary>
public partial class MainWindow : Window
{
    private readonly DesktopWindowManager _windowManager;
    private readonly ConfigService _configService;
    private readonly MainWindowViewModel _viewModel;
    private readonly DispatcherTimer _clockTimer;

    // Sürükleme durumu
    private bool _isDragging;
    private NativeMethods.POINT _dragStartCursor;
    private double _initialLeft;
    private double _initialTop;

    public MainWindow()
    {
        InitializeComponent();

        _configService = new ConfigService();
        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;

        _windowManager = new DesktopWindowManager(this, Dispatcher);

        _clockTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += ClockTimer_Tick;

        SourceInitialized += MainWindow_SourceInitialized;
        Loaded += MainWindow_Loaded;
        Closing += (s, e) => SaveCurrentState();
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
        // 1. DesktopWindowManager başlat (WispDock dinamik Z-order mimarisi)
        _windowManager.Initialize();
        _windowManager.DisplaySettingsChanged += MainWindow_DisplaySettingsChanged;

        // 2. Kaydedilen yapılandırmayı yükle ve ekran sınırlarını doğrula (SourceInitialized aşamasında dosyaya yazılmaz)
        LoadAndApplyConfig(saveIfNewOrAdjusted: false);

        // 3. Çoklu monitör menüsünü hazırla
        PopulateMonitorMenu();

        // 4. Geri sayım ve otomatik sorgu zamanlayıcısını başlat
        _clockTimer.Start();

        // 5. İlk taze kota sorgularını eşzamansız başlat
        _ = _viewModel.StartInitialRefreshAsync();
    }

    private void MainWindow_Loaded(object? sender, EventArgs e)
    {
        // Pencere düzeni hesaplanıp ActualWidth/ActualHeight belirlendikten sonra sınır denetimini tazele
        // ve dosya yoksa veya koordinat düzeltildiyse ilk kaydı yap
        LoadAndApplyConfig(saveIfNewOrAdjusted: true);
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _clockTimer.Stop();
        _windowManager.DisplaySettingsChanged -= MainWindow_DisplaySettingsChanged;
        // SaveCurrentState() Closing olayında bir kez çalıştırıldığı için burada mükerrer kayıt yapılmaz
        _viewModel.Dispose();
        _windowManager.Dispose();
    }

    private void MainWindow_DisplaySettingsChanged()
    {
        // Çoklu monitör, çözünürlük veya DPI değiştiğinde sağ tık menüsünü ve konum sınırlarını güncelle
        PopulateMonitorMenu();
        LoadAndApplyConfig(saveIfNewOrAdjusted: true);
        _windowManager.UpdateZOrder();
    }

    private void ClockTimer_Tick(object? sender, EventArgs e)
    {
        _viewModel.Tick(DateTimeOffset.UtcNow);
    }

    #region Konum ve Yapılandırma Yönetimi

    private void LoadAndApplyConfig(bool saveIfNewOrAdjusted = false)
    {
        AppConfig config = _configService.LoadConfig();

        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        IReadOnlyList<MonitorDescriptor> monitors = ScreenHelper.GetMonitors(dpi);

        double width = ActualWidth > 0 ? ActualWidth : (Width > 0 ? Width : 340);
        double height = ActualHeight > 0 ? ActualHeight : (Height > 0 ? Height : 460);

        var (safeLeft, safeTop) = ConfigService.EnsureVisibleBounds(
            config.WindowLeft,
            config.WindowTop,
            width,
            height,
            monitors,
            config.MonitorDeviceName);

        Left = safeLeft;
        Top = safeTop;
        Opacity = config.Opacity;

        UpdateOpacityCheckmarks(config.Opacity);

        // Yalnızca Loaded aşamasında ve gerekliyse ilk kaydı yap
        if (saveIfNewOrAdjusted)
        {
            if (!System.IO.File.Exists(_configService.ConfigFilePath) || safeLeft != config.WindowLeft || safeTop != config.WindowTop)
            {
                SaveCurrentState();
            }
        }
    }

    private void SaveCurrentState()
    {
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        IReadOnlyList<MonitorDescriptor> monitors = ScreenHelper.GetMonitors(dpi);

        double actualW = ActualWidth > 0 ? ActualWidth : (Width > 0 ? Width : 340);
        double actualH = ActualHeight > 0 ? ActualHeight : (Height > 0 ? Height : 460);

        Rect currentRect = new(Left, Top, actualW, actualH);
        // Pencere birden fazla monitöre taşıyorsa, pencerenin en büyük bölümünü içeren monitörü kaydet
        MonitorDescriptor? dominantMonitor = ScreenHelper.FindDominantMonitor(currentRect, monitors);

        var config = new AppConfig
        {
            WindowLeft = Left,
            WindowTop = Top,
            MonitorDeviceName = dominantMonitor?.DeviceName,
            Opacity = Opacity
        };

        _configService.SaveConfig(config);
    }

    private void PopulateMonitorMenu()
    {
        MenuMoveToMonitor.Items.Clear();

        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        IReadOnlyList<MonitorDescriptor> monitors = ScreenHelper.GetMonitors(dpi);

        for (int i = 0; i < monitors.Count; i++)
        {
            MonitorDescriptor mon = monitors[i];
            MenuItem item = new()
            {
                Header = mon.DisplayName,
                Tag = mon
            };
            item.Click += (s, e) =>
            {
                if (s is MenuItem mi && mi.Tag is MonitorDescriptor targetMon)
                {
                    MoveToMonitor(targetMon);
                }
            };
            MenuMoveToMonitor.Items.Add(item);
        }
    }

    private void MoveToMonitor(MonitorDescriptor targetMon)
    {
        // Hedef monitörün çalışma alanının sağ üst veya uygun bir yerine yerleştir
        double newLeft = targetMon.WorkAreaDip.Left + 40;
        double newTop = targetMon.WorkAreaDip.Top + 40;

        Left = newLeft;
        Top = newTop;

        _windowManager.UpdateZOrder();
        SaveCurrentState();
    }

    private void UpdateOpacityCheckmarks(double opacity)
    {
        MenuOpacity100.IsChecked = Math.Abs(opacity - 1.0) < 0.05;
        MenuOpacity80.IsChecked = Math.Abs(opacity - 0.8) < 0.05;
        MenuOpacity60.IsChecked = Math.Abs(opacity - 0.6) < 0.05;
    }

    #endregion

    #region Sürükleme ve Taşıma (Odak Çalmadan, DPI Uyumlu)

    private void DragHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject depObj && FindVisualParent<Button>(depObj) != null)
        {
            return;
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
            double scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
            double scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;

            double deltaX = (currentCursor.X - _dragStartCursor.X) / scaleX;
            double deltaY = (currentCursor.Y - _dragStartCursor.Y) / scaleY;

            Left = _initialLeft + deltaX;
            Top = _initialTop + deltaY;

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

            // Sürükleme bitince Z-sırasını tazele ve yeni konumu kaydet
            _windowManager.UpdateZOrder();
            SaveCurrentState();
        }
    }

    private void DragHandle_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            SaveCurrentState();
        }
    }

    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        DependencyObject? parentObject = VisualTreeHelper.GetParent(child);
        if (parentObject == null) return null;
        if (parentObject is T parent) return parent;
        return FindVisualParent<T>(parentObject);
    }

    #endregion

    #region Kullanıcı Arayüzü ve Menü Olayları

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        _ = _viewModel.TriggerManualRefreshAsync();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MenuRefresh_Click(object sender, RoutedEventArgs e)
    {
        _ = _viewModel.TriggerManualRefreshAsync();
    }

    private void MenuResetPosition_Click(object sender, RoutedEventArgs e)
    {
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        IReadOnlyList<MonitorDescriptor> monitors = ScreenHelper.GetMonitors(dpi);
        if (monitors.Count > 0)
        {
            MonitorDescriptor primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
            double width = ActualWidth > 0 ? ActualWidth : (Width > 0 ? Width : 340);
            double height = ActualHeight > 0 ? ActualHeight : (Height > 0 ? Height : 460);

            // Birincil ekranın sağ alt güvenli çalışma alanına konumlandır
            double safeLeft = primary.WorkAreaDip.Right - width - 24;
            double safeTop = primary.WorkAreaDip.Bottom - height - 48;

            if (safeLeft < primary.WorkAreaDip.Left) safeLeft = primary.WorkAreaDip.Left + 20;
            if (safeTop < primary.WorkAreaDip.Top) safeTop = primary.WorkAreaDip.Top + 20;

            Left = safeLeft;
            Top = safeTop;

            _windowManager.UpdateZOrder();
            SaveCurrentState();
        }
        else
        {
            Left = 80;
            Top = 80;
            SaveCurrentState();
        }
    }

    private void MenuOpacity_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && item.Tag is string tagStr && double.TryParse(tagStr, System.Globalization.CultureInfo.InvariantCulture, out double op))
        {
            Opacity = Math.Clamp(op, 0.2, 1.0);
            UpdateOpacityCheckmarks(Opacity);
            SaveCurrentState();
        }
    }

    private void MenuExit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    #endregion
}
