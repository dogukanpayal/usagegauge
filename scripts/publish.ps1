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
    [string]$OutputDir = "dist"
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

    $publishProcess = Start-Process -FilePath "dotnet" -ArgumentList $publishArgs -NoNewWindow -Wait -PassThru
    if ($publishProcess.ExitCode -ne 0) {
        throw "dotnet publish başarısız oldu! Çıkış kodu: $($publishProcess.ExitCode)"
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

    # 4. ZIP Arşivini Oluştur (Yalnızca hedef ZIP dosyasını yeniler, diğer dosyaları asla silmez)
    $zipName = "UsageGauge-v1.0-$Runtime.zip"
    $zipPath = Join-Path $targetDist $zipName

    if (Test-Path $zipPath) {
        Remove-Item -Force $zipPath
    }

    Write-Host "[*] ZIP arşivi sıkıştırılıyor: $zipPath..." -ForegroundColor Green
    Compress-Archive -Path "$tempStagingDir\*" -DestinationPath $zipPath -CompressionLevel Optimal -Force

    # 5. İsteğe bağlı olarak ikilileri hedef çıktı klasörüne de kopyala (mevcut dosyalara/loglara zarar vermeden)
    $finalBinDir = if ($SingleFile) {
        Join-Path $targetDist "UsageGauge-singlefile"
    } else {
        Join-Path $targetDist "UsageGauge-win-x64"
    }

    if (-not (Test-Path $finalBinDir)) {
        New-Item -ItemType Directory -Path $finalBinDir -Force | Out-Null
    }

    # Dosyaları hedef klasöre güvenle kopyala (mevcut log veya başka dosyaları SİLMEDEN)
    Copy-Item -Path "$tempStagingDir\*" -Destination $finalBinDir -Force

    $zipItem = Get-Item $zipPath
    $zipSizeMb = [Math]::Round($zipItem.Length / 1MB, 2)
    $sha256 = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash

    Write-Host "==========================================================" -ForegroundColor Cyan
    Write-Host " Paketleme Başarıyla Tamamlandı!" -ForegroundColor Green
    Write-Host " Paket Yolu : $zipPath" -ForegroundColor White
    Write-Host " Paket Boyut: $zipSizeMb MB ($($zipItem.Length) bayt)" -ForegroundColor White
    Write-Host " SHA256     : $sha256" -ForegroundColor White
    Write-Host "==========================================================" -ForegroundColor Cyan
}
finally {
    # Geçici staging dizinini temizle
    if (Test-Path $tempStagingDir) {
        Remove-Item -Recurse -Force $tempStagingDir -ErrorAction SilentlyContinue
    }
}
