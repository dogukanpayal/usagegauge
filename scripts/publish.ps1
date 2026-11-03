<#
.SYNOPSIS
    UsageGauge taşınabilir (portable) Windows x64 Release paketleme betiği.

.DESCRIPTION
    Bu betik, .NET 10 WPF UsageGauge uygulamasını bağımsız (.NET çalışma zamanını içeren - self-contained)
    taşınabilir bir paket olarak derler ve son kullanıcı dağıtım ZIP arşivi oluşturur.
    Paketleme işlemi izole bir geçici dizinde (temp staging) yapılır; hedef dizindeki (OutputDir)
    mevcut hiçbir dosya, log veya yapılandırma silinmez ya da bozulmaz.
    Paket içerisine üçüncü taraf ikililer (codex.exe, agy.exe), eski loglar veya kimlik bilgileri gömülmez.
    Son kullanıcı paketinde gereksiz PDB hata ayıklama sembolleri bulunmaz; yalnızca UsageGauge.exe ve README.txt yer alır.

.PARAMETER Configuration
    Derleme yapılandırması (varsayılan: Release).

.PARAMETER Runtime
    Hedef çalışma ortamı (varsayılan: win-x64).

.PARAMETER SingleFile
    Tek dosya (PublishSingleFile) yürütülebilir paketi üretilsin mi? (varsayılan: $true).

.PARAMETER OutputDir
    Çıktı dizini (varsayılan: dist).

.EXAMPLE
    .\scripts\publish.ps1
    .\scripts\publish.ps1 -OutputDir "C:\CustomDist"
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [bool]$SingleFile = $true,
    [string]$OutputDir = "dist",
    [switch]$SimulatePreReplaceFailure = $false
)

$ErrorActionPreference = "Stop"

$scriptRoot = $PSScriptRoot
$projectRoot = Split-Path -Parent $scriptRoot
Set-Location $projectRoot

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " UsageGauge Portable Windows Package Publisher" -ForegroundColor Cyan
Write-Host " Configuration: $Configuration | Runtime: $Runtime | SingleFile: $SingleFile" -ForegroundColor Cyan
Write-Host " Output Directory: $OutputDir" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$csprojPath = Join-Path $projectRoot "src\UsageGauge\UsageGauge.csproj"
if (-not (Test-Path $csprojPath)) {
    throw "Proje dosyası bulunamadı: $csprojPath"
}

# Çıktı dizinini mutlak yola dönüştür
$targetDist = if ([System.IO.Path]::IsPathRooted($OutputDir)) {
    $OutputDir
} else {
    Join-Path $projectRoot $OutputDir
}

if (-not (Test-Path $targetDist)) {
    New-Item -ItemType Directory -Path $targetDist -Force | Out-Null
}

# Güvenli ve izole geçici staging dizini oluştur (%TEMP% altında)
$tempStagingDir = $null
$candidateZipPath = $null
$backupZipPath = $null
$replaceAttempted = $false
$replaceSucceeded = $false

