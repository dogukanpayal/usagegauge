# UsageGauge v1.0 — Windows Masaüstü Widget'ı UX Tanımı

Bu belge, **UsageGauge** bağımsız Windows masaüstü widget'ının kullanıcı deneyimi (UX), görsel hiyerarşisi, pencere davranışları, etkileşim kuralları, veri güncellik ilkeleri ve kabul ölçütlerini tanımlar.

---

## 1. Ürün Amacı ve Temel İlkeler

UsageGauge; yazılım geliştiricilerin yapay zekâ model kullanım limitlerini (ChatGPT Work/Codex havuzu ile Antigravity model kotaları) geliştirme ortamlarını bölmeden, tarayıcıda veya masaüstü uygulamalarında ayar/kullanım ekranlarını açık tutma zorunluluğu olmadan izlemelerini sağlayan **bağımsız, hafif bir Windows masaüstü widget'ıdır**.

### Temel UX İlkeleri
1. **Sıfır Ekran Açma Zorunluluğu:** Kullanıcının ChatGPT web ayarlarını veya Antigravity `Models & Usage` ekranını açık tutmasını gerektiren hiçbir görsel veya operasyonel akış tasarlanmaz. Veriler arka plan mekanizmaları (`codex app-server` ve `agy`) üzerinden doğrudan okunur.
2. **Masaüstünde Rahatsız Etmeyen Varlık:** Widget **asla her zaman en üstte (`always-on-top`) durmaz**. Masaüstünde görünür kalır, normal uygulama pencerelerinin (IDE, tarayıcı, oyun) arkasında yer alır ve kendiliğinden sistem odağını çalmaz.
3. **Veri Dürüstlüğü:** Gösterilen yüzdeler, sağlayıcıdan son başarılı veri çekim anındaki net değerdir. Yüzdeler hiçbir koşulda zaman geçtikçe tahmini veya varsayımsal olarak azaltılıp artırılamaz. Sıfırlanma geri sayım sayacı ise yerel saat üzerinden gerçek zamanlı akar.
4. **Görsel Ayrım ve Çok Boyutlu Okunabilirlik:** İki ana havuz (ChatGPT ve Antigravity Gemini) birbirinden net kart sınırlarıyla ayrılır. Durum ve doluluklar yalnızca renklerle değil, açık metinler, sayısal değerler ve göstergelerle birlikte sunulur. *(Not: T05 nihai kararıyla Antigravity Claude/GPT kartı ürün kapsamından çıkarılmış, widget 2 kart ve 4 kota olarak sadeleştirilmiştir).*

---

## 2. Pencere Davranışı ve Monitör Yönetimi

### 2.1 Masaüstü Katmanı ve Pencere Davranışı Beklentileri
- **Normal Pencerelerin Arkasında Kalma:** Widget, kullanıcının çalıştığı aktif pencerelerin (kod editörü, terminal, tarayıcı vb.) arkasında, masaüstü yüzeyinde sakin bir biçimde durur; pencerelerin önüne fırlamaz.
- **Odak Çalmama (Non-Intrusive Focus):** Widget arka planda veri tazelediğinde veya kullanıcı widget üzerine tıkladığında/sürüklediğinde diğer uygulamaların aktifliğini bozmamalı, klavye/fare odağını gasp etmemelidir.
- **Masaüstünü Göster (Win + D) Davranışı:** Kullanıcı `Win + D` ile masaüstüne döndüğünde widget masaüstüyle birlikte kaybolmamalı; masaüstü alanında görünür ve doğrudan etkileşilebilir kalmalıdır.
- **Masaüstü Simgeleri ve Tıklanabilirlik:** Widget masaüstündeki simgelerin üzerine geldiğinde, widget'ın kendi üzerindeki butonlar (`[⟳]` vb.) ve sürükleme alanı kesintisiz çalışmalı; widget'ın boş alanları masaüstü kullanımını gereksiz kilitlememelidir.
- *Not: Bu gözlenebilir davranışların Windows pencere yöneticisindeki teknik katmanlama çözümü (HWND hiyerarşisi, alt pencere ilişkileri) T02 mimari kararlarına bırakılmıştır.*

