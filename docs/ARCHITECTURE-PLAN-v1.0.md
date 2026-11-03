# UsageGauge v1.0 — Windows Widget Teknik Mimari Planı

Bu belge; [UX-DEFINITION-v1.0.md](UX-DEFINITION-v1.0.md) belgesindeki gereksinimleri ve T00–T00.8a fizibilite araştırmalarında elde edilen somut bulguları temel alarak **UsageGauge** Windows masaüstü widget'ının teknik mimarisini, veri erişim katmanını, pencere yönetimi stratejisini ve uygulama sırasını tanımlar.

---

## 1. Windows UI Teknolojisi Değerlendirmesi ve Seçimi

### 1.1 Aday Teknolojilerin Karşılaştırması

Aday Windows UI teknolojileri; geliştirme karmaşıklığı, düşük seviyeli Win32 pencere mesaj erişimi, dağıtım modeli ve sistemdeki mevcut araçlar çerçevesinde değerlendirilmiştir:

| Kriter | WPF (.NET 10) | WinUI 3 (Windows App SDK) | WinForms (.NET 10) | Webview2 / Tauri / Electron |
| :--- | :--- | :--- | :--- | :--- |
| **Win32 Pencere & Mesaj Erişimi** | **Doğrudan & Yalın** (`HwndSource`, `WindowInteropHelper`, yerleşik `AddHook` mesaj filtresi). | **Mümkün / Ek Katmanlı** (`WindowNative.GetWindowHandle` ve `AppWindow` ile HWND mevcuttur; ancak alt katman mesaj kancaları ek Win32 alt sınıflama gerektirir). | **Doğrudan** (`Control.Handle` ve `WndProc` ezme). | **Çok Kısıtlı** (Web render motoru arkasında izole; işletim sistemi pencere kancaları zordur). |
| **Uygulama & Düzen Karmaşıklığı** | **Düşük-Orta** (Modern XAML, veri bağlama, donanım ivmeli vektör çizim). | **Orta-Yüksek** (Modern kontroller; ancak Windows App SDK çalışma zamanı bağımlılığı ve paketleme katmanı yükü). | **Düşük Başlangıç / Yüksek Bakım** (GDI/GDI+ piksel tabanlı; DPI ve karanlık tema bakımı zordur). | **Orta** (HTML/CSS esnek; ancak çoklu süreç ve IPC mimarisi karmaşıktır). |
| **Sistemde Kurulu SDK / Araç** | **Hazır** (Sistemde `.NET SDK 10.0.402` mevcuttur). | **Eksik** (Windows App SDK çalışma zamanı ve ek iş yükü gerektirir). | **Hazır** (Sistemde `.NET SDK 10.0.402` mevcuttur). | **Eksik** (Sistemde Node.js kurulu değildir). |
| **Dağıtım Bağımsızlığı** | Windows Desktop SDK ile doğrudan derleme; standart dağıtım klasörü veya tek dosya yayını. | WinAppSDK bağımlılık paketleri (MSIX veya bootstrapper) gerektirir. | Windows Desktop SDK ile doğrudan derleme. | Webview2 çalışma zamanı veya Chromium paketleri gerektirir. |

### 1.2 Mimari Karar: WPF (.NET 10 Desktop SDK)
**Seçilen Teknoloji:** **WPF (.NET 10)**
- **Gerekçe:**
  1. *Yalın Win32 Interop Modeli:* Masaüstü widget'ının normal pencerelerin arkasında kalması, sistem odağını çalmaması (`WS_EX_NOACTIVATE`) ve Z-order senkronizasyonu için gereken Win32 pencere mesaj döngüsü (`WndProc`) kancaları WPF'te `HwndSource.AddHook` ile ek çalışma zamanı katmanı olmadan en sade biçimde kurulur. WinUI 3'te de `WindowNative` üzerinden HWND erişimi mümkün olmakla birlikte, bu proje kapsamında ek Windows App SDK bağımlılıkları getirmeden ilerlemek daha sade bir mimari sağlamaktadır.
  2. *Düşük Uygulama Karmaşıklığı:* XAML tabanlı vektör grafikler, karanlık tema, ilerleme çubukları ve veri bağlama (data-binding) mimarisi ek bir web çalışma zamanı veya harici paket yükü olmadan temiz biçimde uygulanır.
  3. *Mevcut SDK Uyumu:* Makinede `.NET SDK 10.0.402` kurulu olduğundan ek araç zinciri veya C++ derleyicisi kurmaya gerek kalmadan doğrudan derlenebilir.