$tempStagingDir = Join-Path ([System.IO.Path]::GetTempPath()) ("UsageGauge_Staging_" + [System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempStagingDir -Force | Out-Null

try {
    Write-Host "[*] İzole geçici derleme dizini: $tempStagingDir" -ForegroundColor Gray
    Write-Host "[*] .NET self-contained Release derlemesi başlatılıyor..." -ForegroundColor Green

    $publishArgs = @(
        "publish",
        $csprojPath,
        "-c", $Configuration,
        "-r", $Runtime,
        "--self-contained", "true",
        "-o", $tempStagingDir
    )

    if ($SingleFile) {
        $publishArgs += @(
            "-p:PublishSingleFile=true",
            "-p:IncludeNativeLibrariesForSelfExtract=true",
            "-p:EnableCompressionInSingleFile=true"
        )
    }

    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish başarısız oldu! Çıkış kodu: $LASTEXITCODE"
    }

    # 1. Güvenlik ve Bağımlılık Denetimleri
    Write-Host "[*] Güvenlik ve temizlik denetimleri yapılıyor..." -ForegroundColor Green

    $forbiddenPatterns = @("codex.exe", "agy.exe", "config.json", "*.log")
    foreach ($pattern in $forbiddenPatterns) {
        $found = Get-ChildItem -Path $tempStagingDir -Recurse -Filter $pattern -ErrorAction SilentlyContinue
        if ($found) {
            throw "GÜVENLİK İHLALİ: Dağıtım paketinde yasaklı dosya bulundu: $($found.FullName)"
        }
    }

    $mainExe = Join-Path $tempStagingDir "UsageGauge.exe"
    if (-not (Test-Path $mainExe)) {
        throw "Dağıtım ikilisi bulunamadı: $mainExe"
    }

    # 2. Son Kullanıcı Paketinden PDB Sembollerini Çıkar
    $pdbFiles = Get-ChildItem -Path $tempStagingDir -Filter "*.pdb" -ErrorAction SilentlyContinue
    if ($pdbFiles) {
        foreach ($pdb in $pdbFiles) {
            Remove-Item -Force $pdb.FullName
            Write-Host "[-] Hata ayıklama sembolü paketten çıkarıldı: $($pdb.Name)" -ForegroundColor Gray
        }
    }

    # 3. Dağıtım / Kullanım Kılavuzunu Pakete Ekle
    $deploymentDoc = Join-Path $projectRoot "docs\DEPLOYMENT-GUIDE.md"
    $readmeTarget = Join-Path $tempStagingDir "README.txt"

    if (Test-Path $deploymentDoc) {
        Copy-Item $deploymentDoc -Destination $readmeTarget -Force
        Write-Host "[+] Dağıtım kılavuzu pakete eklendi: README.txt" -ForegroundColor Gray
    }

    # 4. Aday ZIP Arşivini Hedef Dosya Sisteminde Oluştur ve Doğrula (Fail-Safe Atomic Replace)
    $zipName = "UsageGauge-v1.0-$Runtime.zip"
    $zipPath = Join-Path $targetDist $zipName

    # Aday dosya, atomik Win32 ReplaceFile için MUTLAKA hedef ile aynı dosya sisteminde/dizinde olmalıdır
    $candidateZipPath = Join-Path $targetDist ($zipName + ".candidate." + [System.Guid]::NewGuid().ToString("N"))

    Write-Host "[*] Aday ZIP arşivi oluşturuluyor: $candidateZipPath..." -ForegroundColor Green
    [System.Reflection.Assembly]::LoadWithPartialName("System.IO.Compression.FileSystem") | Out-Null
    [System.IO.Compression.ZipFile]::CreateFromDirectory($tempStagingDir, $candidateZipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

    # ZIP bütünlüğü ve içerik doğrulama
    if (-not (Test-Path $candidateZipPath)) {
        throw "Aday ZIP arşivi oluşturulamadı: $candidateZipPath"
    }
    $candidateZipItem = Get-Item $candidateZipPath
    if ($candidateZipItem.Length -eq 0) {
        throw "Aday ZIP arşivi boş (0 bayt): $candidateZipPath"
    }

    Write-Host "[*] Aday ZIP arşivi bütünlüğü ve girdi denetimleri yapılıyor..." -ForegroundColor Green
    $zipArchive = [System.IO.Compression.ZipFile]::OpenRead($candidateZipPath)
    try {
        $entryNames = @($zipArchive.Entries | ForEach-Object { $_.FullName })
        if (-not ($entryNames -contains "UsageGauge.exe")) {
            throw "ZIP doğrulama hatası: 'UsageGauge.exe' arşive dahil edilmemiş!"
        }
        if (-not ($entryNames -contains "README.txt")) {
            throw "ZIP doğrulama hatası: 'README.txt' arşive dahil edilmemiş!"
        }
        $forbiddenZipEntries = $entryNames | Where-Object {
            $_ -like "*.pdb" -or $_ -like "*.log" -or $_ -like "config.json" -or $_ -like "*codex.exe*" -or $_ -like "*agy.exe*"
        }
        if ($forbiddenZipEntries) {
            throw "ZIP doğrulama hatası: Arşivde yasaklı dosya tespit edildi: $($forbiddenZipEntries -join ', ')"
        }
    }
    finally {
        $zipArchive.Dispose()
    }

    # Kontrollü test kancası (T08.1 hata sınama testi için)
    if ($SimulatePreReplaceFailure) {
        throw "KONTROLLÜ HATA TESTİ: Aday ZIP hazırlandı ve doğrulandı; hedef ZIP atomik olarak değiştirilmeden hemen önce kontrollü hata tetiklendi."
    }

    # Doğrulama başarılı: Hedef ZIP varsa Win32 ReplaceFile ile atomik değiştir; yoksa File::Move yap
    Write-Host "[*] Doğrulama başarılı. Hedef ZIP atomik olarak güncelleniyor: $zipPath" -ForegroundColor Green
    $replaceAttempted = $true

    if (Test-Path $zipPath) {
        $backupZipPath = Join-Path $targetDist ($zipName + ".backup." + [System.Guid]::NewGuid().ToString("N"))
        try {
            [System.IO.File]::Replace($candidateZipPath, $zipPath, $backupZipPath, $true)
            $replaceSucceeded = $true
            Write-Host "[+] Win32 ReplaceFile ile atomik değiştirme başarılı." -ForegroundColor Green

            # YALNIZCA değiştirme başarılı olduğu kesinleştiğinde yedek dosya güvenle temizlenir
            if (Test-Path $backupZipPath) {
                Remove-Item -Force $backupZipPath -ErrorAction SilentlyContinue
            }
        }
        catch {
            $replaceEx = $_
            # Hata durumu: Win32 ReplaceFileW (örn. ERROR_UNABLE_TO_MOVE_REPLACEMENT_2 / 1177)
            # eski hedef dosyayı $backupZipPath yoluna taşımış olabilir.
            # BU NEDENLE $backupZipPath KESİNLİKLE SİLİNMEZ!
            $targetStatus = if (Test-Path $zipPath) { "Mevcut ($( (Get-Item $zipPath).Length ) bayt)" } else { "YOK (Yedek yoluna taşınmış olabilir)" }
            $backupStatus = if (Test-Path $backupZipPath) { "MEVCUT VE KORUNDU ($( (Get-Item $backupZipPath).Length ) bayt) -> Manuel kurtarma için kullanılabilir!" } else { "Oluşturulmadı veya yok" }
            $candidateStatus = if (Test-Path $candidateZipPath) { "Mevcut ($( (Get-Item $candidateZipPath).Length ) bayt)" } else { "Yok" }

            Write-Host "==========================================================" -ForegroundColor Red
            Write-Host " KRİTİK DEĞİŞTİRME HATASI (Win32 File.Replace)" -ForegroundColor Red
            Write-Host " Hedef ZIP     : $zipPath [$targetStatus]" -ForegroundColor Yellow
            Write-Host " Yedek ZIP     : $backupZipPath [$backupStatus]" -ForegroundColor Yellow
            Write-Host " Aday ZIP      : $candidateZipPath [$candidateStatus]" -ForegroundColor Yellow
            Write-Host " Hata Detayı   : $($replaceEx.Exception.Message)" -ForegroundColor Red
            Write-Host "==========================================================" -ForegroundColor Red

            throw "Hedef ZIP atomik olarak değiştirilemedi! Orijinal dosyanın sağlam yedeği silinmemiştir ve korunmaktadır: $backupZipPath. Hata: $($replaceEx.Exception.Message)"
        }
    } else {
        Write-Host "[*] İlk kurulum: Aday ZIP hedef konuma taşınıyor..." -ForegroundColor Green
        try {
            [System.IO.File]::Move($candidateZipPath, $zipPath)
            $replaceSucceeded = $true
            Write-Host "[+] İlk oluşturma taşıması başarılı." -ForegroundColor Green
        }
        catch {
            $moveEx = $_
            $candidateStatus = if (Test-Path $candidateZipPath) { "Mevcut ($( (Get-Item $candidateZipPath).Length ) bayt)" } else { "Yok" }
            Write-Host "KRİTİK TAŞIMA HATASI: [System.IO.File]::Move başarısız oldu. Aday: $candidateZipPath [$candidateStatus]" -ForegroundColor Red
            throw "Aday ZIP hedefe taşınamadı! Aday dosya korunmuştur: $candidateZipPath. Hata: $($moveEx.Exception.Message)"
        }
    }

    # 5. İsteğe bağlı olarak ikilileri hedef çıktı klasörüne de kopyala (mevcut kullanıcı dosyalarına/loglara zarar vermeden)
    # DİKKAT: Bu adım klasör düzeyinde dosya kopyalamasıdır. Yalnızca tekil .zip arşivi Win32 ReplaceFile ile atomiktir;
    # hedef çıktı klasörü atomik değildir.
    $finalBinDir = if ($SingleFile) {
        Join-Path $targetDist "UsageGauge-singlefile"
    } else {
        Join-Path $targetDist "UsageGauge-win-x64"
    }

    try {
        if (-not (Test-Path $finalBinDir)) {
            New-Item -ItemType Directory -Path $finalBinDir -Force | Out-Null
        }

        # Yalnızca UsageGauge'a ait belirlenmiş yayın çıktılarını yenile; config.json, loglar ve kullanıcı dosyaları KESİNLİKLE korunur
        if ($SingleFile) {
            $exeSource = Join-Path $tempStagingDir "UsageGauge.exe"
            $readmeSource = Join-Path $tempStagingDir "README.txt"
            if (Test-Path $exeSource) {
                Copy-Item -Path $exeSource -Destination (Join-Path $finalBinDir "UsageGauge.exe") -Force
            }
            if (Test-Path $readmeSource) {
                Copy-Item -Path $readmeSource -Destination (Join-Path $finalBinDir "README.txt") -Force
            }
        } else {
            # Çok dosyalı yayında yalnızca staging alanındaki derleme çıktılarını kopyala
            Get-ChildItem -Path $tempStagingDir | ForEach-Object {
                Copy-Item -Path $_.FullName -Destination $finalBinDir -Recurse -Force
            }
        }
    }
    catch {
        $looseEx = $_
        Write-Host "==========================================================" -ForegroundColor Yellow
        Write-Host " UYARI: GEVŞEK ÇIKTI KLASÖRÜ GÜNCELLENEMEDİ" -ForegroundColor Yellow
        Write-Host " Dağıtım ZIP Arşivi : BAŞARILI VE GEÇERLİ ($zipPath)" -ForegroundColor Green
        Write-Host " Klasör Hedefi      : BAŞARISIZ ($finalBinDir)" -ForegroundColor Red
        Write-Host " Hata Detayı        : $($looseEx.Exception.Message)" -ForegroundColor Yellow
        Write-Host " Açıklama           : Dağıtım ZIP paketi atomik olarak başarıyla oluşturulmuştur ve geçerlidir. Ancak '$finalBinDir' klasöründeki dosyalar (muhtemelen açık/kullanımda olduğu için) güncellenememiştir." -ForegroundColor Gray
        Write-Host "==========================================================" -ForegroundColor Yellow
        throw "Dağıtım ZIP paketi ($zipPath) başarıyla atomik olarak güncellendi ve geçerlidir. Ancak '$finalBinDir' klasörüne gevşek dosya kopyalaması başarısız oldu: $($looseEx.Exception.Message)"
    }

    $zipItem = Get-Item $zipPath
    $zipSizeMb = [Math]::Round($zipItem.Length / 1MB, 2)
    $sha256 = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash

    Write-Host "==========================================================" -ForegroundColor Cyan
    Write-Host " Paketleme Başarıyla Tamamlandı!" -ForegroundColor Green
    Write-Host " Paket Yolu       : $zipPath" -ForegroundColor White
    Write-Host " Paket Boyut      : $zipSizeMb MB ($($zipItem.Length) bayt)" -ForegroundColor White
    Write-Host " SHA256           : $sha256" -ForegroundColor White
    Write-Host " Gevşek Klasör    : $finalBinDir (Güncellendi)" -ForegroundColor White
    Write-Host " Atomiklik Kapsamı: Yalnızca $zipName ZIP arşivi atomiktir (Win32 ReplaceFile); çıktı klasörünün tamamı atomik değildir." -ForegroundColor Gray
    Write-Host "==========================================================" -ForegroundColor Cyan
}
finally {
    # 1. Geçici staging dizinini temizle (her zaman güvenle silinebilir)
    if ($tempStagingDir -and (Test-Path $tempStagingDir)) {
        Remove-Item -Recurse -Force $tempStagingDir -ErrorAction SilentlyContinue
    }

    # 2. Yedek ZIP ($backupZipPath):
    # YALNIZCA değiştirme başarılı olmuşsa silinmesine izin verilir.
    # Değiştirme başarısız olduysa veya yarıda kaldıysa (örn. Win32 ERROR_UNABLE_TO_MOVE_REPLACEMENT_2 / 1177
    # hatasında eski hedef dosya yedek yoluna taşınmış olabileceğinden) KESİNLİKLE SİLİNMEZ!
    if ($replaceSucceeded) {
        if ($backupZipPath -and (Test-Path $backupZipPath)) {
            Remove-Item -Force $backupZipPath -ErrorAction SilentlyContinue
        }
    } else {
        if ($backupZipPath -and (Test-Path $backupZipPath)) {
            Write-Warning "Güvenlik Koruması: Değiştirme işlemi başarısız olduğundan veya tamamlanmadığından yedek ZIP silinmedi: $backupZipPath"
        }
    }

    # 3. Aday ZIP ($candidateZipPath):
    # - Eğer değiştirme teşebbüsünden önce hata oluştuysa (-not $replaceAttempted):
    #   Aday ZIP güvenle temizlenir (kontrollü hata testi veya erken doğrulama iptalinde artık dosya bırakılmaz).
    # - Eğer değiştirme başarılı olduysa ($replaceSucceeded):
    #   Aday ZIP zaten hedefe taşınmış/dönüşmüştür; geride kaldıysa temizlenir.
    # - ANCAK değiştirme denenip BAŞARISIZ olduysa ($replaceAttempted -and -not $replaceSucceeded):
    #   Aday ZIP adli hata analizi ve kurtarma için KESİNLİKLE SİLİNMEZ!
    if (-not $replaceAttempted) {
        if ($candidateZipPath -and (Test-Path $candidateZipPath)) {
            Remove-Item -Force $candidateZipPath -ErrorAction SilentlyContinue
        }
    } elseif ($replaceSucceeded) {
        if ($candidateZipPath -and (Test-Path $candidateZipPath)) {
            Remove-Item -Force $candidateZipPath -ErrorAction SilentlyContinue
        }
    } else {
        if ($candidateZipPath -and (Test-Path $candidateZipPath)) {
            Write-Warning "Güvenlik Koruması: Değiştirme başarısız olduğundan aday ZIP silinmedi: $candidateZipPath"
        }
    }
}