### 2.2 Çoklu Monitör Desteği ve Konum Kalıcılığı
- **İki Monitör Desteği:** Kullanıcı widget'ı Birincil veya İkincil monitörde dilediği konuma serbestçe taşıyabilmelidir.
- **Görünür Çalışma Alanında Kalma:**
  - Widget kapatılıp açıldığında veya işletim sistemi yeniden başlatıldığında, kullanıcının bıraktığı monitörde ve çalışma alanında açılmalıdır.
  - Monitör sırası, birincil ekran seçimi, ekran çözünürlüğü veya Windows DPI ölçekleme oranı (Scaling: %100, %125, %150 vb.) değiştiğinde widget ekran dışına taşmamalı; her zaman geçerli ve görünür masaüstü çalışma alanının (work area) güvenli sınırları içinde kalmalıdır.
- *Not: Koordinatların, monitör tanıtıcılarının saklanma biçimi ve sanal masaüstü sınır kontrol algoritmaları T02 mimarisine bırakılmıştır.*

---

## 3. Bilgi Hiyerarşisi ve İki Grup Düzeni

Widget; dikey bloklar halinde düzenlenmiş, birbirinden net kart sınırlarıyla ayrılan **2 Ana Grup** barındırır:

*(Ürün Kararı Notu: Başlangıç fizibilite ve T04 aşamasında Antigravity sağlayıcısından 3. bir grup olarak okunan Claude/GPT havuzu, T05 nihai ürün kararıyla widget arayüzünden çıkarılmıştır. Widget yalnızca ChatGPT Work/Codex ve Antigravity Gemini olmak üzere 2 kart ve 4 kota penceresi gösterir. CLI ve T04 sağlayıcı katmanındaki protokol desteği korunmuştur.)*

```
+-------------------------------------------------------------+
| UsageGauge [Son: 17:05]                                [⟳] |
+-------------------------------------------------------------+
| [1] ChatGPT (Work/Codex)                                    |
|  • 5 Saatlik : [████████████████████░░] %84   | 4s 27dk     |
|  • Haftalık  : [██████████████░░░░░░░░] %68   | 3g 19s      |
+-------------------------------------------------------------+
| [2] Antigravity (Gemini Modelleri)                          |
|  • 5 Saatlik : [█████████████████████░] %96   | 4s 26dk     |
|  • Haftalık  : [████████████████░░░░░░] %74   | 2g 23s      |
+-------------------------------------------------------------+
```

### 3.1 Grup 1: ChatGPT (Work / Codex Ortak Havuzu)
- **Kapsam:** ChatGPT Plus/Pro/Team/Work hesabındaki paylaşımlı kod çalıştırma havuzu.
- **Alt Alanlar:**
  1. *5 Saatlik Limit (`300 dk` penceresi):* Kalan yüzde (`%XX`) + yerel saate göre dinamik geri sayım süresi.
  2. *Haftalık Limit (`10080 dk` penceresi):* Kalan yüzde (`%XX`) + yerel saate göre dinamik geri sayım süresi.

### 3.2 Grup 2: Antigravity (Gemini Modelleri)
- **Kapsam:** Gemini Flash ve Gemini Pro modellerinin paylaştığı ortak kota havuzu.
- **Alt Alanlar:**
  1. *5 Saatlik Limit (`5h` penceresi):* Kalan yüzde (`%XX`) + yerel saate göre sıfırlanma sayacı.
  2. *Haftalık Limit (`weekly` penceresi):* Kalan yüzde (`%XX`) + yerel saate göre sıfırlanma sayacı.

