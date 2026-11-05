# UsageGauge — T09: Son Teslim ve Kapanış Hazırlığı

Bu belge, UsageGauge projesinin **T09 Son Teslim ve Kapanış Hazırlığı** aşamasında tamamlanan ürün özelliklerini, otomatik ve manuel doğrulama kanıtlarını, release paketi verilerini, kullanıcının manuel test bildirimiyle kapatılan AC-04 kabul adımını ve nihai kapanış prosedürünü tanımlar.

---

## 1. Alt Görevler ve Kapanış Durumu

| Görev / Alan | Başlık ve Kapsam | Durum | Doğrulama Dayanağı |
| :--- | :--- | :---: | :--- |
| **T09.1** | Dokümantasyon Tutarlılığı | **PASS** | Kök `README.md` tek açık giriş noktası olarak oluşturuldu. `src/UsageGauge/README.md` gerçek dizin ve dosya yapısına (Services/, Models/, ViewModels/) ve dinamik Z-order prensiplerine güncellendi; eski spike butonları/telemetrisi tarihsel bilgiye dönüştürüldü. `docs/DEPLOYMENT-GUIDE.md` ve RC kayıtları tam uyumlu hale getirildi. |
| **T09.2** | Son Paket ve Kaynak Doğrulaması | **PASS** | `dotnet build -c Release` (0 Hata/0 Uyarı), 87/87 test koşumu tamamlandı. `scripts/publish.ps1` ile üretilen RC paketi izole temp dizinine açılarak doğrulandı; sıfır PDB, log, config veya CLI sızıntısı teyit edildi. |
| **T09.3** | Kapanış Kaydı ve Kullanıcıya Son Test | **PASS** | Nihai teslimat dokümantasyonu tamamlandı. Mimari plana T09 satırı eklendi. AC-04 fiziksel saat gözlemi yönergesi hazırlandı ve kullanıcının bildirdiği test sonucuyla başarıyla kapatıldı. |
| **TEKNİK TESLİMAT** | **T01 – T09 Kod ve Paket Hazırlığı** | **PASS** | Tüm geliştirme, mimari, güvenlik, hata yolları ve paketleme çalışmaları eksiksiz tamamlandı. |
| **NİHAİ FİZİKSEL KABUL** | **AC-04 Uzun Süreli Cihaz Gözlemi** | **PASS** | Kullanıcının manuel test bildirimi dayanağıyla her şeyin beklendiği gibi çalıştığı doğrulanarak PASS olarak kapatıldı. |
| **GENEL PROJE KABULÜ** | **AC-01 – AC-06 Kabul Ölçütleri** | **PASS** | Tüm kabul kriterleri (AC-01..AC-06) eksiksiz olarak tamamlandı (%100). |

---

## 2. Tamamlanan Ürün Özellikleri ve Kanıt Türleri

UsageGauge, geliştiricilerin günlük iş akışını bölmeden AI kullanım kotalarını takip edebilmeleri için aşağıdaki yeteneklerle donatılmıştır:

1. **2 Havuz / 4 Kota Penceresi (2 Kart):**
   - **ChatGPT (Work/Codex):** 5 Saatlik Havuz ve Haftalık Havuz.
   - **Antigravity (Gemini):** 5 Saatlik Havuz ve Haftalık Havuz.
   - *Kanıt Türü:* Kod tabanı (`MainWindowViewModel`, `QuotaCardViewModel`, `QuotaWindowViewModel`), 87 birim testi ve Release derleme çıktısı.
2. **Odak Çalmayan Dinamik Z-Order Masaüstü Katmanı (`WS_EX_NOACTIVATE`, `HWND_NOTOPMOST` / `HWND_TOPMOST`):**
   - Normal pencerelerin (`Chrome`, `VS Code` vb.) arkasında kalır; pencerelerin önüne fırlamaz. Tıklama ve sürükleme sırasında diğer uygulamaların metin/klavye odağını çalmaz.
   - Masaüstü katmanı ve pencereler arası geçişler WispDock modelinden uyarlanan dinamik Z-order kancası (`SetWinEventHook`, `WS_EX_NOACTIVATE`, normal uygulamalarda HWND_NOTOPMOST ve aktif pencerenin arkasına yerleştirme, masaüstüne dönüldüğünde `HWND_TOPMOST`) ile yönetilir.
   - *Kanıt Türü:* T03 spike'ı, Win32 P/Invoke kancaları ve **kullanıcının gerçek Windows 11 cihazında doğruladığı manuel test**.
3. **Masaüstünü Göster (`Win + D`) Koruması:**
   - `Win + D` kısayolu ile masaüstüne geçildiğinde simge durumuna küçülmez (`SC_MINIMIZE` engellemesi); masaüstü katmanında görünür kalır.
   - *Kanıt Türü:* SC_MINIMIZE engellemesi, WinEvent kancası ve **kullanıcının gerçek Windows 11 cihazında doğruladığı manuel test**.
