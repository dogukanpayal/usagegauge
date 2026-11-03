# UsageGauge — T08: Son Sürüm Sağlamlaştırma (Release Candidate)

Bu belge, UsageGauge projesinin **T08 Son Sürüm Sağlamlaştırma** aşamasında gerçekleştirilen paketleme güvenilirliği düzeltmelerini, Win32 `ReplaceFile` yedek koruma güvencelerini, çalışma zamanı hata yolları denetimlerini, izole doğrulama testlerini ve release candidate (RC) paket bilgilerini içerir.

---

## 1. Yönetici Özeti ve Durum

| Alt Görev | Başlık ve Kapsam | Durum | Doğrulanan Kapsam ve Dayanak |
| :--- | :--- | :---: | :--- |
| **T08.1** | Paketleme ve Güncelleme Güvenilirliği | **PASS** | `publish.ps1` tek ZIP dosyası için aynı dosya sisteminde aday dosya oluşturma ve Win32 `File.Replace` ile atomik değiştirme güvencesine kavuşturuldu. Değiştirme başarısızlığı durumunda yedeğin silinmesi engellendi (kod incelemesi güvencesi). Gevşek klasör kopyalama hatasında geçerli kalan çıktılar doğru ayrıştırıldı. Çıktı klasöründeki kullanıcı dosyaları korundu. |
| **T08.2** | Çalışma Zamanı Hata Yolları Denetimi | **PASS** | 5 temel hata senaryosu (CLI yokluğu, oturum eksikliği, zaman aşımı, bozuk yanıt, tekil sağlayıcı hatası) kaynak ve testlerle denetlendi; `MainWindowViewModelTests` sınıfına 3 yeni regresyon testi eklendi (toplam 87 test). |
| **T08.3** | Release Candidate ve Teslim | **PASS** | Release derlemesi (0 hata/0 uyarı), 87/87 test koşumu tamamlandı. Güncellenmiş betikle `dist\UsageGauge-v1.0-win-x64.zip` üretildi; dosya listesi, boyut, temizlik ve SHA-256 doğrulandı. |
| **T08 GENEL** | **Son Sürüm Sağlamlaştırma** | **PASS** | Release candidate paketi hazırlandı. (AC-04 fiziksel saatler süren cihaz gözlemi kullanıcı tarafından proje sonunda yapılacaktır; PARTIAL durumunu korur). |

---

## 2. T08.1: Paketleme ve Güncelleme Güvenilirliği

### 2.1 Belirlenen Sorunlar, Kod İncelemesi ve Yapılan Düzeltmeler

#### 1. Gerçek ZIP Değiştirme Güvencesi ve Yedek Korunumu:
- **Sorun:** Win32 `ReplaceFileW` API'si çağrıldığında, Windows önce mevcut hedef dosyayı (`$zipPath`) yedek adına (`$backupZipPath`) taşır; ardından yeni aday dosyayı (`$candidateZipPath`) hedef adına taşır. Eğer ikinci aşamada bir hata oluşursa (örneğin `ERROR_UNABLE_TO_MOVE_REPLACEMENT_2` / hata kodu 1177), eski hedef dosya yedek adında kalmış olur. Önceki kodda hata olsa dahi hem iç hem dış `finally` bloklarında bu yedek siliniyordu; bu durum eski ZIP'in tek sağlam kopyasının kaybolmasına yol açabilirdi.
- **Düzeltme & Güvence (Kod İncelemesi Dayanağı):**
  - `$backupZipPath` temizliği **yalnızca değiştirme işleminin başarıyla tamamlandığı kesinleştiğinde** (`$replaceSucceeded = $true`) çalışır.
  - Değiştirme sırasında istisna fırlatılırsa, hem iç `catch` bloğu hem de dış `finally` bloğu yedeği **kesinlikle silmez**.
  - Hata anında hedef dosya, yedek dosya ve aday dosyanın varlık ve boyut bilgileri konsola detaylı dökülür; orijinal ZIP dosyasının yedek yolunda güvenle korunduğu kullanıcıya/yöneticiye bildirilir.
  - *Kanıt Sınırı / Dürüstlük Beyanı:* `ERROR_UNABLE_TO_MOVE_REPLACEMENT_2` (1177) gibi çekirdek düzeyindeki nadir dosya sistemi hata kodları fiziksel Windows ortamında özel sürücü/arıza enjeksiyonu olmaksızın doğrudan üretilemediğinden, bu spesifik durumun cihazda test edildiği iddia edilmemiştir; **kod incelemesi ve statik mantık analiziyle güvence altına alınmıştır**.