*(Tarihsel Not: T00.8a ve T04'te doğrulanan Antigravity Claude ve GPT modelleri grubu, sağlayıcı seviyesinde okunabilir kalmaya devam etmekte ancak widget arayüzünde gösterilmemektedir.)*

---

## 4. Tipografi, Okunabilirlik ve Erişilebilirlik

### 4.1 Dar Yerleşimde Okunabilirlik
- **Kompakt Ölçüler:** Widget yaklaşık 320–360 px genişliğinde kompakt bir panel olarak tasarlanır.
- **Etiket ve Veri Ayrımı:**
  - Grup başlıkları net ve kısa tutulur (`ChatGPT (Work/Codex)`, `Antigravity (Gemini)`).
  - Her kota satırında dört temel bileşen yatay eksende hiyerarşik hizalanır:  
    `[Pencere Etiketi]` (örn. `5 Saatlik`) → `[İlerleme Çubuğu]` → `[Kalan Yüzde]` (örn. `%84`) → `[Kalan Süre]` (örn. `4s 26dk`).

### 4.2 Yalnızca Renkle Ayırt Etmeme Kuralı (Erişilebilirlik)
- Renk körlüğü veya farklı monitör kontrastlarında bilgi kaybını önlemek için doluluk veya kritik durumlar **asla yalnızca renklerle (kırmızı/sarı/yeşil) kodlanmaz**.
- **Çok Boyutlu Gösterim:**
  - Her barda net sayısal metin (`%74`, `%96`, `%100`) kalın (bold) yazı tipiyle yer alır.
  - İlerleme çubuğunun doluluk genişliği fiziksel oranı gösterir.
  - Kritik seviyelerde sayısal metnin yanına semantik metinsel/ikonik işaretler (ör. düşük kota uyarısı için `!`, normal durum için nötr gösterim) eklenir.

---

## 5. Veri Güncelliği, Yenileme ve Hata Davranışı

### 5.1 Yaşam Döngüsü ve Yenileme Akışı
1. **Açılışta Otomatik Çekim:** Widget başlatıldığında ilk veri çekimi otomatik olarak tetiklenir.
2. **Kullanım Sırasında Periyodik Yenileme:** Widget açık kaldığı sürece arka planda periyodik olarak otomatik yenileme yapılır.
3. **Elle Yenileme:** Kullanıcı başlık çubuğundaki `[⟳]` butonuna tıklayarak veya kısayol ile beklemeden anlık yenileme başlatabilir.
4. **Zaman Damgası Şeffaflığı:** Başlık alanında son başarılı veri çekim anı açıkça belirtilir (örn. `"Son Kontrol: 17:05"`).
- *Not: Kesin otomatik yenileme aralığı (ör. 5 dk, 10 dk) ve başarısız denemelerdeki geri çekilme (exponential backoff) politikası T02 teknik mimarisine bırakılmıştır.*

### 5.2 Hata Sonrası Eski Veri (Stale Data) Gösterimi
- Geçici ağ hatası veya arka uç yanıt vermeme durumunda ekrandaki mevcut veriler **hemen silinmez veya boşaltılmaz**.
- Son başarılı okumanın yüzdeleri ekranda korunur; ancak verinin eski olduğu görsel bir işaretçi ve açıklamayla (örn. kehribar renkli uyarı noktası ve `"Eski Veri - Son başarılı: 16:45"`) şeffaf biçimde belirtilir.

### 5.3 %100 Doluluk ve Sıfırlanma Gösterimi
- Kota %100 dolu olduğunda sağlayıcı açıklama metni dönmese dahi, yanıtta mutlak bir sıfırlanma zaman damgası varsa sayaç gizlenmez.
- Arayüzde kalan yüzde `%100` olarak yazılırken, döngü sıfırlanma tarihi yerel saate çevrilerek kalan süreyle birlikte (örn. `Sıfırlanma: 4s 59dk` veya `Döngü: 6g 23s sonra`) açıkça gösterilir.

---

## 6. Durumlar ve Görsel Geri Bildirimler

| Durum | Görsel İşaret / Stil | Gösterge ve Davranış |
| :--- | :--- | :--- |
| **Normal / Güncel (Fresh)** | Nötr/tema rengi barlar, net bold yüzdeler | Veriler güncel, yerel geri sayım sayacı akıyor. |
| **Yenileniyor (Loading)** | Başlık butonunda hafif animasyon | Arka planda sorgu yapılıyor; mevcut veriler ekranda kalır, UI donmaz. |
| **Eski Veri (Stale)** | Kehribar rengi uyarı rozeti / ikonu | Son başarılı değerler korunur; yanına son başarılı kontrol saati eklenir. |
| **Oturum Eksik (Auth Required)** | İlgili grupta turuncu durum etiketi | "Oturum Gerekli: `codex login` yapın" veya "Antigravity oturumu yok". |
| **CLI Eksik (Missing Dep)** | İlgili grupta belirgin uyarı kutusu | "Bağımlılık Bulunamadı: `codex` veya `agy` sistemde tespit edilemedi". |
| **Okuma Hatası (Error)** | Hata göstergesi, son kontrol saati | "Arka uç yanıt vermedi (Son başarılı veriler korunuyor)". |

---

## 7. Kullanıcı Etkileşimi (Interaction Design)

1. **Sürükle ve Bırak (Drag & Drop):** Kullanıcı widget başlığından veya panel boşluklarından tutarak widget'ı masaüstünde serbestçe taşıyabilir. Bırakıldığında yeni konum kaydedilir.
2. **Elle Yenileme:** Başlıktaki `[⟳]` butonuna basıldığında beklemedeki periyot sıfırlanır ve anlık taze veri istenir.
3. **Bağlam Menüsü (Sağ Tık):**
   - `Şimdi Yenile`
   - `Monitöre Taşı` (`Monitör 1` / `Monitör 2`)
   - `Varsayılan Konuma Sıfırla`
   - `Opaklık` (`%100`, `%80`, `%60`)
   - `Çıkış`

---

## 8. Kabul Ölçütleri (Acceptance Criteria)

- [x] **AC-01 (Masaüstü Katmanı ve Odak):** Widget normal pencerelerin arkasında kalır; pencerelerin önüne fırlamaz ve kendiliğinden sistem odağını çalmaz.
- [x] **AC-02 (Çoklu Monitör ve Çalışma Alanı):** *(Doğrulandı — Kanıt Türü: Kullanıcının manuel test bildirimi)* Widget'ın iki monitör arasında serbestçe taşınması, ikinci monitöre bırakılıp kapatıldıktan sonra yeniden açıldığında konumunu koruması, çözünürlük veya DPI ölçek değişimi sonrasında ekran çalışma alanı sınırlarında kalması ve varsayılan konuma sıfırlanabilmesi kullanıcının fiziksel iki monitörlü cihazındaki manuel test bildirimiyle doğrulandı (ayrıca xUnit sınır denetim testleri ve `WM_DISPLAYCHANGE`/`WM_DPICHANGED` mesaj kancaları ile desteklenmiştir).
- [x] **AC-03 (4 Kota Alanı):** 2 ana grupta toplam 4 kota penceresi (2 adet 5 saatlik, 2 adet haftalık) bağımsız olarak listelenir. *(T05 nihai kararıyla 3. kart olan Claude/GPT arayüzden çıkarılmıştır; 2 kart/4 kota nihai kapsamdır).*
- [ ] **AC-04 (Veri Dürüstlüğü ve Canlı Sayaç):** *(Kısmi / Doğrulanan ve Bekleyen Ayrıldı)*
  - *Doğrulanan (Kod ve Test Kapsamında):* Yüzde değerinin sağlayıcı yanıtı dışında yapay olarak eksiltilmemesi/değiştirilmemesi, sıfırlanma zamanına kalan sayacın yerel saat üzerinden dinamik geri sayması, sıfırlanma eşiğinde tek sorgu ve üstel geri çekilme takvimi birim testleri, kod incelemesi ve yaklaşık 4 saat 40 dakikalık deterministik sanal saat simülasyonuyla doğrulandı.
  - *Bekleyen / Teyit Edilmemiş:* Fiziksel Windows cihazında saatler süren uzun süreli gerçek gözlem ve başlangıç/son kota kayıtları henüz ayrıca teyit edilmedi.
- [x] **AC-05 (Ekran Bağımsızlığı):** Kullanıcının ChatGPT web sekmesini veya Antigravity `Models & Usage` ekranını açık tutması gerekmez. Her iki servis için de kota verileri bu ekranlar kapalıyken arka plandaki araçlar üzerinden okunur ve gösterilir. *(Not: Antigravity masaüstü uygulamasının sistemde tamamen kapalı olması durumu bu aşamada garanti edilmez; doğrulanmış koşul Models & Usage ekranının kapalı olmasıdır).*
- [x] **AC-06 (Erişilebilir Okunabilirlik):** Kota dolulukları ve durumlar yalnızca renkle değil, sayısal değerler (`%XX`), kalın metinler ve açık etiketlerle sunulur.

---

## 9. Manuel Görünüm ve Davranış Test Senaryoları

### Test 1: İlk Açılış ve Konumlandırma
1. Widget'ı başlatın.
2. Başlık çubuğundan tutarak Monitör 1'in sağ alt köşesine sürükleyin.
3. Widget'ı kapatıp yeniden açın.
4. **Beklenen Sonuç:** Widget Monitör 1'deki koordinatlarında açılmalıdır.

### Test 2: İkinci Monitöre Taşıma ve Ölçek Değişimi
1. Widget'ı Monitör 2'ye sürükleyin ve bırakın.
2. Widget'ı kapatın, ekran çözünürlüğünü/ölçeğini değiştirip widget'ı yeniden başlatın.
3. **Beklenen Sonuç:** Widget Monitör 2 üzerinde, ekran dışına taşmadan görünür çalışma alanında açılmalıdır.

### Test 3: Normal Pencerelerin Arkasında Kalma
1. Widget'ın üzerine denk gelecek şekilde büyük bir tarayıcı veya IDE penceresi açın.
2. Pencereler arasında geçiş yapın, kod yazın, tıklayın.
3. **Beklenen Sonuç:** Widget asla aktif pencerelerin önüne geçmemeli; masaüstüne dönüldüğünde (`Win + D` veya pencereler simge durumuna alındığında) görünür kalmalıdır.

### Test 4: Kullanım Ekranları Kapalıyken Elle Yenileme
1. Tarayıcıda ChatGPT web sekmesini ve Antigravity uygulamasındaki `Models & Usage` ekranını kapatın *(Antigravity masaüstü uygulaması normal çalışır durumda kalabilir)*.
2. Widget üzerindeki `[⟳]` butonuna basın.
3. **Beklenen Sonuç:** Yükleme göstergesi belirmeli, ardından hiçbir kullanım ekranı açılmadan 4 kotanın (ChatGPT 5h/haftalık, Antigravity Gemini 5h/haftalık) tamamı güncel değerleriyle listelenmelidir.

### Test 5: %100 Doluluk ve Erişilebilirlik Görünümü
1. Kotalardan biri %100 iken kart görünümünü inceleyin.
2. **Beklenen Sonuç:** Bar tam dolu olmalı, açıkça `%100` sayısal metni yer almalı; sağlayıcı metni boş olsa bile sıfırlanma döngüsü yerel saat/kalan süre olarak okunabilmelidir. Bilgi yalnızca renkle değil, metin ve bar genişliğiyle anlaşılmalıdır.