- *Ayrım Notu:* Sistemde `.NET 10 SDK` bulunması kanıtlanmış bir olgudur; ancak uygulamanın derlendikten sonraki çalışma zamanı bellek (RAM) ve işlemci (CPU) tüketimi T03 aşamasında Windows cihazında profil çıkarılarak doğrulanacaktır.

---

## 2. Masaüstü Pencere Davranışı ve Z-Order Stratejisi

Widget'ın temel UX beklentisi: normal uygulama pencerelerinin arkasında masaüstünde sakin durması, `Win + D` ile gizlenmemesi, fareyle sürüklenebilmesi ve sistem odağını çalmamasıdır.

### 2.1 Olası Windows Mekanizmaları Analizi

1. **Yaklaşım 1: Genişletilmiş Pencere Stilleri ve Dinamik Z-Order (`WS_EX_TOOLWINDOW` + `WS_EX_NOACTIVATE` + `HWND_BOTTOM`)**
   - *Resmî / Belgelenmiş Kısım:* `WS_EX_TOOLWINDOW` pencereyi Alt+Tab ve görev çubuğu listesinden çıkarır; `WS_EX_NOACTIVATE` tıklandığında aktif pencere odağını widget'a geçirmez; `SetWindowPos(..., HWND_BOTTOM, ...)` pencereyi z-order'ın en altına konumlandırır.
   - *Henüz Cihazda Kanıtlanmamış Kısım:* Kullanıcı masaüstündeki bir simgeye tıkladığında veya pencereler minimize edildiğinde widget'ın masaüstü arka planıyla Z-order senkronizasyonu.
2. **Yaklaşım 2: Explorer Masaüstü Kabuğu Ebeveynliği (WorkerW / Progman Parenting)**
   - *Mekanizma:* Windows masaüstü duvar kağıdını barındıran `WorkerW` / `Progman` pencere tanıtıcısını bulup `SetParent(widgetHwnd, workerwHwnd)` çağrısı yapmak.
   - *Durum:* Bu yöntem **belgelenmemiş (undocumented)** bir kabuk davranışıdır. Windows 11 güncellemelerinde pencere hiyerarşisi değişebilir; fare olaylarının iletilmesinde kararsızlıklara yol açabilir. Bu nedenle doğrudan nihai çözüm sayılmayacaktır.

### 2.2 İlk Kod Görevi: T03 Davranış Spike'ı ve Doğrulama Ölçütleri
Teknik riski en başta elemek için ilk kod geliştirme adımı (T03) veri sağlayıcıları veya ayar kalıcılığı içermeyen, yalnızca pencere davranışını sınayan küçük ve geri alınabilir bir spike olacaktır.

- **T03 Dosya Kapsamı (Derlenebilir Minimal Proje):**
  - `src/UsageGauge/UsageGauge.csproj` (Target: `net10.0-windows`, `UseWPF: true`)
  - `src/UsageGauge/App.xaml` & `src/UsageGauge/App.xaml.cs`
  - `src/UsageGauge/MainWindow.xaml` & `src/UsageGauge/MainWindow.xaml.cs`
  - `src/UsageGauge/Native/NativeMethods.cs` (P/Invoke: `SetWindowPos`, `SetWindowLong`, `GetWindowLong`, `WS_EX_*`, `HWND_*`)
  - `src/UsageGauge/Native/DesktopWindowManager.cs` (Z-order kancası ve stil yönetimi)