- **Aday Dosya Temizliği:**
  - Değiştirme teşebbüsünden önce hata oluşursa (`$replaceAttempted = $false`, örneğin `-SimulatePreReplaceFailure` testinde veya aday doğrulama hatasında), adayın temizlenmesi korunur (çalışma alanı temiz kalır).
  - Değiştirme denenip başarısız olduysa (`$replaceAttempted -and -not $replaceSucceeded`), aday dosya da adli inceleme ve kurtarma için silinmez.

#### 2. Gevşek Çıktı Klasörü ve Atomiklik Kapsamı:
- **Sıralama ve Durum Raporlaması:** ZIP arşivi atomik olarak güncellendikten sonra `dist\UsageGauge-singlefile` klasörüne gevşek ikililerin kopyalanması denenir. Eğer bu aşamada bir hata oluşursa (örneğin çalışan bir `UsageGauge.exe` nedeniyle dosya kilitliyse):
  - Dağıtım ZIP paketinin (`$zipPath`) **başarıyla güncellendiği ve geçerli olduğu**,
  - Hedef gevşek klasörün (`UsageGauge-singlefile`) ise **güncellenemediği**,
  ayrıntılı biçimde konsola dökülür ve betik bu açık teşhisle sonlanır.
- **Kapsam Sınırı:** **Tüm çıktı klasörü atomik değildir.** Yalnızca tekil `UsageGauge-v1.0-win-x64.zip` arşivi Win32 `ReplaceFile` ile dosya sistemi düzeyinde atomik değiştirilir.

#### 3. Hedef Dizindeki Kullanıcı Dosyalarının ve Günlüklerin Korunması:
- Hedef dizindeki tüm `*.pdb` dosyalarını silen kontrolsüz döngü kaldırıldı; yalnızca staging aşamasında PDB'ler paketten çıkarılır.
- Tek dosya modunda (`UsageGauge-singlefile`) yalnızca belirlenmiş iki çıktı yenilenir:
  - `UsageGauge.exe`
  - `README.txt`
- `config.json`, `usagegauge-diagnostics.log` ve kullanıcı tarafından eklenmiş başka dosyalar asla silinmez veya ezilmez.

---

### 2.2 Anlamlı Doğrulama Testleri (İzole Geçici Hedef Dizin)

Fiziksel Windows ortamında geçici bir hedef dizinde (`%TEMP%\UG_TestDist_<GUID>`) iki aşamalı izole test icra edilmiştir:

#### Test 1: Değiştirme Öncesi Kontrollü Başarısızlık (Post-Build & Post-Verify)
- **Amaç:** Aday ZIP oluşturulup içeriği doğrulandıktan hemen sonra, hedef dosya değiştirilmeden önce hata tetiklendiğinde önceki ZIP'in bütünlüğünün bozulmadığını ve aday geçici dosyasının temizlendiğini kanıtlamak.
- **Koşum:** `publish.ps1 -OutputDir $testDist -SimulatePreReplaceFailure`
- **Sonuç:**
  - Önceki ZIP varlığını korudu: **True**
  - Önceki ZIP SHA-256 hash değeri birebir aynı kaldı: **True** (`01F11D2680F44562...`)
  - Dizinde artık aday dosya (`*.candidate.*`) kalmadı: **True**
  - Kullanıcı dosyaları (`config.json`, log, özel notlar) korundu: **True**
  - **Durum: PASS**

#### Test 2: Başarılı Atomik Değiştirme ve Kullanıcı Dosyası Korunumu
- **Amaç:** Aday doğrulandıktan sonra atomik değiştirmenin başarıyla sonuçlandığını, yeni ZIP'in geçerli olduğunu, geçici yedeklerin temizlendiğini ve kullanıcı dosyalarının korunduğunu kanıtlamak.
- **Koşum:** `publish.ps1 -OutputDir $testDist`
- **Sonuç:**
  - Yeni ZIP oluşturuldu ve hedefte güncellendi: **True**
  - Hash değeri güncellendi: **True** (`597EE0DC2774C9B6...`)
  - Yeni ZIP `UsageGauge.exe` ve `README.txt` içeriyor: **True**
  - Geçici yedek (`*.backup.*`) ve aday (`*.candidate.*`) temizlendi: **True**
  - Kullanıcı `config.json`, logu ve özel dosyaları korundu: **True**
  - **Durum: PASS**

---

## 3. T08.2: Çalışma Zamanı Hata Yolları Denetimi

Mevcut kaynaklar (`ChatGptProvider`, `AntigravityProvider`, `QuotaScheduler`, `MainWindowViewModel`) 5 temel hata senaryosu açısından denetlendi:

1. **CLI Bulunamaması (`CliNotFound`):** `codex.exe` veya `agy.exe` bulunamazsa widget çökmez; ilgili kartta `✕ CLI Bulunamadı` rozeti ve kurulum yönergesi sunulur. Diğer kart normal çalışır.
2. **Oturum Eksikliği (`AuthenticationRequired`):** Oturum açılmamışsa veya yetkilendirme hatası dönerse `🔑 Oturum Gerekli` rozeti gösterilir; şifre veya token asla okunmaz/saklanmaz.
3. **Zaman Aşımı (`Timeout`):** Dış CLI süreçleri 15 saniyeyi aşarsa `TimeoutException` yakalanır, süreç sonlandırılır ve 10-15-20-30 dk üstel geri çekilme takvimine geçilir.
4. **Bozuk Yanıt (`InvalidResponse`):** Yanıt JSON değilse veya eksik kota penceresi varsa `⚠ Okuma Hatası` rozeti gösterilir; önceki başarılı veri varsa `✓ Eski Veri (%XX)` olarak korunur.
5. **Tekil Sağlayıcı Hatası:** Bir sağlayıcı istisna fırlatsa veya çökse dahi bağımsız kilitler sayesinde diğer kart normal güncellenir.

### Test Kapsamı
`tests/UsageGauge.Tests/ViewModels/MainWindowViewModelTests.cs` dosyasına eklenen regresyon testleri ile birlikte toplam test sayısı **87**'ye ulaşmıştır:
- `StartInitialRefreshAsync_WhenAntigravityCliNotFound_ChatGptSucceeds_IsolatesErrorAndDisplaysAppropriateStatuses`
- `StartInitialRefreshAsync_WhenAntigravityReturnsMalformedResponse_ChatGptSucceeds_AndDisplaysAppropriateStatus`
- `StartInitialRefreshAsync_WhenBothRequireAuthentication_DisplaysSessionRequiredOnBothCards`

---

## 4. T08.3: Release Candidate (RC) Paket Bilgileri

### 4.1 Derleme ve Test Koşumu
- **Release Derlemesi:**
  ```powershell
  dotnet build UsageGauge.slnx -c Release
  ```
  *Sonuç:* 0 Hata, 0 Uyarı.
- **Birim & Regresyon Testleri:**
  ```powershell
  dotnet test UsageGauge.slnx -c Release --filter "Category!=Live"
  ```
  *Sonuç:* **Toplam: 87 | Geçen: 87 | Kalan: 0 | Atlanan: 0**

### 4.2 Son Kullanıcı Dağıtım Paketi
- **Paket Dosyası:** `dist\UsageGauge-v1.0-win-x64.zip`
- **Boyut:** `59.227.236 bayt` (56,48 MB)
- **SHA-256:** `7033308731B9EA7B61657A94C31C4EA47679D143B1647F40BC097043F357E232`
- **İçerik Doğrulaması (`tar -tf`):**
  ```text
  README.txt
  UsageGauge.exe
  ```
- **Güvenlik Doğrulaması:**
  - `*.pdb` sembolleri: **Yok** (paketten çıkarıldı).
  - `codex.exe`, `agy.exe`: **Yok** (harici sistem araçlarıdır, gömülmez).
  - `config.json`, `*.log`: **Yok** (dağıtım paketine dahil edilmez).
  - Kimlik bilgisi / API token: **Yok**.

---

## 5. Bilinen Sınırlar ve Kullanıcı İçin Son Manuel Adımlar

### Bilinen Sınırlar
- **AC-02 (Çoklu Monitör ve Çalışma Alanı):** İki monitörde taşıma, çözünürlük/ölçek değişimi ve varsayılan konuma sıfırlama kullanıcının fiziksel Windows 11 cihazında başarıyla doğrulandı (**PASS**).
- **AC-04 (Uzun Süreli Fiziksel Gözlem):** Yüzde değişmezliği, UTC sayaç hesabı ve sıfırlanma eşiği davranışları birim testleri ve ~4 saat 40 dakikalık sanal saat simülasyonu ile doğrulandı. Saatler süren kesintisiz fiziksel cihaz gözlemi proje sonunda kullanıcının takvimine bırakıldı (**PARTIAL**).

### Kullanıcı İçin Son Manuel Kontrol Yönergesi
1. `dist\UsageGauge-v1.0-win-x64.zip` arşivini veya `dist\UsageGauge-singlefile\UsageGauge.exe` dosyasını çalıştırın.
2. İki kartta 4 kotanın (ChatGPT 5s/haftalık, Antigravity Gemini 5s/haftalık) okunup okunmadığını kontrol edin.
3. Sağ tık menüsünden:
   - `Şimdi Yenile` seçeneğini deneyin (taze verinin geldiğini görün).
   - `Opaklık` ayarını `%80` ve `%60` olarak test edin.
   - `Varsayılan Konuma Sıfırla` ile pencerenin ana ekranın sağ alt köşesine oturduğunu teyit edin.
4. Widget'ı kapatıp açtığınızda son konumunun korunduğunu teyit edin.
