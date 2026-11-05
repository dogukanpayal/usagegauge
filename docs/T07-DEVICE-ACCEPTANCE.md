# UsageGauge — T07: Cihaz Kabul Doğrulaması

Bu belge, UsageGauge projesinin **T07 Cihaz Kabul Doğrulaması** kapsamındaki otomatik test kanıtlarını, simülasyon sonuçlarını ve kullanıcının fiziksel Windows 11 ortamında gerçekleştireceği manuel kabul adımlarını içerir.

---

## 1. Otomatik Test ve Simülasyon Kanıtları

### 1.1 Derleme ve Birim/Entegrasyon Testleri
- **Yapılandırma:** Release (`win-x64`)
- **Derleme Durumu:** `dotnet build UsageGauge.slnx -c Release` — 0 Hata, 0 Uyarı.
- **Çevrimdışı Test Koşumu:** `dotnet test UsageGauge.slnx -c Release --filter "Category!=Live"`
  - **Sonuç:** Toplam **84 test** çalıştırıldı; **84 geçti (Passed)**, **0 kaldı (Failed)**, **0 atlandı (Skipped)**.

### 1.2 T07.1: Monitör, DPI ve Çözünürlük Güvenceleri
1. **Dinamik Ekran Değişikliği Kancası (`WM_DISPLAYCHANGE` & `WM_DPICHANGED`):**
   - Windows üzerinde ekran çözünürlüğü, DPI ölçeği veya monitör sayısı çalışma anında değiştiğinde işletim sistemi üst düzey pencerelere `WM_DISPLAYCHANGE` (0x007E) ve `WM_DPICHANGED` (0x02E0) mesajlarını gönderir.
   - `DesktopWindowManager.cs` mesaj döngüsüne bu mesajlar eklendi ve `DisplaySettingsChanged` olayı tanımlandı.
   - `MainWindow.xaml.cs` bu olayı dinleyerek:
     - Çoklu monitör sağ tık menüsünü (`PopulateMonitorMenu`) güncel monitörlerle anında yeniler.
     - `LoadAndApplyConfig(saveIfNewOrAdjusted: true)` çağrısıyla pencereyi yeni ekran sınırlarına göre doğrular ve gerekirse çalışma alanının içine çeker.
2. **Çözünürlük Küçülmesi Testi (`ConfigServiceTests`):**
   - `EnsureVisibleBounds_WhenResolutionShrinksOnDualMonitors_ClampsWithinVisibleWorkArea` testi ile 4K'dan Full HD'ye düşen ikinci monitörde pencerenin yeni sınırlar içine güvenle hapsedildiği kanıtlanmıştır.
3. **Varsayılan Konuma Sıfırla İyileştirmesi:**
   - Sağ tık menüsündeki `Varsayılan Konuma Sıfırla` komutu, dokümantasyonda ve mimari planda belirtildiği gibi pencereyi birincil monitörün sağ alt güvenli çalışma alanına (`Right - width - 24`, `Bottom - height - 48`) konumlandırmaktadır.