- **Derleme Kanıtı:** `dotnet build src/UsageGauge/UsageGauge.csproj` komutu 0 hata ve 0 uyarı ile derlenmelidir.
- **Windows Cihazında Yapılacak Manuel Testler:**
  1. *Normal Pencere Arkasında Kalma:* Widget'ın üzerine büyük bir tarayıcı/IDE penceresi açılır; widget arkada kalmalı, pencerelerin önüne fırlamamalıdır.
  2. *Masaüstünü Göster (`Win + D`):* `Win + D` tuşlandığında widget masaüstünde görünür kalmalı; masaüstüyle birlikte simge durumuna küçülmemelidir.
  3. *Tıklama ve Sürükleme (Odak Çalmama):* Widget başlığından tutulup taşınırken veya tıklandığında aktif metin düzenleyicinin odağı bozulmamalıdır.
  4. *İki Monitör Arasında Taşıma:* Widget canlı olarak Monitör 1'den Monitör 2'ye sürüklenebilmeli ve her iki ekranda da masaüstü katmanında kalabilmelidir. *(Not: Kapatıp açınca konumun geri yüklenmesi testi, kalıcılık mantığının uygulanacağı T05 aşamasına bırakılmıştır).*
- **Durma / BLOCKED Koşulu:**
  - Widget `Win + D` sonrası masaüstünde kaybolup geri gelmiyorsa veya normal pencerelerin önüne geçiyorsa, alternatif pencere stili ve kanca mekanizması doğrulanmadan veri sağlayıcı aşamasına (T04) geçilmez.

---

## 3. ChatGPT Veri Kaynağı (Codex App Server Entegrasyonu)

T00.7 aşamasında yerel cihazda doğrulanan resmî `codex app-server` JSON-RPC 2.0 stdio arayüzü kullanılacaktır:

```
[UsageGauge Widget]
        |
        | (stdio JSON-RPC 2.0)
        v
[codex app-server --stdio]  <--->  [OpenAI Yerel Oturum: ~/.codex]
```

### 3.1 İletişim Akışı, Çıkış ve Zaman Aşımları
1. `codex app-server --stdio` alt süreci arka planda başlatılır. Dış süreç zaman aşımı **15 saniye** olarak yapılandırılır.
2. **El Sıkışma (Initialize):**
   ```json
   {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"clientInfo":{"name":"usagegauge","title":"UsageGauge","version":"0.1.0"},"capabilities":{"experimentalApi":true}}}
   ```
3. Sunucudan yanıt alındıktan hemen sonra bildirim gönderilir:
   ```json
   {"jsonrpc":"2.0","method":"initialized"}
   ```
4. **Kota Okuma (Read Rate Limits):**
   ```json
   {"jsonrpc":"2.0","id":2,"method":"account/rateLimits/read","params":{}}
   ```
5. **Düzgün Süreç Kapanışı (Graceful Exit):**
   - Kota yanıtı eksiksiz alındıktan sonra istemci `stdin` akışını kapatır (EOF).
   - `proc.WaitForExitAsync(cancellationToken)` veya kısa bir süre (`2000 ms`) sürecin kendiliğinden temiz biçimde sonlanması beklenir.
   - Süreç ağacını zorla sonlandırma (`Kill(entireProcessTree: true)`), **yalnızca 15 saniyelik zaman aşımı durumunda veya sürecin stdin kapanışına rağmen takılı kalması halinde yedek güvenlik önlemi** olarak kullanılır.

### 3.2 Yanıt Eşleştirme Kuralları
- Yanıt `result.rateLimitsByLimitId.codex` nesnesi üzerinden okunur:
  - **5 Saatlik Limit:** `windowDurationMins == 300` olan pencere.
  - **Haftalık Limit:** `windowDurationMins == 10080` olan pencere.
- **Değer Dönüşümü:**
  - Kalan Yüzde: `100.0 - usedPercent`
  - Sıfırlanma: `resetsAt` (Unix epoch saniyesi) → `DateTimeOffset.FromUnixTimeSeconds(resetsAt)`
