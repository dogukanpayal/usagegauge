# UsageGauge — T03.3a Dinamik Z-Order Döngüsü ve Teşhis Düzeltmeleri

Bu sürüm; T03.3'te WispDock mimarisinden uyarlanan dinamik Z-Order mekanizmasındaki döngü risklerini, hata denetimlerini ve teşhis günlüğü optimizasyonlarını içerir.

---

## 1. Uygulanan Düzeltmeler Özeti

1. **Tek Seferlik 100 ms Masaüstü Doğrulaması:**
   - Masaüstüne geçiş anında planlanan 100 ms'lik doğrulama artık `isVerification: true` bayrağı ile çalıştırılır.
   - Doğrulama çağrısının kendisi asla yeni bir doğrulama planlamaz.
   - Yalnızca kullanıcı normal bir uygulamadan masaüstüne yeni bir geçiş yaptığında tek seferlik planlanır; aynı masaüstü oturumunda mükerrer doğrulamalar birikmez ve sonsuz döngü engellenir.

2. **Sıfır Ön Plan Penceresi (`GetForegroundWindow == 0`) Döngü Sınırı:**
   - Ekran kilidi, UAC veya geçiş anlarında `GetForegroundWindow` sıfır döndüğünde 50 ms'lik gecikmeli tekrar en fazla **4 defa (toplam 200 ms)** ile sınırlandırılmıştır (`MaxZeroFgRetries = 4`).
   - Bu sınır aşıldığında döngü kesilir ve bir sonraki gerçek WinEvent olayı beklenir.

3. **`SetWinEventHook` Başarısızlık Koruması:**
   - Kanca kurulumu (`SetWinEventHook`) başarısız olursa (`_hookHandle == IntPtr.Zero`), `Initialize()` metodu doğrudan `false` döner.
   - Pencere asla `HWND_TOPMOST` katmanında takılı bırakılmaz; güvenli biçimde `HWND_NOTOPMOST` ve `HWND_BOTTOM` katmanına çekilir.
   - Arayüzde açık hata mesajı (`Win32 Kodu`) gösterilir.

4. **`SetWindowPos` Hata Denetimi ve Başarı Mesajı Tutarlılığı:**
   - `SetWindowPos` çağrıları başarısız olduğunda asla "masaüstünde görünür" veya "uygulamanın arkasında" şeklinde sahte başarı mesajı gösterilmez; Win32 hata kodu bildirilir.
   - Normal uygulamaya geçildiğinde `HWND_NOTOPMOST` çağrısı başarısız olursa kritik hata olarak ele alınır ve pencerenin en üstte takılı kalabileceği uyarısı verilir.

5. **Teşhis Günlüğü Optimizasyonu ve Mükerrer Kayıt Süzgeci:**
   - 100 ms'lik döngü sonlandırılarak dosyanın saniyeler içinde büyümesi engellendi.
   - `DesktopDiagnostics` katmanına durum imzası (`Signature`) süzgeci eklendi: durum değişmediği sürece dosya diskine mükerrer yazma yapılmaz.
   - Kalp atışı teşhisi 10 saniyelik kontrollü örnekleme aralığına çekildi.

---

## 2. Derleme ve Çalıştırma

### Derleme:
```powershell
dotnet build src/UsageGauge/UsageGauge.csproj
```
*(Hedef: 0 Warning, 0 Error)*

### Çalıştırma:
```powershell
dotnet run --project src/UsageGauge/UsageGauge.csproj
```

---

## 3. Windows 11 Ortamında 4 Manuel Test Rehberi

Pencere açıldığında canlı Z-Order katmanı ("Masaüstü" veya "Normal Uygulama") ve odak telemetrisi arayüzde görüntülenir:

### Test 1: Normal Pencere Arkasında Kalma (Önceden GEÇTİ ✓)
- **İşlem:** Kullanmakta olduğunuz web tarayıcısını (Chrome/Edge) veya IDE'yi (VS Code) UsageGauge penceresinin üzerine getirin.
- **Beklenen Davranış:** Z-Order durumu `Normal Uygulama (HWND_NOTOPMOST)` olmalı ve widget aktif pencerenin arkasında kalmalıdır; öne fırlamamalıdır.

### Test 2: Win + D (Masaüstünü Göster — WispDock Modeli)
- **İşlem:** Klavyeden <kbd>Win</kbd> + <kbd>D</kbd> tuş kombinasyonuna basın.
- **Kabul Ölçütü:** Normal uygulamalar simge durumuna küçülürken, UsageGauge SC_MINIMIZE engellemesi ve WinEvent kancası sayesinde masaüstü yüzeyinde **doğrudan görünür kalmalıdır**. Arayüzdeki Z-Order durumu `Masaüstü (HWND_TOPMOST)` olmalıdır.

### Test 3: Tıklama ve Sürükleme Odak Davranışı (Önceden GEÇTİ ✓)
- **İşlem:** Bir metin düzenleyicide (Notepad) yazı yazarken veya düzenleyici aktifken UsageGauge üzerindeki **"Ön Pencereyi Kilitle"** butonuna basın ve widget'ı sürükleyin.
- **Beklenen Davranış:** UsageGauge odağı çalmamalı; arayüzdeki "Odak Sonucu" yeşil renkle `BAŞARILI: Odak referansta korundu ✓` yazmaya devam etmelidir.

### Test 4: İki Monitör Arasında Taşıma (Önceden GEÇTİ ✓)
- **İşlem:** Widget'ı üst tutamacından tutarak Monitör 1'den Monitör 2'ye doğru sürükleyip bırakın.
- **Beklenen Davranış:** İki monitör arasında DPI ölçek değişimlerinde sıçrama veya kaybolma olmadan pürüzsüzce taşınabilmelidir.
