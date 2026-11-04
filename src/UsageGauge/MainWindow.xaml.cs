using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
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
    private readonly SingleInstanceService? _singleInstanceService;

    private TrayIconManager? _trayIconManager;
    private HwndSource? _hwndSource;
    private SettingsWindow? _settingsWindow;
    private bool _isRealExit;
    private bool _isAlwaysOnTop;
    private string _currentViewMode = "Normal";

    public const double NormalWidth = 340;
    public const double MediumWidth = 270;
    public const double MediumMiniWidth = 200;
    public const double MiniWidth = 340;

    public SettingsWindow? ActiveSettingsWindow => _settingsWindow;

    // Menü öğesi uyumluluk özellikleri (kod ve test uyumluluğu için)
    public MenuItem MenuAlwaysOnTop { get; set; } = new();
    public MenuItem MenuViewNormal { get; set; } = new();
    public MenuItem MenuViewMedium { get; set; } = new();
    public MenuItem MenuViewMediumMini { get; set; } = new();
    public MenuItem MenuViewMini { get; set; } = new();
    public MenuItem MenuMoveToMonitor { get; set; } = new();
    public MenuItem MenuOpacity100 { get; set; } = new();
    public MenuItem MenuOpacity80 { get; set; } = new();
    public MenuItem MenuOpacity60 { get; set; } = new();

    public bool IsAlwaysOnTop
    {
        get => _isAlwaysOnTop;
        set => SetAlwaysOnTop(value, saveConfig: false);
    }

    public string CurrentViewMode => _currentViewMode;
    public bool IsMiniView => _currentViewMode.Equals("Mini", StringComparison.OrdinalIgnoreCase);
    public bool IsMediumMiniView => _currentViewMode.Equals("Orta-Mini", StringComparison.OrdinalIgnoreCase);
    public bool IsMediumView => _currentViewMode.Equals("Orta", StringComparison.OrdinalIgnoreCase);
    public bool IsNormalView => _currentViewMode.Equals("Normal", StringComparison.OrdinalIgnoreCase);
    public DesktopWindowManager WindowManager => _windowManager;

    public TrayIconManager? TrayManager
    {
        get => _trayIconManager;
        internal set => _trayIconManager = value;
    }

    public bool IsTrayAvailable => _trayIconManager != null && _trayIconManager.IsAvailable;

    public static Action<string>? DefaultTrayUnavailableNotifier { get; set; } = msg =>
    {
        // xUnit / testhost test ortamlarında modal mesaj kutusu açarak testi bloklama
        if (AppDomain.CurrentDomain.FriendlyName.Contains("testhost", StringComparison.OrdinalIgnoreCase) ||
            AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name?.StartsWith("xunit", StringComparison.OrdinalIgnoreCase) == true))
        {
            return;
        }

        MessageBox.Show(
            msg,
            "UsageGauge - Bildirim Alanı Uyarısı",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    };

    public Action<string>? TrayUnavailableNotifier { get; set; } = DefaultTrayUnavailableNotifier;

    // Sürükleme durumu
    private bool _isDragging;
    private NativeMethods.POINT _dragStartCursor;
    private double _initialLeft;
    private double _initialTop;

    public MainWindow(SingleInstanceService? singleInstanceService = null)
    {
        InitializeComponent();

        _singleInstanceService = singleInstanceService;
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
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;

        UpdateViewModeCheckmarks(_currentViewMode);
        UpdateOpacityCheckmarks(1.0);

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

        // 2. Bildirim alanı (System Tray) yöneticisini kur ve mesaj kancasını bağla
        IntPtr hwnd = _windowManager.Handle;
        _trayIconManager ??= new TrayIconManager(
            hwnd,
            ShowFromTray,
            () => _ = _viewModel.TriggerManualRefreshAsync(),
            ExitApplication,
            isWindowHiddenFunc: () => Visibility != Visibility.Visible);

        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource?.AddHook(WndProc);

        // 3. Tek örnek sinyal dinleyicisini bağla
        _singleInstanceService?.StartListening(() => Dispatcher.BeginInvoke(new Action(ShowFromTray)));

        // 4. Kaydedilen yapılandırmayı yükle ve ekran sınırlarını doğrula (SourceInitialized aşamasında dosyaya yazılmaz)
        LoadAndApplyConfig(saveIfNewOrAdjusted: false);

        // 5. Çoklu monitör menüsünü hazırla
        PopulateMonitorMenu();

        // 6. Geri sayım ve otomatik sorgu zamanlayıcısını başlat
        _clockTimer.Start();

        // 7. İlk taze kota sorgularını eşzamansız başlat
        _ = _viewModel.StartInitialRefreshAsync();
    }

    private void MainWindow_Loaded(object? sender, EventArgs e)
    {
        // Pencere düzeni hesaplanıp ActualWidth/ActualHeight belirlendikten sonra sınır denetimini tazele
        // ve dosya yoksa veya koordinat düzeltildiyse ilk kaydı yap
        LoadAndApplyConfig(saveIfNewOrAdjusted: true);
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Gerçek bir Çıkış komutu verilmediği sürece (X düğmesi veya Alt+F4) pencereyi kapatmak yerine bildirim alanına gizle
        if (!_isRealExit)
        {
            e.Cancel = true;

            // Tepsi simgesi geçerli veya kurtarılabiliyorsa bildirim alanına gizle
            if (IsTrayAvailable || (_trayIconManager != null && _trayIconManager.TryEnsureIconCreated()))
            {
                HideToTray();
                return;
            }

            // Bildirim alanı simgesi kullanılamıyorsa pencere gizlenemez; kullanıcı erişimini kaybetmemesi için açık tutulur
            TrayUnavailableNotifier?.Invoke("Bildirim alanı (System Tray) simgesi kullanılamadığı için pencere gizlenemiyor. Uygulamayı tamamen kapatmak için sağ tık menüsündeki 'Çıkış' seçeneğini kullanabilirsiniz.");
            return;
        }

        SaveCurrentState();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        CloseSettingsWindow();
        _clockTimer.Stop();
        _windowManager.DisplaySettingsChanged -= MainWindow_DisplaySettingsChanged;

        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }

        // Bildirim alanı simgesini derhal temizle (hayalet ikon kalmaması için)
        _trayIconManager?.Dispose();
        _trayIconManager = null;

        _singleInstanceService?.Dispose();

        _viewModel.Dispose();
        _windowManager.Dispose();

        Application.Current?.Shutdown();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_trayIconManager?.ProcessWindowMessage(msg, wParam, lParam) == true)
        {
            handled = true;
            return IntPtr.Zero;
        }

        return IntPtr.Zero;
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

    public Func<IReadOnlyList<MonitorDescriptor>>? MonitorsProvider { get; set; }

    public IReadOnlyList<MonitorDescriptor> GetCurrentMonitors()
    {
        if (MonitorsProvider != null)
        {
            return MonitorsProvider();
        }

        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        return ScreenHelper.GetMonitors(dpi);
    }

    private void LoadAndApplyConfig(bool saveIfNewOrAdjusted = false)
    {
        AppConfig config = _configService.LoadConfig();

        ApplyViewMode(config.ViewMode, saveConfig: false, config.MonitorDeviceName);
        SetAlwaysOnTop(config.IsAlwaysOnTop, saveConfig: false);

        IReadOnlyList<MonitorDescriptor> monitors = GetCurrentMonitors();

        double targetW = _currentViewMode switch
        {
            "Mini" => MiniWidth,
            "Orta-Mini" => MediumMiniWidth,
            "Orta" => MediumWidth,
            _ => NormalWidth
        };
        double targetH = _currentViewMode switch
        {
            "Mini" => 80,
            "Orta-Mini" => 140,
            "Orta" => 170,
            _ => 450
        };
        double width = ActualWidth > 0 ? ActualWidth : targetW;
        double height = ActualHeight > 0 ? ActualHeight : targetH;

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

    public bool SetAlwaysOnTop(bool alwaysOnTop, bool saveConfig = true)
    {
        bool applied = _windowManager.SetAlwaysOnTop(alwaysOnTop);
        if (!applied)
        {
            // Menü işareti ve kaydedilen tercih, gerçek işlem başarısızsa sahte başarı göstermesin
            UpdateAlwaysOnTopCheckmark(_windowManager.IsAlwaysOnTop);
            _isAlwaysOnTop = _windowManager.IsAlwaysOnTop;
            if (_settingsWindow != null && _settingsWindow.IsLoaded)
            {
                _settingsWindow.SyncSettings();
            }
            return false;
        }

        _isAlwaysOnTop = alwaysOnTop;
        UpdateAlwaysOnTopCheckmark(alwaysOnTop);

        if (saveConfig)
        {
            SaveCurrentState();
        }

        if (_settingsWindow != null && _settingsWindow.IsLoaded)
        {
            _settingsWindow.SyncSettings();
        }

        return true;
    }

    public void ApplyViewMode(string viewMode, bool saveConfig = true, string? preferredDeviceName = null)
    {
        IReadOnlyList<MonitorDescriptor> monitors = GetCurrentMonitors();

        // 1. Hedef monitörü boyut değişmeden önceki gerçek pencere konumu ve boyutundan belirle
        string? targetDeviceName = preferredDeviceName;
        if (string.IsNullOrEmpty(targetDeviceName) && monitors.Count > 0)
        {
            double oldWidth = ActualWidth > 0 ? ActualWidth : (Width > 0 ? Width : NormalWidth);
            double oldHeight = ActualHeight > 0 ? ActualHeight : (Height > 0 ? Height : 460);
            Rect preChangeRect = new(Left, Top, oldWidth, oldHeight);
            MonitorDescriptor? dominantMon = ScreenHelper.FindDominantMonitor(preChangeRect, monitors);
            targetDeviceName = dominantMon?.DeviceName;
        }

        string sanitizedMode = ConfigService.SanitizeViewMode(viewMode, configVersion: 3);
        _currentViewMode = sanitizedMode;

        if (sanitizedMode == "Mini")
        {
            if (NormalViewPanel != null) NormalViewPanel.Visibility = Visibility.Collapsed;
            if (MediumViewPanel != null) MediumViewPanel.Visibility = Visibility.Collapsed;
            if (MediumMiniViewPanel != null) MediumMiniViewPanel.Visibility = Visibility.Collapsed;
            if (MiniViewPanel != null) MiniViewPanel.Visibility = Visibility.Visible;
            if (MainRootGrid != null) MainRootGrid.Margin = new Thickness(10, 6, 10, 7);
            if (HeaderGrid != null) HeaderGrid.Margin = new Thickness(0, 0, 0, 4);
            Width = MiniWidth;
        }
        else if (sanitizedMode == "Orta-Mini")
        {
            if (NormalViewPanel != null) NormalViewPanel.Visibility = Visibility.Collapsed;
            if (MediumViewPanel != null) MediumViewPanel.Visibility = Visibility.Collapsed;
            if (MediumMiniViewPanel != null) MediumMiniViewPanel.Visibility = Visibility.Visible;
            if (MiniViewPanel != null) MiniViewPanel.Visibility = Visibility.Collapsed;
            if (MainRootGrid != null) MainRootGrid.Margin = new Thickness(10, 8, 10, 10);
            if (HeaderGrid != null) HeaderGrid.Margin = new Thickness(0, 0, 0, 8);
            Width = MediumMiniWidth;
        }
        else if (sanitizedMode == "Orta")
        {
            if (NormalViewPanel != null) NormalViewPanel.Visibility = Visibility.Collapsed;
            if (MediumViewPanel != null) MediumViewPanel.Visibility = Visibility.Visible;
            if (MediumMiniViewPanel != null) MediumMiniViewPanel.Visibility = Visibility.Collapsed;
            if (MiniViewPanel != null) MiniViewPanel.Visibility = Visibility.Collapsed;
            if (MainRootGrid != null) MainRootGrid.Margin = new Thickness(10, 8, 10, 10);
            if (HeaderGrid != null) HeaderGrid.Margin = new Thickness(0, 0, 0, 8);
            Width = MediumWidth;
        }
        else
        {
            if (NormalViewPanel != null) NormalViewPanel.Visibility = Visibility.Visible;
            if (MediumViewPanel != null) MediumViewPanel.Visibility = Visibility.Collapsed;
            if (MediumMiniViewPanel != null) MediumMiniViewPanel.Visibility = Visibility.Collapsed;
            if (MiniViewPanel != null) MiniViewPanel.Visibility = Visibility.Collapsed;
            if (MainRootGrid != null) MainRootGrid.Margin = new Thickness(10, 8, 10, 10);
            if (HeaderGrid != null) HeaderGrid.Margin = new Thickness(0, 0, 0, 8);
            Width = NormalWidth;
        }

        UpdateViewModeCheckmarks(sanitizedMode);

        // 2. Yeni düzen ölçüldükten sonra gerçek ActualWidth/ActualHeight ile aynı monitörün çalışma alanında sınırla
        UpdateLayout();

        if (monitors.Count > 0)
        {
            double fallbackW = sanitizedMode switch
            {
                "Mini" => MiniWidth,
                "Orta-Mini" => MediumMiniWidth,
                "Orta" => MediumWidth,
                _ => NormalWidth
            };
            double fallbackH = sanitizedMode switch
            {
                "Mini" => 80,
                "Orta-Mini" => 140,
                "Orta" => 170,
                _ => 450
            };

            double newActualW = ActualWidth > 0 ? ActualWidth : fallbackW;
            double newActualH = ActualHeight > 0 ? ActualHeight : fallbackH;

            var (clampedLeft, clampedTop) = ConfigService.EnsureVisibleBounds(
                Left,
                Top,
                newActualW,
                newActualH,
                monitors,
                targetDeviceName);

            Left = clampedLeft;
            Top = clampedTop;
        }

        _windowManager.UpdateZOrder();

        if (saveConfig)
        {
            SaveCurrentState();
        }

        if (_settingsWindow != null && _settingsWindow.IsLoaded)
        {
            _settingsWindow.SyncSettings();
        }
    }

    private void UpdateAlwaysOnTopCheckmark(bool isChecked)
    {
        if (MenuAlwaysOnTop != null)
        {
            MenuAlwaysOnTop.IsChecked = isChecked;
        }
    }

    private void UpdateViewModeCheckmarks(string currentMode)
    {
        bool isNormal = currentMode.Equals("Normal", StringComparison.OrdinalIgnoreCase);
        bool isMedium = currentMode.Equals("Orta", StringComparison.OrdinalIgnoreCase);
        bool isMediumMini = currentMode.Equals("Orta-Mini", StringComparison.OrdinalIgnoreCase);
        bool isMini = currentMode.Equals("Mini", StringComparison.OrdinalIgnoreCase);

        if (MenuViewNormal != null) MenuViewNormal.IsChecked = isNormal;
        if (MenuViewMedium != null) MenuViewMedium.IsChecked = isMedium;
        if (MenuViewMediumMini != null) MenuViewMediumMini.IsChecked = isMediumMini;
        if (MenuViewMini != null) MenuViewMini.IsChecked = isMini;
    }

    private void SaveCurrentState()
    {
        IReadOnlyList<MonitorDescriptor> monitors = GetCurrentMonitors();

        double fallbackW = _currentViewMode switch
        {
            "Mini" => MiniWidth,
            "Orta-Mini" => MediumMiniWidth,
            "Orta" => MediumWidth,
            _ => NormalWidth
        };
        double fallbackH = _currentViewMode switch
        {
            "Mini" => 80,
            "Orta-Mini" => 140,
            "Orta" => 170,
            _ => 450
        };

        double actualW = ActualWidth > 0 ? ActualWidth : fallbackW;
        double actualH = ActualHeight > 0 ? ActualHeight : fallbackH;

        Rect currentRect = new(Left, Top, actualW, actualH);
        // Pencere birden fazla monitöre taşıyorsa, pencerenin en büyük bölümünü içeren monitörü kaydet
        MonitorDescriptor? dominantMonitor = ScreenHelper.FindDominantMonitor(currentRect, monitors);

        var config = new AppConfig
        {
            ConfigVersion = 3,
            WindowLeft = Left,
            WindowTop = Top,
            MonitorDeviceName = dominantMonitor?.DeviceName,
            Opacity = Opacity,
            IsAlwaysOnTop = _isAlwaysOnTop,
            ViewMode = _currentViewMode
        };

        _configService.SaveConfig(config);
    }

    private void PopulateMonitorMenu()
    {
        if (MenuMoveToMonitor == null) return;
        MenuMoveToMonitor.Items.Clear();

        IReadOnlyList<MonitorDescriptor> monitors = GetCurrentMonitors();

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

    public void MoveToMonitor(MonitorDescriptor targetMon)
    {
        if (targetMon == null) return;

        // Hedef monitörün çalışma alanının sağ üst veya uygun bir yerine yerleştir
        double newLeft = targetMon.WorkAreaDip.Left + 40;
        double newTop = targetMon.WorkAreaDip.Top + 40;

        Left = newLeft;
        Top = newTop;

        _windowManager.UpdateZOrder();
        SaveCurrentState();

        if (_settingsWindow != null && _settingsWindow.IsLoaded)
        {
            _settingsWindow.SyncSettings();
        }
    }

    public void SetOpacity(double opacity, bool saveConfig = true)
    {
        Opacity = Math.Clamp(opacity, 0.2, 1.0);
        UpdateOpacityCheckmarks(Opacity);
        if (saveConfig)
        {
            SaveCurrentState();
        }
        if (_settingsWindow != null && _settingsWindow.IsLoaded)
        {
            _settingsWindow.SyncSettings();
        }
    }

    public void ResetPosition()
    {
        IReadOnlyList<MonitorDescriptor> monitors = GetCurrentMonitors();
        if (monitors.Count > 0)
        {
            MonitorDescriptor primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
            double fallbackW = _currentViewMode switch
            {
                "Mini" => MiniWidth,
                "Orta-Mini" => MediumMiniWidth,
                "Orta" => MediumWidth,
                _ => NormalWidth
            };
            double fallbackH = _currentViewMode switch
            {
                "Mini" => 80,
                "Orta-Mini" => 140,
                "Orta" => 170,
                _ => 450
            };
            double width = ActualWidth > 0 ? ActualWidth : fallbackW;
            double height = ActualHeight > 0 ? ActualHeight : fallbackH;

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

        if (_settingsWindow != null && _settingsWindow.IsLoaded)
        {
            _settingsWindow.SyncSettings();
        }
    }

    private void UpdateOpacityCheckmarks(double opacity)
    {
        if (MenuOpacity100 != null) MenuOpacity100.IsChecked = Math.Abs(opacity - 1.0) < 0.05;
        if (MenuOpacity80 != null) MenuOpacity80.IsChecked = Math.Abs(opacity - 0.8) < 0.05;
        if (MenuOpacity60 != null) MenuOpacity60.IsChecked = Math.Abs(opacity - 0.6) < 0.05;
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

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        OpenOrActivateSettingsWindow();
    }

    private void BtnHide_Click(object sender, RoutedEventArgs e)
    {
        HideToTray();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        HideToTray();
    }

    private void MenuRefresh_Click(object sender, RoutedEventArgs e)
    {
        _ = _viewModel.TriggerManualRefreshAsync();
    }

    private void MenuSettings_Click(object sender, RoutedEventArgs e)
    {
        OpenOrActivateSettingsWindow();
    }

    private void MenuHide_Click(object sender, RoutedEventArgs e)
    {
        HideToTray();
    }

    public void OpenOrActivateSettingsWindow()
    {
        if (_settingsWindow != null && _settingsWindow.IsLoaded)
        {
            _settingsWindow.SyncSettings();
            if (_settingsWindow.WindowState == WindowState.Minimized)
            {
                _settingsWindow.WindowState = WindowState.Normal;
            }
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(this);
        _settingsWindow.Closed += (s, e) =>
        {
            _settingsWindow = null;
        };
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void CloseSettingsWindow()
    {
        if (_settingsWindow != null)
        {
            try
            {
                _settingsWindow.Close();
            }
            catch
            {
                // Zaten kapalı veya kapanma sürecinde olan pencere için güvenli yoksayma
            }
            _settingsWindow = null;
        }
    }

    public bool HideToTray()
    {
        // 1. Tepsi simgesi henüz hazır değilse kurtarma dene
        if (!IsTrayAvailable)
        {
            _trayIconManager?.TryEnsureIconCreated();
        }

        // 2. Halen kullanılamıyorsa pencereyi erişilemez yapmamak için gizleme
        if (!IsTrayAvailable)
        {
            TrayUnavailableNotifier?.Invoke("Bildirim alanı (System Tray) simgesi kullanılamadığı için pencere gizlenemiyor. Pencere erişilebilir kalması için açık tutuldu.");
            return false;
        }

        CloseSettingsWindow();
        SaveCurrentState();
        _windowManager.HideToTray();
        Visibility = Visibility.Hidden;
        return true;
    }

    public void ShowFromTray()
    {
        Visibility = Visibility.Visible;
        _windowManager.ShowFromTray();
    }

    public void ExitApplication()
    {
        _isRealExit = true;
        CloseSettingsWindow();
        SaveCurrentState();
        Close();
    }

    private void MenuAlwaysOnTop_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
        {
            bool requested = menuItem.IsChecked;
            bool success = SetAlwaysOnTop(requested, saveConfig: true);
            if (!success)
            {
                menuItem.IsChecked = _isAlwaysOnTop;
            }
        }
    }

    private void MenuViewNormal_Click(object sender, RoutedEventArgs e)
    {
        ApplyViewMode("Normal", saveConfig: true);
    }

    private void MenuViewMedium_Click(object sender, RoutedEventArgs e)
    {
        ApplyViewMode("Orta", saveConfig: true);
    }

    private void MenuViewMediumMini_Click(object sender, RoutedEventArgs e)
    {
        ApplyViewMode("Orta-Mini", saveConfig: true);
    }

    private void MenuViewMini_Click(object sender, RoutedEventArgs e)
    {
        ApplyViewMode("Mini", saveConfig: true);
    }

    private void MenuResetPosition_Click(object sender, RoutedEventArgs e)
    {
        ResetPosition();
    }

    private void MenuOpacity_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && item.Tag is string tagStr && double.TryParse(tagStr, System.Globalization.CultureInfo.InvariantCulture, out double op))
        {
            SetOpacity(op, saveConfig: true);
        }
    }

    private void MenuExit_Click(object sender, RoutedEventArgs e)
    {
        ExitApplication();
    }

    #endregion
}