### 1.3 T07.2: Deterministik Sanal Saat Simülasyonu (Yaklaşık 4 Saat 40 Dakika)
`MultiHourLifecycleSimulationTests.cs` ile sanal saat (`_fakeClock`) ilerletilerek yaklaşık 4 saat 40 dakikalık yaşam döngüsü senaryoları test edilmiştir (T0=10:00'dan 14:30 sıfırlanma anı ve 14:40 ilk geri çekilme adımına kadar toplam 280 dakika / yaklaşık 4 saat 40 dakika):
1. **Yüzde Sabitliği:** Sağlayıcıdan yeni yanıt gelmedikçe geçen saatler boyunca kalan yüzde (örn. `%42`) asla yapay olarak eksiltilmez veya değiştirilmez.
2. **UTC Sayaç Hesabı:** Geri sayım sayacı mutlak UTC `ResetsAtUtc` değeri üzerinden yerel saat kaymalarından bağımsız olarak `4s 30dk` -> `3s 30dk` -> `1s 30dk` -> `15dk` şeklinde tam doğrulukla geri sayar.
3. **Sıfırlanma Anı Davranışı:** Sıfırlanma zamanı dolduğunda yüzde kendiliğinden `%100` yapılmaz; tek seferlik sorgu tetiklenir. Sorgu başarısız olursa önceki yüzde `%42` olarak (eski veri etiketiyle) korunur ve sayaç `Sıfırlanma zamanı doldu — yeni kota doğrulanıyor` metnini gösterir.
4. **Sorgu Takvimi ve Backoff:** Normal sorgu periyodu 10 dakikadır; hata durumlarında 10, 15, 20, 30 dakikalık üstel geri çekilme takvimine tam uyulur. Dakikalar veya saniyeler içinde mükerrer sorgu döngüsü oluşmaz.

> [!NOTE]
> **Sanal Saat Testi ile Fiziksel Cihaz Gözleminin Ayrımı:**
> - **Sanal Saat Simülasyonu (Birim/Entegrasyon Testi):** Bellek içi sahte saat ile deterministik durum makinelerini, sıfırlanma eşiklerini ve UTC zaman farkı hesaplarını saniyeler içinde yaklaşık 4 saat 40 dakikalık sanal zaman akışı boyunca doğrulamıştır (PASS).
> - **Fiziksel Cihaz Gözlemi (Kullanıcı Tarafından Gerçekleştirildi):** Kullanıcının gerçek Windows 11 ortamında uygulamanın açık kaldığı süre boyunca kotanın yapay düşmediği ve sayaçların düzenli aktığı kullanıcı tarafından bizzat gözlemlenmiş ve her şeyin beklendiği gibi çalıştığı bildirilmiştir (PASS).

---

## 2. AC-02 Doğrulama Sonucu (İki Monitör, Çözünürlük ve DPI)

- **Durum:** **DOĞRULANDI (PASS)**
- **Kanıt Türü:** **Kullanıcının manuel test bildirimi**
- **Doğrulama Notu:** Kullanıcı, iki monitörlü fiziksel Windows 11 ortamında aşağıdaki adımları uygulamış ve pencere davranışının başarıyla çalıştığını bildirmiştir ("her şey dediğin gibi çalışıyor").

### Gerçekleştirilen Manuel Test Adımları
1. **Ekran Ayarları:** Monitör 1 ve Monitör 2 görüntü ayarları açıkken widget başlatıldı.
2. **İkinci Monitöre Taşıma:** Widget Monitör 2'ye taşındı ve görüntülendiği teyit edildi.
3. **Çözünürlük / Ölçek Değişikliği:** İkinci ekranda çözünürlük veya ölçek değişikliği uygulandı.
4. **Davranış Doğrulaması:**
   - [x] Widget Monitör 2'nin çalışma alanı sınırları içinde görünür kaldı (ekranın dışına taşmadı / kaybolmadı).
   - [x] Sağ tık menüsünden **Varsayılan Konuma Sıfırla** seçildiğinde birincil ekranın sağ alt köşesine güvenle döndü.
5. **Ayarların Geri Alınması:** Ekran ayarları orijinal durumuna getirildi.

---

## 3. AC-04 Cihaz Gözlemi Durumu (Saatler Boyunca Gözlem)

- **Durum:** **DOĞRULANDI (PASS)**
- **Otomatik / Kod Doğrulaması:** **PASS** (Yaklaşık 4 saat 40 dakikalık deterministik sanal saat simülasyonu, 84 birim/entegrasyon testi ve kod incelemesi).
- **Fiziksel Cihaz Doğrulaması:** **PASS (Doğrulandı — Kanıt Türü: Kullanıcının manuel test bildirimi)**
- **Açıklama:** Kullanıcı, proje sonuna bırakılan AC-04 fiziksel cihaz testini gerçekleştirdiğini ve her şeyin beklendiği gibi çalıştığını (kota yüzdesinin sağlayıcı çağrısı dışında yapay azalmadığını, geri sayım sayaçlarının düzenli aktığını ve veri dürüstlüğünün korunduğunu) bildirmiştir. Bu bildirim dayanağıyla AC-04 fiziksel cihaz kabulü PASS olarak tamamlanmıştır.
- **Dürüstlük ve Kanıt Ayrımı:** Kullanıcı tarafından özel bir test süresi, saat dökümü, başlangıç/bitiş yüzdeleri veya ekran görüntüsü iletilmediğinden kurgusal değerler üretilmemiştir; kabul dayanağı olarak doğrudan kullanıcının test bildirimi esas alınmıştır. Sanal saat simülasyonu ise birim test seviyesindeki durum makinesi ve eşik güvencesi olarak konumlandırılmış, fiziksel gözlem yerine sunulmamıştır.

### Gözlem Kriterleri ve Doğrulanan Davranış
- **Yapay Azalma Olmaması:** Model çağrısı yapılmadığı sürece yüzde sabit kalır; uygulama zaman geçtikçe yüzdeyi kendiliğinden düşürmez (deterministik testler ve kullanıcının fiziksel test bildirimiyle doğrulanmıştır).
- **Sayaç Akışı:** Sıfırlanma sayacı yerel saate göre dinamik olarak geriye doğru akar (deterministik testler ve kullanıcının fiziksel test bildirimiyle doğrulanmıştır).
- **Fiziksel Cihaz Gözlemi:** Kullanıcı tarafından fiziksel cihazda test edilmiş ve doğrulandığı teyit edilmiştir.

### Gözlem Kayıt Tablosu Şablonu

| Gözlem Adımı | Saat (Yerel) | ChatGPT Kalan % | ChatGPT Sayacı | Antigravity Kalan % | Antigravity Sayacı | Kart Durumu (Rozet) | Notlar |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :--- |
| **Başlangıç (T0)** | Örn. 10:00 | %... | Örn. 4s 15dk | %... | Örn. 3s 40dk | ✓ Güncel | İlk açılış |
| **Ara Kontrol (T0 + 1 saat)** | Örn. 11:00 | %... | Örn. 3s 15dk | %... | Örn. 2s 40dk | ✓ Güncel | Model kullanılmadıysa % sabit olmalı |
| **Ara Kontrol (T0 + 2-3 saat)**| Örn. 13:00 | %... | Örn. 1s 15dk | %... | Örn. 0s 40dk | ✓ Güncel | Sayaç doğru aktı mı? |
| **Sıfırlanma Anı (Varsa)** | Örn. 14:15 | %... | Doğrulanıyor / Yenilendi | %... | ... | ... | Kendiliğinden %100 oldu mu? (Olmamalı) |

---

## 4. T07 Kabul Durum Özeti

| Alt Görev | Başlık ve Kapsam | Otomatik Doğrulama | Gerçek Cihaz Doğrulaması | Nihai Durum |
| :--- | :--- | :---: | :---: | :---: |
| **T07.1** | Monitör, DPI ve Çözünürlük Davranışı | **PASS** (84 test, WM_DISPLAYCHANGE kancası, sınır testleri) | **PASS** (Kullanıcının manuel test bildirimi) | **PASS** |
| **T07.2** | Saatler Boyunca Kota ve Sayaç Doğruluğu | **PASS** (Yaklaşık 4 saat 40 dakikalık sanal saat simülasyonu) | **PASS** (Kullanıcının manuel test bildirimi) | **PASS** |
| **T07.3** | Gerçek Cihaz Kabul Rehberi ve Teslim | **PASS** (Rehber ve form eksiksiz hazırlandı) | **TAMAMLANDI** | **PASS** |
| **T07 GENEL** | **Cihaz Kabul Doğrulaması** | **PASS** (Tüm kod, test ve mimari gereksinimler eksiksiz) | **PASS** (AC-02 ve AC-04 kullanıcı bildirimleriyle doğrulandı) | **PASS** |

> [!NOTE]
> Kullanıcı iki monitörlü fiziksel cihazında AC-02 adımlarını doğrulamış; proje sonuna bırakılan AC-04 saatler süren fiziksel cihaz gözlemini de gerçekleştirerek her şeyin beklendiği gibi çalıştığını bildirmiştir. Böylece T07 genel durumu **PASS** olarak tamamlanmıştır.
