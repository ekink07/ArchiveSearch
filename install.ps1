<#
  Arsiv Ara kurulum betigi.

  Kurulum (tek satir):
    irm https://raw.githubusercontent.com/YOUR_GITHUB_USER/ArsivAra/main/install.ps1 | iex

  Seceneklerle (masaustu kisayolu / kaldirma / ayarlar ve kutuphaneyle birlikte kaldirma):
    & ([scriptblock]::Create((irm https://raw.githubusercontent.com/YOUR_GITHUB_USER/ArsivAra/main/install.ps1))) -Desktop
    & ([scriptblock]::Create((irm https://raw.githubusercontent.com/YOUR_GITHUB_USER/ArsivAra/main/install.ps1))) -Uninstall
    & ([scriptblock]::Create((irm https://raw.githubusercontent.com/YOUR_GITHUB_USER/ArsivAra/main/install.ps1))) -Uninstall -RemoveData

  Islemciye uygun (x64 ya da ARM64) son surumu GitHub'dan indirir, SHA256 ile dogrular,
  %LOCALAPPDATA%\Programs\ArsivAra icine koyar, Baslat menusune ve Ayarlar > Uygulamalar listesine ekler.
  Yonetici yetkisi gerekmez. Uretici: Verlora Studios.

  Bu dosya bilerek sadece ASCII karakter icerir: Windows PowerShell 5.1 BOM'suz UTF-8'i yanlis okur,
  "irm | iex" ise BOM'u sevmez. Turkce metinler \uXXXX kacislariyla yazilir (U fonksiyonu).
#>
param(
    [switch]$Desktop,
    [switch]$Uninstall,
    [switch]$RemoveData,
    # Kurulumdan sonra programi acma (betikle toplu kurulum icin)
    [switch]$NoStart,
    # Sadece test ve ayna sunucular icin: GitHub API yerine kullanilacak "son surum" adresi
    [string]$ReleaseApi
)

$ErrorActionPreference = 'Stop'
function U([string]$s) { [regex]::Unescape($s) }

$Repo         = 'YOUR_GITHUB_USER/ArsivAra'
$AppName      = U 'Ar\u015Fiv Ara'
$Publisher    = 'Verlora Studios'
$InstallDir   = Join-Path $env:LOCALAPPDATA 'Programs\ArsivAra'
$Exe          = Join-Path $InstallDir 'ArsivAra.exe'
$DataDir      = Join-Path $env:LOCALAPPDATA 'ArsivAra'
$TempDir      = Join-Path ([IO.Path]::GetTempPath()) 'ArsivAra'
$UninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ArsivAra'
$StartMenu    = Join-Path ([Environment]::GetFolderPath('Programs')) "$AppName.lnk"
$DesktopLnk   = Join-Path ([Environment]::GetFolderPath('Desktop')) "$AppName.lnk"
# 1.0.0 kurulumu kisayollari "s" ile yaziyordu (WScript.Shell Unicode adlari bozar); guncellemede ve kaldirmada temizlenir.
$LegacyLinks  = @((Join-Path ([Environment]::GetFolderPath('Programs')) 'Arsiv Ara.lnk'), (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Arsiv Ara.lnk'))
if (-not $ReleaseApi) { $ReleaseApi = "https://api.github.com/repos/$Repo/releases/latest" }

# Ayarlar > Uygulamalar'daki "Kaldir" bu betigi calistirir (internete gerek kalmadan).
$UninstallScript = @'
# Arsiv Ara kaldirma betigi (install.ps1 yazdi). Ayarlar ve kutuphane de silinsin: -RemoveData
param([switch]$RemoveData)
$ErrorActionPreference = 'SilentlyContinue'
$name = [regex]::Unescape('Ar\u015Fiv Ara')
$dir = Join-Path $env:LOCALAPPDATA 'Programs\ArsivAra'
Get-Process ArsivAra | Where-Object { $_.Path -eq (Join-Path $dir 'ArsivAra.exe') } | Stop-Process -Force
Remove-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath('Programs')) "$name.lnk") -Force
Remove-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath('Desktop')) "$name.lnk") -Force
Remove-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath('Programs')) 'Arsiv Ara.lnk') -Force
Remove-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Arsiv Ara.lnk') -Force
Remove-Item -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ArsivAra' -Recurse -Force
if ($RemoveData) {
    Remove-Item -LiteralPath (Join-Path $env:LOCALAPPDATA 'ArsivAra') -Recurse -Force
    Remove-Item -LiteralPath (Join-Path ([IO.Path]::GetTempPath()) 'ArsivAra') -Recurse -Force
}
Set-Location -LiteralPath ([IO.Path]::GetTempPath())
Remove-Item -LiteralPath $dir -Recurse -Force
'@

function Stop-App {
    Get-Process ArsivAra -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Exe } | Stop-Process -Force
}

if ($Uninstall) {
    Stop-App
    foreach ($p in @($StartMenu, $DesktopLnk) + $LegacyLinks) { if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Force } }
    if (Test-Path -LiteralPath $UninstallKey) { Remove-Item -LiteralPath $UninstallKey -Recurse -Force }
    if (Test-Path -LiteralPath $InstallDir) { Remove-Item -LiteralPath $InstallDir -Recurse -Force }
    if ($RemoveData) {
        foreach ($p in $DataDir, $TempDir) { if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Recurse -Force } }
    }
    Write-Host ((U '{0} kald\u0131r\u0131ld\u0131.') -f $AppName) -ForegroundColor Green
    if (-not $RemoveData -and (Test-Path -LiteralPath $DataDir)) {
        Write-Host ((U 'Ayarlar ve k\u00FCt\u00FCphane korundu: {0} (silmek i\u00E7in -RemoveData)') -f $DataDir) -ForegroundColor DarkGray
    }
    return
}

# Islemci: Win32_Processor.Architecture 9 = x64, 12 = ARM64 (x64 PowerShell ARM64'te oykunmeyle calissa da dogru sonuc verir).
function Get-Arch {
    $cpu = $null
    try { $cpu = (Get-CimInstance Win32_Processor -ErrorAction Stop | Select-Object -First 1).Architecture } catch { }
    if ($cpu -eq 12 -or $env:PROCESSOR_ARCHITECTURE -eq 'ARM64' -or $env:PROCESSOR_ARCHITEW6432 -eq 'ARM64') { return 'arm64' }
    if ($cpu -eq 9 -or $env:PROCESSOR_ARCHITECTURE -eq 'AMD64' -or $env:PROCESSOR_ARCHITEW6432 -eq 'AMD64') { return 'x64' }
    return $null
}

# Kisayol, Windows'un IShellLinkW arabirimiyle yazilir: WScript.Shell ad ve yollardaki Unicode harfleri
# (ornegin "Arsiv" yerine dogru yazilisi ya da Turkce kullanici adlari) ANSI'ye cevirip bozar.
function New-Shortcut([string]$Path) {
    if (-not ('ArsivAraKisayol' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
public static class ArsivAraKisayol {
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")] class ShellLink { }
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    interface IShellLinkW {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int cch, IntPtr data, int flags);
        void GetIDList(out IntPtr pidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int cch, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, int reserved);
        void Resolve(IntPtr hwnd, int flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
    public static void Create(string lnk, string target, string workDir, string description) {
        var link = (IShellLinkW)new ShellLink();
        link.SetPath(target);
        link.SetWorkingDirectory(workDir);
        link.SetDescription(description);
        ((IPersistFile)link).Save(lnk, true);
        Marshal.ReleaseComObject(link);
    }
}
'@
    }
    [ArsivAraKisayol]::Create($Path, $Exe, $InstallDir, (U 'ZIP, RAR, 7z, TAR ve ISO ar\u015Fivlerinin i\u00E7inde arama'))
}

# Windows PowerShell 5.1 varsayilan olarak eski TLS kullanabilir.
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$headers = @{ 'User-Agent' = 'ArsivAra-installer' }

$arch = Get-Arch
if (-not $arch) { throw (U '32 bit Windows desteklenmiyor; x64 ya da ARM64 gerekli.') }

Write-Host (U 'Son s\u00FCr\u00FCm aran\u0131yor\u2026') -ForegroundColor Cyan
$release = Invoke-RestMethod $ReleaseApi -Headers $headers
$assetName = if ($arch -eq 'arm64') { 'ArsivAra-arm64.exe' } else { 'ArsivAra.exe' }
$asset = $release.assets | Where-Object name -eq $assetName | Select-Object -First 1
if (-not $asset -and $arch -eq 'arm64') {
    Write-Host (U 'Bu s\u00FCr\u00FCmde ARM64 dosyas\u0131 yok; x64 s\u00FCr\u00FCm\u00FC kuruluyor (Windows \u00F6yk\u00FCnmesiyle \u00E7al\u0131\u015F\u0131r).') -ForegroundColor Yellow
    $assetName = 'ArsivAra.exe'
    $asset = $release.assets | Where-Object name -eq $assetName | Select-Object -First 1
}
if (-not $asset) { throw ((U 'S\u00FCr\u00FCm {0} i\u00E7inde {1} bulunamad\u0131.') -f $release.tag_name, $assetName) }
$hashAsset = $release.assets | Where-Object name -eq "$assetName.sha256" | Select-Object -First 1
$version = "$($release.tag_name)".TrimStart('v')

$tmp = Join-Path ([IO.Path]::GetTempPath()) "ArsivAra-$([guid]::NewGuid().ToString('N')).exe"
try {
    Write-Host ((U '\u0130ndiriliyor: {0} {1} ({2}, {3} MB)') -f $AppName, $release.tag_name, $arch, [math]::Round($asset.size / 1MB, 1)) -ForegroundColor Cyan
    Invoke-WebRequest $asset.browser_download_url -OutFile $tmp -UseBasicParsing -Headers $headers

    if ($hashAsset) {
        $expected = ((Invoke-RestMethod $hashAsset.browser_download_url -Headers $headers) -split '\s+')[0].Trim()
        $actual = (Get-FileHash $tmp -Algorithm SHA256).Hash
        if ($actual -ne $expected) { throw ((U 'SHA256 e\u015Fle\u015Fmedi. Beklenen {0}, inen {1}. Kurulum durduruldu.') -f $expected, $actual) }
        Write-Host (U 'SHA256 do\u011Fruland\u0131.') -ForegroundColor DarkGray
    }
    else {
        Write-Host (U 'Bu s\u00FCr\u00FCmde SHA256 dosyas\u0131 yok; do\u011Frulama atland\u0131.') -ForegroundColor Yellow
    }

    Stop-App
    New-Item -ItemType Directory -Force $InstallDir | Out-Null
    Move-Item $tmp $Exe -Force
}
finally {
    if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Force }
}

foreach ($p in $LegacyLinks) { if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Force } }
New-Shortcut $StartMenu
if ($Desktop) { New-Shortcut $DesktopLnk }

# Ayarlar > Uygulamalar > Yuklu uygulamalar: ad, surum, uretici, simge ve kaldirma komutu
$uninstallPs1 = Join-Path $InstallDir 'uninstall.ps1'
[IO.File]::WriteAllText($uninstallPs1, $UninstallScript, [Text.Encoding]::ASCII)
$command = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$uninstallPs1`""
New-Item -Path $UninstallKey -Force | Out-Null
$values = @{
    DisplayName          = $AppName
    DisplayVersion       = $version
    Publisher            = $Publisher
    DisplayIcon          = "$Exe,0"
    InstallLocation      = $InstallDir
    UninstallString      = $command
    QuietUninstallString = $command
    URLInfoAbout         = "https://github.com/$Repo"
    HelpLink             = "https://github.com/$Repo/issues"
}
foreach ($k in $values.Keys) { New-ItemProperty -Path $UninstallKey -Name $k -Value $values[$k] -PropertyType String -Force | Out-Null }
foreach ($k in 'NoModify', 'NoRepair') { New-ItemProperty -Path $UninstallKey -Name $k -Value 1 -PropertyType DWord -Force | Out-Null }
New-ItemProperty -Path $UninstallKey -Name 'EstimatedSize' -Value ([int]((Get-Item -LiteralPath $Exe).Length / 1KB)) -PropertyType DWord -Force | Out-Null

Write-Host ''
Write-Host ((U '{0} {1} kuruldu: {2}') -f $AppName, $version, $Exe) -ForegroundColor Green
Write-Host ((U 'Ba\u015Flat men\u00FCs\u00FCnde "{0}" olarak bulabilirsiniz. Kald\u0131rmak i\u00E7in: Ayarlar > Uygulamalar > Y\u00FCkl\u00FC uygulamalar.') -f $AppName)
Write-Host (U 'Verlora Studios \u00B7 Created By MaledictusMaggot\u00AE') -ForegroundColor DarkGray
if (-not $NoStart) { Start-Process $Exe }
