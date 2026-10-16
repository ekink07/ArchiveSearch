<#
  Arşiv Ara kurulum betiği.

  Kurulum (tek satır):
    irm https://raw.githubusercontent.com/YOUR_GITHUB_USER/ArsivAra/main/install.ps1 | iex

  Seçeneklerle (masaüstü kısayolu / kaldırma):
    & ([scriptblock]::Create((irm https://raw.githubusercontent.com/YOUR_GITHUB_USER/ArsivAra/main/install.ps1))) -Desktop
    & ([scriptblock]::Create((irm https://raw.githubusercontent.com/YOUR_GITHUB_USER/ArsivAra/main/install.ps1))) -Uninstall

  Son sürümü GitHub'dan indirir, SHA256 ile doğrular, %LOCALAPPDATA%\Programs\ArsivAra içine koyar
  ve Başlat menüsüne kısayol ekler. Yönetici yetkisi gerekmez.
#>
param(
    [switch]$Desktop,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$Repo       = 'YOUR_GITHUB_USER/ArsivAra'
$InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\ArsivAra'
$Exe        = Join-Path $InstallDir 'ArsivAra.exe'
$StartMenu  = Join-Path ([Environment]::GetFolderPath('Programs')) 'Arşiv Ara.lnk'
$DesktopLnk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Arşiv Ara.lnk'

function New-Shortcut([string]$Path) {
    $shell = New-Object -ComObject WScript.Shell
    $lnk = $shell.CreateShortcut($Path)
    $lnk.TargetPath = $Exe
    $lnk.WorkingDirectory = $InstallDir
    $lnk.Description = 'ZIP, RAR, 7z ve TAR arşivlerinin içinde arama'
    $lnk.Save()
}

if ($Uninstall) {
    Get-Process ArsivAra -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Exe } | Stop-Process -Force
    foreach ($p in $StartMenu, $DesktopLnk) { if (Test-Path $p) { Remove-Item $p -Force } }
    if (Test-Path $InstallDir) { Remove-Item $InstallDir -Recurse -Force }
    Write-Host 'Arşiv Ara kaldırıldı.' -ForegroundColor Green
    return
}

# Windows PowerShell 5.1 varsayılan olarak eski TLS kullanabilir.
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

Write-Host 'Son sürüm aranıyor…' -ForegroundColor Cyan
$release = Invoke-RestMethod "https://api.github.com/repos/$Repo/releases/latest" -Headers @{ 'User-Agent' = 'ArsivAra-installer' }
$asset = $release.assets | Where-Object name -eq 'ArsivAra.exe' | Select-Object -First 1
if (-not $asset) { throw "Sürüm $($release.tag_name) içinde ArsivAra.exe bulunamadı." }
$hashAsset = $release.assets | Where-Object name -eq 'ArsivAra.exe.sha256' | Select-Object -First 1

$tmp = Join-Path ([IO.Path]::GetTempPath()) "ArsivAra-$([guid]::NewGuid().ToString('N')).exe"
try {
    Write-Host "İndiriliyor: $($release.tag_name) ($([math]::Round($asset.size / 1MB, 1)) MB)" -ForegroundColor Cyan
    Invoke-WebRequest $asset.browser_download_url -OutFile $tmp -UseBasicParsing

    if ($hashAsset) {
        $expected = ((Invoke-RestMethod $hashAsset.browser_download_url -Headers @{ 'User-Agent' = 'ArsivAra-installer' }) -split '\s+')[0].Trim()
        $actual = (Get-FileHash $tmp -Algorithm SHA256).Hash
        if ($actual -ne $expected) { throw "SHA256 eşleşmedi. Beklenen $expected, inen $actual. Kurulum durduruldu." }
        Write-Host 'SHA256 doğrulandı.' -ForegroundColor DarkGray
    }

    Get-Process ArsivAra -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Exe } | Stop-Process -Force
    New-Item -ItemType Directory -Force $InstallDir | Out-Null
    Move-Item $tmp $Exe -Force
}
finally {
    if (Test-Path $tmp) { Remove-Item $tmp -Force }
}

New-Shortcut $StartMenu
if ($Desktop) { New-Shortcut $DesktopLnk }

Write-Host ''
Write-Host "Arşiv Ara $($release.tag_name) kuruldu: $Exe" -ForegroundColor Green
Write-Host 'Başlat menüsünde "Arşiv Ara" olarak bulabilirsiniz.'
Write-Host 'Created By MaledictusMaggot®' -ForegroundColor DarkGray
Start-Process $Exe
