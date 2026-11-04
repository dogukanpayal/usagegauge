using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UsageGauge.Native;
using UsageGauge.Services;

namespace UsageGauge;

/// <summary>
/// UsageGauge ayarlar penceresi.
/// Görünüm modu, Her Zaman Üstte, Opaklık, Monitöre Taşı ve Konum Sıfırlama ayarlarını tek merkezden yönetir.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly MainWindow _mainWindow;
    private bool _isSyncing;

    public SettingsWindow(MainWindow mainWindow)
    {
        InitializeComponent();
        _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));

        try
        {
            if (new System.Windows.Interop.WindowInteropHelper(mainWindow).Handle != IntPtr.Zero)
            {
                Owner = mainWindow;
            }
        }
        catch (InvalidOperationException)
        {
            // Test ortamında veya henüz handle oluşturulmamış ana pencerede Owner ataması güvenle atlanır
        }

        Loaded += (s, e) => SyncSettings();
    }

    /// <summary>
    /// Ana pencerenin güncel durumunu ayarlar penceresindeki kontrollere yansıtır.
    /// </summary>
    public void SyncSettings()
    {
        _isSyncing = true;
        try
        {
            // 1. Görünüm Düzeni
            string mode = _mainWindow.CurrentViewMode;
            RadioNormal.IsChecked = mode.Equals("Normal", StringComparison.OrdinalIgnoreCase);
            RadioMedium.IsChecked = mode.Equals("Orta", StringComparison.OrdinalIgnoreCase);
            RadioMediumMini.IsChecked = mode.Equals("Orta-Mini", StringComparison.OrdinalIgnoreCase);
            RadioMini.IsChecked = mode.Equals("Mini", StringComparison.OrdinalIgnoreCase);

            // 2. Her Zaman Üstte
            CheckAlwaysOnTop.IsChecked = _mainWindow.IsAlwaysOnTop;

            // 3. Opaklık
            double op = _mainWindow.Opacity;
            RadioOpacity100.IsChecked = Math.Abs(op - 1.0) < 0.05;
            RadioOpacity80.IsChecked = Math.Abs(op - 0.8) < 0.05;
            RadioOpacity60.IsChecked = Math.Abs(op - 0.6) < 0.05;

            // 4. Monitör Listesi
            IReadOnlyList<MonitorDescriptor> monitors = _mainWindow.GetCurrentMonitors();
            ComboMonitors.ItemsSource = monitors;
            if (monitors.Count > 0)
            {
                // Mevcut pencerenin en çok kesiştiği monitörü seçili yap
                Rect currentRect = new(_mainWindow.Left, _mainWindow.Top, _mainWindow.Width, _mainWindow.Height);
                MonitorDescriptor? dominant = ScreenHelper.FindDominantMonitor(currentRect, monitors);
                ComboMonitors.SelectedItem = dominant ?? monitors[0];
            }
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void RadioViewMode_Click(object sender, RoutedEventArgs e)
    {
        if (_isSyncing) return;

        if (ReferenceEquals(sender, RadioNormal))
        {
            _mainWindow.ApplyViewMode("Normal", saveConfig: true);
        }
        else if (ReferenceEquals(sender, RadioMedium))
        {
            _mainWindow.ApplyViewMode("Orta", saveConfig: true);
        }
        else if (ReferenceEquals(sender, RadioMediumMini))
        {
            _mainWindow.ApplyViewMode("Orta-Mini", saveConfig: true);
        }
        else if (ReferenceEquals(sender, RadioMini))
        {
            _mainWindow.ApplyViewMode("Mini", saveConfig: true);
        }
    }

    private void CheckAlwaysOnTop_Click(object sender, RoutedEventArgs e)
    {
        if (_isSyncing) return;

        bool requested = CheckAlwaysOnTop.IsChecked == true;
        bool success = _mainWindow.SetAlwaysOnTop(requested, saveConfig: true);
        if (!success)
        {
            CheckAlwaysOnTop.IsChecked = _mainWindow.IsAlwaysOnTop;
        }
    }

    private void RadioOpacity_Click(object sender, RoutedEventArgs e)
    {
        if (_isSyncing) return;

        if (sender is RadioButton rb && rb.Tag is string tagStr && double.TryParse(tagStr, CultureInfo.InvariantCulture, out double op))
        {
            _mainWindow.SetOpacity(op, saveConfig: true);
        }
    }

    private void BtnMoveToMonitor_Click(object sender, RoutedEventArgs e)
    {
        if (ComboMonitors.SelectedItem is MonitorDescriptor targetMon)
        {
            _mainWindow.MoveToMonitor(targetMon);
        }
    }

    private void BtnResetPosition_Click(object sender, RoutedEventArgs e)
    {
        _mainWindow.ResetPosition();
    }
}
