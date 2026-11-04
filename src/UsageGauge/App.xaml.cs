using System;
using System.Diagnostics;
using System.Windows;
using UsageGauge.Services;

namespace UsageGauge;

/// <summary>
/// UsageGauge uygulama yaşam döngüsü.
/// Tek örnek (Single-Instance) kontrolü ve ilk örneğin başlatılması burada yönetilir.
/// </summary>
public partial class App : Application
{
    private SingleInstanceService? _singleInstanceService;

    /// <summary>
    /// İkinci örnek mevcut örneğe ulaşamadığında hata mesajını gösteren delege.
    /// Test ortamında UI bloklanmasını önlemek için değiştirilebilir.
    /// </summary>
    public static Action<string, string>? SignalErrorNotifier { get; set; } = (message, caption) =>
    {
        // xUnit / testhost test ortamlarında modal mesaj kutusu açarak testi bloklama
        if (AppDomain.CurrentDomain.FriendlyName.Contains("testhost", StringComparison.OrdinalIgnoreCase) ||
            AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name?.StartsWith("xunit", StringComparison.OrdinalIgnoreCase) == true))
        {
            return;
        }

        MessageBox.Show(
            message,
            caption,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    };

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceService = new SingleInstanceService();
        if (!_singleInstanceService.IsFirstInstance)
        {
            // İkinci bir örnek başlatıldı: Mevcut ilk örneği bildirim alanından öne getirmesi için sınırlı tekrarla uyar
            bool signaled = SingleInstanceService.SignalExistingInstanceWithRetry(
                maxRetries: 3,
                retryDelayMs: 250,
                timeoutPerAttemptMs: 1000);

            _singleInstanceService.Dispose();

            if (!signaled)
            {
                // Sessiz başarıyla çıkma: Kullanıcıya anlaşılır hata göster ve başarısız çıkış kodu (1) ver
                SignalErrorNotifier?.Invoke(
                    "UsageGauge zaten arka planda çalışıyor, ancak mevcut pencereye sinyal iletilemedi.\n\nLütfen Windows bildirim alanındaki (System Tray) simgeyi kontrol edin veya Görev Yöneticisi'nden işlemi sonlandırıp tekrar deneyin.",
                    "UsageGauge - Çalışan Örnek Hatası");

                Shutdown(1);
                return;
            }

            // Başarılı sinyal iletildi: İkinci örnek pencere veya bildirim ikonu açmadan derhal sonlanır
            Shutdown(0);
            return;
        }

        var mainWindow = new MainWindow(_singleInstanceService);
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceService?.Dispose();
        _singleInstanceService = null;
        base.OnExit(e);
    }
}
