# UsageGauge

Windows masaüstünde **ChatGPT (Work/Codex)** ve **Google Antigravity (Gemini)** kotalarını odak çalmadan, hafif ve şeffaf bir widget üzerinde gerçek zamanlı takip etmeyi sağlayan modern, taşınabilir (.NET 10 WPF) masaüstü aracı.

---

## 🚀 Genel Bakış ve Özellikler

- **2 Havuz / 4 Kota Penceresi (2 Kart):**
  1. **ChatGPT (Work/Codex):** 5 Saatlik Pencere ve Haftalık Pencere.
  2. **Antigravity (Gemini):** 5 Saatlik Pencere ve Haftalık Pencere.
- **Odak Çalmayan Dinamik Z-Order Masaüstü Katmanı:** Normal pencerelerin (`Chrome`, `VS Code` vb.) arkasında kalır; pencerelerin önüne fırlamaz. Masaüstü katmanı ve pencere geçişleri WispDock modelinden uyarlanan dinamik Z-order kancası (`SetWinEventHook`, `WS_EX_NOACTIVATE`, normal uygulamalarda HWND_NOTOPMOST ve aktif pencerenin arkasına yerleştirme, masaüstüne dönüldüğünde `HWND_TOPMOST`) ile yönetilir.
- **Masaüstünü Göster (`Win + D`) Koruması:** `Win + D` kısayoluyla masaüstüne dönüldüğünde simge durumuna küçülmez (`SC_MINIMIZE` engellemesi); masaüstü yüzeyinde görünür kalır.
- **Çoklu Monitör ve DPI Desteği:** Monitörler arası sürükleme, DPI ölçekleme, birincil ekran değişimi ve çalışma alanı sınırları korunumu.
- **Güvenli ve Bağımsız Veri Sağlayıcıları:**
  - Token harcamayan resmi yerel CLI sorguları:
    - ChatGPT: `codex app-server --stdio` (JSON-RPC 2.0 `account/rateLimits/read`)
    - Antigravity: `agy -p /usage --output-format json --print-timeout 10s`
  - Sağlayıcılar birbirinden bağımsız çalışır; biri eksik veya hatalı olsa dahi diğeri güncellenmeye devam eder.
  - Asla kullanıcı parolası, token veya API anahtarı saklanmaz veya talep edilmez.
- **Periyodik Sorgu ve Yerel Geri Sayım Mimarisi:**
  - **Kota Yüzdeleri:** Sağlayıcıların periyodik sorgularıyla (varsayılan 10 dakikada bir veya elle "Şimdi Yenile" ile) güncellenir; sorgular arasında yapay/tahmini yüzde değişimi yapılmaz.
  - **Geri Sayım Sayaçları:** Sağlayıcıdan gelen UTC sıfırlanma zamanı (`DateTimeOffset`) referans alınarak yerel zamanlayıcı ile saniyelik/dakikalık olarak gerçek zamanlı güncellenir.
  - Hata anında 10-15-20-30 dk üstel geri çekilme takvimi işletilir.
- **Sağ Tık Menüsü:** Anlık "Şimdi Yenile", opaklık ayarı (%100, %80, %60), monitöre taşıma, varsayılan konuma sıfırlama ve çıkış.

---

## 📦 Son Kullanıcı İçin Kurulum ve Çalıştırma

UsageGauge kurulum gerektirmeyen taşınabilir (portable) bir uygulamadır:

1. Dağıtım arşivini indirin: `dist\UsageGauge-v1.0-win-x64.zip`
2. Arşivi bilgisayarınızda dilediğiniz bir klasöre çıkartın.
3. `UsageGauge.exe` dosyasına çift tıklayarak çalıştırın.

Ayrıntılı son kullanıcı rehberi ve sorun giderme adımları için [docs/DEPLOYMENT-GUIDE.md](docs/DEPLOYMENT-GUIDE.md) belgesini inceleyebilirsiniz.

### Sistem Gereksinimleri
- **İşletim Sistemi:** Windows 10 (1903+) veya Windows 11 (64-bit / x64).
- **Çalışma Zamanı:** .NET 10 çalışma zamanı pakete gömülüdür (**self-contained**); harici .NET kurulumu gerektirmez.
- **Dış Araçlar (CLI):**
  - ChatGPT için: [OpenAI Codex CLI](https://developers.openai.com/codex/cli) (`codex.exe`) ve `codex login`.
  - Antigravity için: [Google Antigravity CLI](https://antigravity.google/docs) (`agy.exe`) ve yetkilendirilmiş oturum.

---

## 🛠️ Geliştiriciler İçin Derleme ve Test

Projeyi derlemek ve testlerini çalıştırmak için sistemde .NET 10 SDK bulunmalıdır.

### Çözüm Derlemesi (Release)
```powershell
dotnet build UsageGauge.slnx -c Release
```
*(Hedef: 0 Hata, 0 Uyarı)*

### Birim ve Entegrasyon Testleri
```powershell
dotnet test UsageGauge.slnx -c Release --filter "Category!=Live"
```
*(Hedef: 87 Geçen Test, 0 Hata, 0 Atlanan)*

### Dağıtım Paketi Oluşturma (Publish)
```powershell
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
```
Bu betik:
- .NET 10 self-contained tek dosya paketini izole `%TEMP%` staging dizininde derler.
- Güvenlik denetimlerini uygular (PDB, harici CLI, log veya config sızdırmazlığı).
- Aday ZIP oluşturup doğrular ve Win32 `ReplaceFile` API'si ile hedef `dist\UsageGauge-v1.0-win-x64.zip` dosyasını atomik olarak günceller.
- Kullanıcı yapılandırmalarını ve loglarını korur.

---

## 📚 Proje Belgeleri ve Yol Haritası

- [docs/DEPLOYMENT-GUIDE.md](docs/DEPLOYMENT-GUIDE.md): Dağıtım, yapılandırma ve son kullanıcı kullanım kılavuzu.
- [docs/UX-DEFINITION-v1.0.md](docs/UX-DEFINITION-v1.0.md): Ürün vizyonu, ekran tasarımı ve kabul kriterleri (AC-01..AC-06).
- [docs/ARCHITECTURE-PLAN-v1.0.md](docs/ARCHITECTURE-PLAN-v1.0.md): Mimari tasarım, teknik kararlar ve T01–T09 aşamaları yol haritası.
- [docs/T07-DEVICE-ACCEPTANCE.md](docs/T07-DEVICE-ACCEPTANCE.md): Fiziksel Windows cihaz kabul testleri ve sanal saat simülasyonu kayıtları.
- [docs/T08-RELEASE-CANDIDATE.md](docs/T08-RELEASE-CANDIDATE.md): Paketleme güvenilirliği, hata yolları denetimi ve Release Candidate kayıtları.
- [docs/T09-FINAL-HANDOFF.md](docs/T09-FINAL-HANDOFF.md): Nihai teslimat özeti, teknik kabul ve son kullanıcı kapanış adımları.
