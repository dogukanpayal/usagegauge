# UsageGauge Dağıtım ve Kullanım Kılavuzu

UsageGauge, geliştiricilerin ChatGPT (Work/Codex) ve Google Antigravity (Gemini) kullanım kotalarını Windows masaüstünde şeffaf, hafif ve odak çalmayan bir widget üzerinde gerçek zamanlı takip etmelerini sağlayan taşınabilir (portable) bir masaüstü aracıdır.

---

## 1. Sistem Gereksinimleri

- **İşletim Sistemi:** Windows 10 (Sürüm 1903 veya üzeri) ya da Windows 11 (64-bit / x64).
- **.NET Çalışma Zamanı:** Gerekli tüm .NET 10 çalışma zamanı bileşenleri ve WPF kitaplıkları paketin içine gömülüdür (**self-contained**). Sisteme harici .NET SDK veya .NET Runtime kurmanıza gerek yoktur.
- **Yönetici Hakları:** Yönetici (Administrator) yetkisi gerektirmez; standart kullanıcı haklarıyla çalışır.

---

## 2. Kurulum ve İlk Çalıştırma

UsageGauge geleneksel bir Windows kurulum sihirbazı (MSI/Installer) gerektirmez; tamamen taşınabilir (portable) olarak tasarlanmıştır.

1. `UsageGauge-v1.0-win-x64.zip` arşivini bilgisayarınızda dilediğiniz bir dizine çıkartın (örneğin: `C:\Tools\UsageGauge` veya kullanıcı klasörünüz).
   - **Paket İçeriği:** Paket son kullanıcı için hazırlanmış olup yalnızca `UsageGauge.exe` (tüm çalışma zamanını içeren tek dosya) ve `README.txt` kılavuzunu barındırır. Geliştiriciye özgü `.pdb` sembolleri veya harici DLL kalabalığı içermez.
2. Klasör içerisindeki `UsageGauge.exe` dosyasına çift tıklayarak uygulamayı başlatın.
3. Widget masaüstünüzde sağ alt köşede veya daha önce kaydettiğiniz konumda belirir.

> [!TIP]
> Windows her açıldığında UsageGauge'ın otomatik başlamasını isterseniz, `UsageGauge.exe` için bir kısayol oluşturup `Win + R` -> `shell:startup` ile açılan Başlangıç klasörüne yapıştırabilirsiniz.

---

## 3. CLI Bağımlılıkları ve Oturum Kurulumu

UsageGauge, güvenlik ve gizlilik ilkeleri gereğince üçüncü taraf CLI ikililerini (`codex.exe`, `agy.exe`) veya kullanıcı şifrelerini paket içinde **barındırmaz**. Kotaları okumak için sisteminizde kurulu olan resmi araçları kullanır.

### A. ChatGPT Work / Codex Kotası
- **Gereken Araç:** OpenAI Codex CLI (`codex.exe`).
- **Konum:** Sistem `PATH` değişkeninde veya standart konumlarda (`%LOCALAPPDATA%\Programs\codex\codex.exe`, WinGet paketleri veya `%USERPROFILE%\.codex\bin\codex.exe`) aranır.
- **Oturum Açma:** Terminalde bir kez oturum açmanız yeterlidir:
  ```powershell
  codex login
  ```
- **Hata Durumları:**
  - CLI yüklü değilse: Kart üzerinde `✕ CLI Bulunamadı` durumu gösterilir.
  - Oturum açılmamışsa: Kart üzerinde `🔑 Oturum Gerekli` durumu gösterilir.

### B. Antigravity Gemini Kotası
- **Gereken Araç:** Google Antigravity CLI (`agy.exe`).
- **Konum:** Sistem `PATH` değişkeninde veya standart Antigravity kurulum dizinlerinde (`%LOCALAPPDATA%\agy\bin\agy.exe`, `%USERPROFILE%\.gemini\antigravity\bin\agy.exe`) aranır.
- **Oturum Açma:** Antigravity IDE üzerinden veya terminalden kimlik doğrulamasının tamamlanmış olması gerekir.
- **Hata Durumları:**
  - `agy` bulunamazsa: `✕ CLI Bulunamadı` durumu gösterilir.
  - Oturum geçersizse: `🔑 Oturum Gerekli` durumu gösterilir.