4. **Çoklu Monitör, DPI ve Konum Kalıcılığı:**
   - İki monitör arasında DPI ölçek değişimlerinde pürüzsüz taşıma; uygulama kapatılıp açıldığında son koordinatların korunması; ekran sayısı değiştiğinde veya monitör kaybolduğunda güvenli çalışma alanına geri çekilme (`ScreenHelper`, `WM_DISPLAYCHANGE`).
   - *Kanıt Türü:* 8 birim testi ve **kullanıcının iki monitörlü fiziksel cihazında doğruladığı AC-02 testi (PASS)**.
5. **Güvenli ve Bağımsız Veri Sağlayıcıları:**
   - Sıfır token harcaması resmi CLI sorguları:
     - ChatGPT: `codex app-server --stdio` (JSON-RPC 2.0 `account/rateLimits/read`)
     - Antigravity: `agy -p /usage --output-format json --print-timeout 10s`
   - Periyodik sorgu ve yerel geri sayım mimarisi:
     - **Kota Yüzdeleri:** Periyodik sağlayıcı sorguları (10 dakikada bir veya "Şimdi Yenile" ile) üzerinden güncellenir; sorgular arasında yüzde tahmini/yapay olarak düşürülmez.
     - **Geri Sayım Sayaçları:** Sağlayıcıdan alınan UTC sıfırlanma zamanı (`DateTimeOffset`) referans alınarak yerel zamanlayıcı ile gerçek zamanlı geriye doğru akar.
   - Sağlayıcı yalıtımı: ChatGPT veya Antigravity'den biri hata verse/kurulu olmasa dahi diğeri normal çalışır.
   - Gizlilik ve Güvenlik: Kullanıcı şifresi/tokeni asla okunmaz; hata durumlarında hassas veri sızdırılmaz (maskelenir); dış süreç zaman aşımı 15 saniyedir.
   - *Kanıt Türü:* `QuotaSchedulerTests`, `MainWindowViewModelTests` ve `ChatGptProviderTests` (87 test).
6. **Taşınabilir (Portable) ve Güvenli Paketleme:**
   - .NET 10 self-contained tek dosya (`UsageGauge.exe`) ve kılavuz (`README.txt`).
   - Win32 `ReplaceFile` API'si ile hedef `.zip` dosyasının atomik güncellenmesi; hata durumunda orijinal yedeğin silinmeyip korunması.
   - Kullanıcı dosyalarının (`config.json`, günlükler) asla ezilmemesi veya silinmemesi.
   - *Kanıt Türü:* İzole geçici dizin simülasyonları (`-SimulatePreReplaceFailure`), SHA-256 doğrulama testleri.

---

## 3. Gerçek Derleme, Test ve Paket Doğrulama Çıktıları

### 3.1 Release Derlemesi
```powershell
dotnet build UsageGauge.slnx -c Release
```
- **Sonuç:** `0 Warning(s), 0 Error(s)`
- **Süre:** ~5.8 saniye

### 3.2 Birim ve Entegrasyon Testleri
```powershell
dotnet test UsageGauge.slnx -c Release --filter "Category!=Live"
```
- **Sonuç:** **Toplam: 87 | Başarılı: 87 | Başarısız: 0 | Atlanan: 0**
- **Test Kapsamı:** ViewModels, QuotaScheduler (zamanlayıcı, geri çekilme, bağımsız yenileme), Providers (JSON ayrıştırma, hata maskeleme), ScreenHelper (monitör seçimi, DPI koordinatları), ConfigService (atomik yazma).

### 3.3 Yayınlama ve Paket Bütünlüğü (`scripts/publish.ps1`)
```powershell
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
```
- **Paket Dosyası:** `dist\UsageGauge-v1.0-win-x64.zip`
- **Paket Boyutu:** `59.227.202 bayt` (56,48 MB)
- **SHA-256 Özeti:** `AF5EC2B03CEC5FC3CF8A7E4AA258DB0687153FC01F48C0C6432FE692BDD30F90`
- **İzole Çıkartma ve Dosya Yapısı Doğrulaması:**
  - `README.txt` (6.124 bayt)
  - `UsageGauge.exe` (64.904.917 bayt)
  - Yasaklı girdi kontrolü (`*.pdb`, `*.log`, `config.json`, `codex.exe`, `agy.exe`): **0 adet (Temiz)**.

---

## 4. Kullanıcının Gerçek Cihazında Önceden Doğruladığı Özellikler

Aşağıdaki işlevler, kullanıcının fiziksel Windows 11 ortamında (iki monitörlü masaüstü) adım adım test edilmiş ve doğrulanmıştır:

1. **Win + D (Masaüstünü Göster):** Widget masaüstünde açık kalır, diğer pencereler küçülürken görünürlüğü bozulmaz (**GEÇTİ**).
2. **Normal Pencerelerin Arkasında Kalma:** Tarayıcı (Chrome/Edge) veya IDE (VS Code) penceresi üzerine getirildiğinde widget arkada kalır, öne fırlamaz (**GEÇTİ**).
3. **Çift Monitör Taşıma ve Konum Kalıcılığı:** Widget ikinci monitöre taşınıp kapatıldığında, yeniden açılışta ikinci monitördeki koordinatlarında başlar (**GEÇTİ**).
4. **Sağ Tık Menüsü:** "Şimdi Yenile", opaklık seviyeleri (%100, %80, %60), "Varsayılan Konuma Sıfırla" sorunsuz çalışır (**GEÇTİ**).
5. **Kota Okuma:** ChatGPT Work/Codex ve Antigravity Gemini olmak üzere 4 kota penceresi gerçek oturumlardan başarıyla okunur (**GEÇTİ**).

> [!NOTE]
> **Sonraki T11 Güncellemesi:** T09 aşamasında sağ tık menüsünde bulunan opaklık ve konum kontrolleri, sonraki **T11** geliştirmesinde başlıktaki `[⚙]` düğmesi veya sağ tık üzerinden açılan bağımsız **Ayarlar** penceresine taşınmıştır. Dört görünüm düzeni (`Normal`, `Orta`, `Orta-Mini`, `Mini`) ve `Her Zaman Üstte` kontrolleri de bu ayrı pencerede sunulmaktadır; sağ tık menüsü ise hızlı işlemler (`Şimdi Yenile`, `Ayarlar...`, `Gizle`, `Çıkış`) için sadeleştirilmiştir (T11 kapsamındaki bu yeni kontroller T09 teslim testlerinin parçası değildir; ayrı T11 doğrulama paketinde test edilmiştir).

---

## 5. AC-04 Uzun Süreli Fiziksel Cihaz Gözlemi Doğrulaması (PASS)

### 5.1 Mevcut Durum ve Kanıt Sınırı
- **Kod ve Simülasyon Kapsamı (PASS):**
  - İki periyodik sorgu arasında veya model kullanılmadığında kota yüzdesinin yapay/tahmini bir algoritmayla azaltılmadığı kod düzeyinde doğrulanmıştır.
  - Geri sayım sayacının istemci yerel saat kaymalarından bağımsız UTC epoch farkı (`DateTimeOffset.UtcNow`) üzerinden geriye doğru aktığı test edilmiştir.
  - Test ortamında ~4 saat 40 dakikalık sanal saat ilerlemesi simülasyonu başarıyla yürütülmüştür (bu simülasyon mantık ve durum makinesi güvencesi olup, fiziksel gözlem yerine sunulmamıştır).
- **Fiziksel Cihaz Kapsamı (PASS — Kullanıcının Manuel Test Bildirimi Dayanağıyla):**
  - Kullanıcı, proje sonuna bırakılan fiziksel cihaz testini kendi Windows 11 ortamında gerçekleştirdiğini ve her şeyin beklendiği gibi çalıştığını (kota yüzdesinin yapay azalmadığını, sayaçların geriye doğru düzenli aktığını ve veri dürüstlüğünün korunduğunu) bildirmiştir.
  - **Dürüstlük İlkesi:** Kullanıcı tarafından ek bir test süresi, saat dökümü, başlangıç/bitiş yüzdeleri veya ekran görüntüsü iletilmediğinden kurgusal değerler üretilmemiş; kullanıcının doğrudan teyit bildirimi kabul dayanağı olarak kaydedilmiştir.

### 5.2 Kullanıcı İçin Pratik Test Rehberi (Referans Kayıt)

> [!NOTE]
> 10 dakikalık periyodik sağlayıcı sorguları başarılı olduğunda, kota sıfırlanma anı geldiğinde veya başka bir oturumda/cihazda aynı hesapla model kullanıldığında yüzde değeri değişebilir; bu durum beklenen ve doğal bir yenilenmedir.
> Testin amacı; **sağlayıcı sorguları arasında arayüzün kendi kendine yapay yüzde düşüşü üretmediğini** ve **sıfırlanma sayacının kesintisiz aktığını** doğrulamaktır.

Kullanıcının doğruladığı temel kriterler:
1. İki sağlayıcı sorgusu arasında (veya model kullanılmadığında) yüzde yapay olarak azalmamalı, yerel tahmine dayalı düşüş sergilememelidir (Doğrulandı).
2. Sıfırlanma sayacı yerel zamanlayıcı ile düzenli olarak geriye doğru akmalıdır (Doğrulandı).

### 5.3 Nihai Kabulün Kapanış Durumu
Kullanıcının manuel test bildirimiyle birlikte:
- `docs/UX-DEFINITION-v1.0.md` içerisindeki AC-04 maddesi `[x]` olarak işaretlenmiştir,
- Mimari plandaki AC-04 durumu `PASS` olarak güncellenmiştir,
- Böylece projenin tüm UX ve teknik kabul ölçütleri (AC-01..AC-06) %100 tamamlanmıştır.
