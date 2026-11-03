# UsageGauge — WPF Masaüstü Uygulaması (Kaynak Kılavuzu)

Bu dizin, UsageGauge masaüstü widget'ının .NET 10 WPF kaynak kodlarını, Win32 yerel kancalarını (hooks), arka uç servislerini, veri sağlayıcılarını ve MVVM bileşenlerini barındırır.

---

## 1. Dizin ve Bileşen Yapısı

```
src/UsageGauge/
├── App.xaml / App.xaml.cs                 # Uygulama yaşam döngüsü ve kaynaklar
├── MainWindow.xaml / MainWindow.xaml.cs   # Ana widget penceresi, sağ tık menüsü ve DPI koordinat yönetimi
├── Models/
│   ├── AntigravityQuotaResult.cs          # Antigravity kota ayrıştırma ve sağlayıcı sonuç modeli
│   ├── ChatGptQuotaResult.cs              # ChatGPT / Codex kota ayrıştırma ve sağlayıcı sonuç modeli
│   ├── OverallUsageSnapshot.cs            # Birleşik kullanım enstantanesi
│   ├── ProviderStatus.cs                  # Sağlayıcı durum enum'ı (Success, AuthenticationRequired, CliNotFound vb.)
│   ├── QuotaGroup.cs                      # Kota grubu veri modeli
│   └── QuotaWindow.cs                     # 5 saatlik ve haftalık kota penceresi modeli
├── Native/
│   ├── DesktopDiagnostics.cs              # Teşhis günlüğü (usagegauge-diagnostics.log) ve imza filtreleme
│   ├── DesktopWindowManager.cs            # Dinamik Z-Order yönetimi, WinEvent kancası ve Win+D koruması
│   └── NativeMethods.cs                   # Win32 API P/Invoke tanımları (SetWindowPos, SetWinEventHook vb.)
├── Providers/
│   ├── AntigravityProvider.cs             # Google Antigravity CLI entegrasyonu (agy -p /usage ...)
│   ├── ChatGptProvider.cs                 # OpenAI Codex CLI stdio JSON-RPC entegrasyonu
│   ├── IAntigravityProvider.cs            # Antigravity sağlayıcı arayüzü
│   ├── IChatGptProvider.cs                # ChatGPT sağlayıcı arayüzü
│   ├── UsageCoordinator.cs                # Sağlayıcıları koordine eden üst katman
│   └── Process/
│       ├── CliPathResolver.cs             # CLI ikililerini (PATH / AppData / WinGet) tespit eden çözücü
│       ├── IProcessRunner.cs              # Süreç çalıştırma soyutlaması
│       ├── IStdioSession.cs               # İki yönlü stdio JSON-RPC oturum arayüzü
│       ├── ProcessRunResult.cs            # Süreç çalışma sonucu modeli
│       └── StandardProcessRunner.cs       # Standart Process başlatma ve 15s zaman aşımı yönetimi
├── Services/
│   ├── AppConfig.cs                       # Pencere konumu, monitör ve opaklık yapılandırma modeli
│   ├── ConfigService.cs                   # %APPDATA% üzerinde güvenli ve atomik yapılandırma yönetimi
│   ├── IConfigService.cs                  # Yapılandırma servisi arayüzü
│   ├── QuotaScheduler.cs                  # 10 dk periyot, üstel geri çekilme ve bağımsız yenileme
│   ├── QuotaTimeFormatter.cs              # Sayaç ve kalan süre metin biçimlendiricisi
│   └── ScreenHelper.cs                    # Çoklu monitör koordinatları, DPI ölçekleme ve çalışma alanı tespiti
└── ViewModels/
    ├── MainWindowViewModel.cs             # 2 kart / 4 kota penceresi görünüm mantığı, sayaçlar ve menü komutları
    ├── QuotaCardViewModel.cs              # Tekil sağlayıcı kartı (ChatGPT Work veya Antigravity Gemini)
    ├── QuotaWindowViewModel.cs            # Tekil kota penceresi göstergesi (yüzde, sayaç, ilerleme çubuğu)
    └── ViewModelBase.cs                   # INotifyPropertyChanged temel sınıfı
```

---

## 2. Üretim Widget'ı Mimarisi

Mevcut sürüm, **2 Havuz / 4 Kota Penceresi (2 Kart)** mimarisine sahiptir:
1. **ChatGPT (Work/Codex):** 5 Saatlik Pencere ve Haftalık Pencere.
2. **Antigravity (Gemini):** 5 Saatlik Pencere ve Haftalık Pencere.

> [!NOTE]
> **Tarihsel Not:** Erken T03 spike aşamasında kullanılan Z-order test butonları ("Ön Pencereyi Kilitle", geçici odak telemetrisi göstergeleri), T05 aşamasında kaldırılarak yerini odak çalmayan, şeffaf ve kompakt üretim widget'ına bırakmıştır.

---

## 3. Dinamik Z-Order ve Masaüstü Entegrasyonu Prensipleri (T03.3a Mirası)

UsageGauge, WispDock mimarisinden uyarlanan dinamik Z-Order ve WinEvent mekanizmasını kullanır. Kararlılık kuralları:

1. **Tek Seferlik 100 ms Masaüstü Doğrulaması:**
   - Masaüstüne geçiş anında planlanan 100 ms'lik doğrulama `isVerification: true` bayrağı ile çalışır.
   - Doğrulama çağrısı mükerrer planlama yapmaz; sonsuz döngü engellenir.
2. **Sıfır Ön Plan Penceresi (`GetForegroundWindow == 0`) Sınırı:**
   - Ekran kilidi, UAC veya geçiş anlarında gecikmeli tekrar en fazla **4 defa (toplam 200 ms)** ile sınırlandırılmıştır (`MaxZeroFgRetries = 4`).
3. **`SetWinEventHook` Başarısızlık Koruması:**
   - Kanca kurulumu başarısız olursa pencere asla `HWND_TOPMOST` katmanında takılı bırakılmaz; `HWND_NOTOPMOST` ve `HWND_BOTTOM` katmanına çekilir.
4. **`SetWindowPos` Hata Denetimi:**
   - Normal uygulamaya geçildiğinde `HWND_NOTOPMOST` çağrısı başarısız olursa kritik hata olarak günlüğe yazılır; sahte başarı üretilmez.
5. **Teşhis Günlüğü Süzgeci:**
   - Durum imzası (`Signature`) değişmediği sürece diske mükerrer log yazılmaz; teşhis günlüğü (`usagegauge-diagnostics.log`) kontrollü boyutta tutulur.

---

## 4. Geliştirme, Derleme ve Çalıştırma

### Geliştirici Ortamı Gereksinimleri
- Windows 10 (1903+) veya Windows 11 x64
- .NET 10 SDK

### Derleme:
```powershell
dotnet build src/UsageGauge/UsageGauge.csproj -c Debug
```

### Çalıştırma:
```powershell
dotnet run --project src/UsageGauge/UsageGauge.csproj
```

### Test Koşumu:
```powershell
dotnet test tests/UsageGauge.Tests/UsageGauge.Tests.csproj --filter "Category!=Live"
```