> [!NOTE]
> Her iki sağlayıcı da birbirinden tamamen yalıtılmıştır. Araçlardan birisi kurulu olmasa veya hata verse dahi diğer kart kotalarını normal şekilde okumaya ve güncellemeye devam eder.

---

## 4. Kullanım ve Masaüstü Entegrasyonu

- **İki Kart ve Dört Kota:**
  - **ChatGPT (Work/Codex):** 5 Saatlik Havuz ve Haftalık Havuz.
  - **Antigravity (Gemini):** 5 Saatlik Havuz ve Haftalık Havuz.
- **Masaüstü Katmanı:** Widget, arka planda masaüstü katmanında durur. Normal pencerelerin (`Chrome`, `VS Code` vb.) arkasında kalır; pencerelerin önüne fırlamaz.
- **Win + D (Masaüstünü Göster) Koruması:** `Win + D` kısayoluyla masaüstüne dönüldüğünde simge durumuna küçülmez; masaüstüyle birlikte görünür kalır.
- **Sürükle ve Bırak:** Üst başlık çubuğundan tutarak widget'ı masaüstünüzde istediğiniz konuma veya harici bir monitöre taşıyabilirsiniz. Tıklama veya taşıma sırasında diğer uygulamaların metin/klavye odağı bozulmaz.
- **Sağ Tık Menüsü:**
  - **Şimdi Yenile:** Bekleme süresini beklemeden anlık veri sorgusu tetikler.
  - **Monitöre Taşı:** Çoklu monitör kurulumlarında widget'ı doğrudan hedef ekrana taşır.
  - **Varsayılan Konuma Sıfırla:** Widget'ı ana ekranın sağ alt güvenli çalışma alanına çeker.
  - **Opaklık:** `%100`, `%80` veya `%60` saydamlık seviyeleri arasında geçiş yapar.
  - **Çıkış:** Widget'ı temiz şekilde kapatır.

---

## 5. Yapılandırma ve Gizlilik

- **Yapılandırma Dosyası:** `%APPDATA%\UsageGauge\config.json`
- **Saklanan Veriler:** Yalnızca pencere koordinatları (`WindowLeft`, `WindowTop`), monitör adı (`MonitorDeviceName`) ve saydamlık (`Opacity`).
- **Gizlilik Güvencesi:**
  - Asla kullanıcı şifresi, çerez, erişim jetonu (access token) veya API anahtarı okunmaz ve saklanmaz.
  - Dağıtım günlüklerinde (`usagegauge-diagnostics.log`) yalnızca pencere tanıtıcıları (HWND), boyutları ve genel hata durumları tutulur; hiçbir kişisel veri veya ham sorgu çıktısı günlüğe yazılmaz.
  - Model turu veya token harcayan komutlar kesinlikle çalıştırılmaz.

---

## 6. Güncelleme ve Kaldırma (Uninstall)

- **Güncelleme:**
  1. Çalışan UsageGauge uygulamasını sağ tık menüsünden `Çıkış` seçerek kapatın.
  2. Yeni sürümdeki `UsageGauge.exe` dosyasını mevcut klasöre kopyalayın.
  3. Uygulamayı yeniden başlatın. `%APPDATA%\UsageGauge\config.json` içerisindeki konum ve görünüm tercihleriniz aynen korunur.
- **Kaldırma:**
  1. Uygulamadan çıkış yapın.
  2. `UsageGauge` klasörünü doğrudan silin.
  3. İsteğe bağlı olarak yapılandırma dosyasını silmek için:
     ```powershell
     Remove-Item -Recurse -Force "$env:APPDATA\UsageGauge"
     ```
  4. Kayıt defterinde (Registry) hiçbir girdi veya artık kalmaz.