- **Güvenlik Sınırı:** Hiçbir model çağrısı (`thread/start`, `model/prompt`) yapılmaz; 0 token harcanır.

---

## 4. Antigravity Veri Kaynağı (CLI `agy` Entegrasyonu)

T00.8a aşamasında doğrulanmış olan başsız CLI komutu çalıştırılacaktır:

```
[UsageGauge Widget]  --->  exec("agy -p /usage --output-format json --print-timeout 10s")  --->  JSON Çıktısı
```

### 4.1 Ayrıştırma ve Zaman Aşımı Uyumu
- **Zaman Aşımı Kademelendirmesi:** `agy` komutuna iç zaman aşımı olarak `--print-timeout 10s` verilir. Widget'ın dış süreç çalıştırma zaman aşımı ise **15 saniye** olarak ayarlanır. Böylece CLI'nın kendi zaman aşımı mekanizması tetiklenmeden önce dış sürecin erkenden zorla öldürülmesi (race condition) engellenir.
- **Bucket ID Eşleştirmesi:** Dizi indekslerine **asla güvenilmez**; `command.data.groups` nesnesi içinde grup ve bucket ID'leri taranır:
  1. `Gemini Models` Grubu:
     - `id == "gemini-5h"` → 5 Saatlik Havuz
     - `id == "gemini-weekly"` → Haftalık Havuz
  2. `Claude and GPT models` Grubu:
     - `id == "3p-5h"` → 5 Saatlik Havuz
     - `id == "3p-weekly"` → Haftalık Havuz
     *(T05 Nihai Ürün Kapsamı Notu: Antigravity sağlayıcısı ve veri modeli bu grubu okuma yeteneğini korur; ancak T05 nihai ürün kararıyla widget UI katmanında yalnızca Gemini grubu listelenmektedir).*
- **Alan Doğrulama:**
  - Kalan Yüzde: `remaining_fraction` (0.0 – 1.0 aralığı) doğrulanır; `remaining_fraction * 100` ile yüzde hesaplanır.
  - Sıfırlanma: `reset_time` (ISO 8601 UTC dizgisi) → `DateTimeOffset.Parse(reset_time)`.
- **Kararlılık ve Savunmacı Ayrıştırma:**
  - `command.data.groups` iç bir yapıdır; resmî olarak dondurulmuş bir genel API garantisi taşımaz.
  - CLI çıktısı beklenenden farklı gelirse veya ID'ler bulunamazsa widget uydurma değer üretmez; ilgili grubu `Error` / `Stale` durumuna geçirir.

---

## 5. Veri Akışı, Eşzamansızlık ve Yaşam Döngüsü

### 5.1 Kaynakların Yalıtımı (Decoupling)
- ChatGPT ve Antigravity sağlayıcıları tamamen bağımsız iki asenkron görev olarak çalışır.
- Bir servisin başarısız olması diğer servisin çalışmasını veya arayüzün güncellenmesini engellemez.
- Her iki sağlayıcının kendine ait `LastSuccessTime`, `IsLoading` ve `ErrorMessage` alanları bulunur.

### 5.2 Yenileme Politikası ve Kesinleşmiş Hata Takvimi
1. **Normal Periyot:** 10 dakika.
2. **Hata Durumunda Geri Çekilme (Backoff Takvimi):**
   - Otomatik hata tekrarları kesinlikle normal sorgu aralığından (10 dk) daha sık yapılmaz.
   - Ardışık başarısızlıklarda bekleme süresi şu kesin takvime göre artırılır:
     - *1. Hata:* **10 dakika** (normal aralık korunur).
     - *2. Ardışık Hata:* **15 dakika**.
     - *3. Ardışık Hata:* **20 dakika**.
     - *4. ve Sonraki Ardışık Hatalar:* **30 dakika** (sabit üst sınır).
   - Servis tekrar başarılı veri döndürdüğünde bekleme aralığı derhal 10 dakikaya sıfırlanır.
3. **Elle Yenileme (`[⟳]`):**
   - Eşzamanlı sorgu koruması: Halihazırda devam eden bir sorgu varsa (`IsLoading == true`), mükerrer tıklamalar yoksayılır.
   - Başarılı elle yenileme sonrasında bir sonraki otomatik zamanlayıcı sayacı sıfırlanır.

### 5.3 Canlı Geri Sayım, UTC Zaman ve Sıfırlanma Davranışı
- **Zaman Hesabı:** Tüm geri sayım farkları `DateTimeOffset.UtcNow` ile hedef sıfırlanma zamanı (`TargetResetTimeUtc`) arasındaki mutlak fark üzerinden hesaplanır:  
  `TimeSpan kalan = TargetResetTimeUtc - DateTimeOffset.UtcNow;`
- **Yüzde Değişmezliği İlkesi:** Yüzde değeri son başarılı okuma anındaki değerde sabit kalır; aradaki dakikalarda tahmini olarak eksiltilmez.
- **Sıfırlanma Zamanı Dolduğunda Davranış:**  
  `kalan <= TimeSpan.Zero` olduğunda:
  - Eski yüzde değeri **asla otomatik olarak %100 yapılmaz**.
  - Arayüzde sayaç yerine `"Sıfırlanma zamanı doldu — Yeni kota doğrulanıyor..."` durum rozeti gösterilir.
  - Arka planda sağlayıcıya taze veri sorgusu iletilir. Yalnızca sağlayıcıdan taze yanıt alındığında yeni yüzde ekrana yansıtılır.

---

## 6. Yerel Uygulama Mimarisi ve Dış Bağımlılık Yönetimi

### 6.1 Dağıtım Modeli ve Dış CLI Araçlarının Ayrımı
- **Uygulama Dağıtım Biçimleri:**
  - *Standart Dağıtım Klasörü:* `dotnet publish -c Release` ile üretilen, ana `.exe` ve ilişkili bağımlılık DLL'lerini içeren uygulama dizini.
  - *Tek Dosya Yayını (İsteğe Bağlı):* Proje dosyasında `-p:PublishSingleFile=true` yapılandırılarak yönetilen tüm derlemelerin tek bir `.exe` dosyasında paketlenmesi.
  - *Çalışma Zamanı İlişkisi (`--self-contained`):* Sistemde `.NET 10` kurulu olduğu için uygulama varsayılan olarak çerçeveye bağımlı (framework-dependent) çalışır. İstenirse `--self-contained` bayrağıyla .NET çalışma zamanı ikiliye dahil edilebilir; bu durum dış araçların paketlenmesi anlamına gelmez.
- **Harici CLI Araçları (`codex.exe`, `agy.exe`):**
  - Bu araçlar widget ikilisinin içine gömülmez; sistemde bağımsız olarak kurulu bulunmaları gerekir.
  - Kurulumun otomatik yapıldığı varsayılmaz. Widget açılışta `codex` ve `agy` araçlarının varlığını denetler; araçlar bulunamazsa widget çökmez.
  - İlgili servis kartında `"Bağımlılık Bulunamadı"` uyarısı ve kurulum yönergesi (`winget install OpenAI.Codex` veya `irm https://antigravity.google/cli/install.ps1 | iex`) gösterilir.
  - Oturum açılmamışsa `"Oturum Gerekli: codex login yapın"` durumu bildirilir.

### 6.2 Konum Kalıcılığı ve Çoklu Monitör Güvenliği
- Konum ayarları kullanıcının yerel profilinde `%APPDATA%\UsageGauge\config.json` dosyasında saklanır (`windowLeft`, `windowTop`, `targetMonitorDevice`, `opacity`).
- **Güvenli Sınır Denetimi:** Açılışta kaydedilen koordinatlar `System.Windows.Forms.Screen.AllScreens` çalışma alanlarıyla karşılaştırılır. Monitör sırası, çözünürlük veya DPI ölçeği değiştiğinde koordinatlar ekran dışına taşarsa, widget otomatik olarak birincil ekranın güvenli çalışma alanına (sağ alt köşe) çekilir.

### 6.3 Güvenlik ve Süreç İzolasyonu
- **Kimlik Bilgisi Yalıtımı:** Widget hiçbir zaman kullanıcı parolası, ChatGPT oturum çerezi veya API anahtarı istemez, okumaz ve diske kaydetmez.
- **Güvenli Loglama:** Hata kayıtlarında yalnızca süreç çıkış kodları ve genel hata mesajları tutulur; ham payload'lar veya kullanıcı e-postaları günlüklere yazılmaz.

---

## 7. Uygulama Sırası ve Görev Yol Haritası

| Görev | Başlık ve Kapsam | Dosya Kapsamı | Otomatik Doğrulama | Manuel Doğrulama | Durma / BLOCKED Koşulu |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **T03** | **Masaüstü Katmanı & Pencere Spike'ı** | `src/UsageGauge/UsageGauge.csproj`, `src/UsageGauge/App.*`, `src/UsageGauge/MainWindow.*`, `src/UsageGauge/Native/*` | `dotnet build` 0 hata/uyarı | Normal pencerelerin arkasında kalma, `Win+D`, tıklama/sürükleme ve 2 monitör arasında taşıma testi | `Win+D` ile masaüstünde görünür kalamazsa veya normal pencerelerin önüne fırlarsa dur. |
| **T04** | **Arka Uç Veri Sağlayıcıları** | `src/UsageGauge/Providers/*` (`ChatGptProvider`, `AntigravityProvider`) | Mock stdio/JSON ayrıştırma birim testleri (6 kota penceresi doğrulaması) | Canlı `codex app-server` ve `agy` çağrısı ile gerçek kotaları okuma | Herhangi bir sağlayıcı token harcaması/model turu tetiklerse dur. |
| **T05** | **UI Birleştirme, Durum Yönetimi & Konum Kalıcılığı** | `src/UsageGauge/ViewModels/*`, `src/UsageGauge/Views/*`, `src/UsageGauge/Services/ConfigService.*` | Timer, durum geçişi ve ayar kaydetme/yükleme testleri | Kullanım ekranları kapalıyken 2 kartta 4 kotanın (ChatGPT 5h/haftalık, Antigravity Gemini 5h/haftalık) izlenmesi; kapatıp açınca konumun korunması (T05 nihai kararıyla 3. kart arayüzden çıkarılmıştır) | UX kabul ölçütlerinden biri ([AC-01 - AC-06](UX-DEFINITION-v1.0.md#8-kabul-ölçütleri-acceptance-criteria)) karşılanamazsa dur. |
| **T06** | **Üretim Sürümü, Taşınabilir Paketleme & Teslimat** | `scripts/publish.ps1`, `docs/DEPLOYMENT-GUIDE.md`, `src/UsageGauge/*`, `tests/*` | `dotnet build -c Release`, 77 birim/entegrasyon testi, temiz dizinde açılış/kapanış doğrulaması | Taşınabilir `.zip` paketinin temiz Windows ortamında çift tıklanarak çalışması, 2 kart/4 kota görünümü, `config.json` bozulmaması | Self-contained paket çalışmazsa, CLI yokluğunda çökerse veya loglara hassas veri sızarsa dur. |
| **T07** | **Cihaz Kabul Doğrulaması** | `src/UsageGauge/Native/*`, `src/UsageGauge/MainWindow.*`, `tests/*`, `docs/T07-DEVICE-ACCEPTANCE.md` | `dotnet test` (84 test), `WM_DISPLAYCHANGE` kancası, yaklaşık 4 saat 40 dk sanal saat simülasyonu (PASS) | AC-02 ekran testi kullanıcı bildirimiyle doğrulandı (PASS); AC-04 uzun süreli fiziksel cihaz gözlemi beklemede (PARTIAL) | Ekran değişimi widget'ı kaybettirirse veya saatler içinde yapay yüzde değişimi olursa dur. |

---

## 8. Teknik Kararlar ve Güven Tablosu

| Mimari Alan / Karar | Sınıflandırma | Durum ve Dayanak |
| :--- | :---: | :--- |
| **.NET SDK 10.0.402 Varlığı** | **Kanıtlanmış** | Sistemde `C:\Program Files\dotnet\sdk\10.0.402` mevcuttur. |
| **Codex App Server Stdio JSON-RPC** | **Kanıtlanmış** | T00.7 raporunda ChatGPT Plus ile 300 ve 10080 dk kotaları 0 token ile okunmuştur. |
| **Antigravity CLI JSON Okuması** | **Kanıtlanmış** | T00.8a raporunda 4 kota alanı ve ISO 8601 UTC sıfırlanma zamanı 0 token ile okunmuştur. |
| **Ekranlar Kapalıyken Okuma** | **Kanıtlanmış** | Her iki sağlayıcının da arayüz açık olmadan başsız (headless) çalıştığı doğrulanmıştır. |
| **WPF (.NET 10) UI Tercihi** | **Teknik Öneri** | HWND mesaj kontrolü (`HwndSource`), düşük geliştirme karmaşıklığı ve mevcut SDK uyumu. |
| **Bağımsız Asenkron Sağlayıcılar** | **Teknik Öneri** | Servislerin birbirini kilitlememesi ve ayrı durum takibi için önerilmiştir. |
| **10 Dk Periyot & 15-20-30 Dk Geri Çekilme** | **Teknik Öneri** | Hata tekrarlarının normal aralıktan sık olmaması ve kaynak tüketimini önlemek için kesinleştirilmiştir. |
| **UTC DateTimeOffset Sayaç Hesabı** | **Teknik Öneri** | Yerel saat kaymalarından bağımsız güvenilir süre farkı için seçilmiştir. |
| **Graceful Exit (stdin EOF) Önceliği** | **Teknik Öneri** | Alt süreçlerin temiz kapanması; zorla sonlandırmanın yalnızca zaman aşımında yedek kalması. |
| **WPF Uygulama Kaynak Tüketimi (RAM/CPU)** | **Windows Cihazında Test Edilecek** | Derlenen uygulamanın boşta ve sayaç akarken tükettiği gerçek RAM/CPU T03'te ölçülecektir. |
| **Normal Pencerelerin Arkasında Kalma (Z-Order)** | **Windows Cihazında Test Edilecek** | Win32 `WS_EX_NOACTIVATE` ve alt katman konumlandırması T03 kod spike'ında gerçek pencerelerle sınanacaktır. |
| **Win + D ile Görünür Kalma** | **Windows Cihazında Test Edilecek** | Windows 11 kabuk davranışında widget'ın masaüstü düzeyinde görünür kalması T03'te test edilecektir. |

---

## 9. Resmî Kaynaklar ve Belgeler

- **OpenAI Codex:**
  - [OpenAI Codex CLI Documentation](https://developers.openai.com/codex/cli)
  - [OpenAI Codex App Server Protocol](https://developers.openai.com/codex/app-server)
  - [WinGet Package: OpenAI.Codex](https://github.com/microsoft/winget-pkgs/tree/master/manifests/o/OpenAI/Codex)
- **Google Antigravity:**
  - [Google Antigravity Documentation Home](https://antigravity.google/docs)
  - [Google Antigravity CLI Headless Mode](https://antigravity.google/docs/cli/headless/)
  - [Google Antigravity CLI /usage Command Reference](https://antigravity.google/docs/cli/commands/usage/)
- **Microsoft Win32 & WPF:**
  - [Microsoft WPF Documentation](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/)
  - [SetWindowPos Function (Win32 API)](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos)
  - [Extended Window Styles (WS_EX_*)](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles)
  - [HwndSource Class (.NET / WPF)](https://learn.microsoft.com/en-us/dotnet/api/system.windows.interop.hwndsource)
  - [DateTimeOffset Structure (.NET)](https://learn.microsoft.com/en-us/dotnet/api/system.datetimeoffset)
