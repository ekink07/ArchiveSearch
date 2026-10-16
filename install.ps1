<#
  Arsiv Ara installer.

  Install (one line):
    irm https://raw.githubusercontent.com/ekink07/ArchiveSearch/main/install.ps1 | iex

  With options (desktop shortcut / uninstall / uninstall together with settings and library):
    & ([scriptblock]::Create((irm https://raw.githubusercontent.com/ekink07/ArchiveSearch/main/install.ps1))) -Desktop
    & ([scriptblock]::Create((irm https://raw.githubusercontent.com/ekink07/ArchiveSearch/main/install.ps1))) -Uninstall
    & ([scriptblock]::Create((irm https://raw.githubusercontent.com/ekink07/ArchiveSearch/main/install.ps1))) -Uninstall -RemoveData

  Downloads the latest release for this processor (x64 or ARM64) from GitHub, verifies its SHA256,
  puts it in %LOCALAPPDATA%\Programs\ArsivAra and adds it to the Start menu and to Settings > Apps.
  No administrator rights needed. Publisher: Verlora Studios.

  This file is deliberately pure ASCII: Windows PowerShell 5.1 misreads UTF-8 without a BOM, and
  "irm | iex" does not like a BOM. Non-ASCII text is written with \uXXXX escapes (function U).
#>
param(
    [switch]$Desktop,
    [switch]$Uninstall,
    [switch]$RemoveData,
    # Do not start the program after installing (for scripted installs)
    [switch]$NoStart,
    # For tests and mirrors only: the "latest release" address to use instead of the GitHub API
    [string]$ReleaseApi
)

$ErrorActionPreference = 'Stop'
function U([string]$s) { [regex]::Unescape($s) }

$Repo         = 'ekink07/ArchiveSearch'
$AppName      = U 'Ar\u015Fiv Ara'
$Publisher    = 'Verlora Studios'
$InstallDir   = Join-Path $env:LOCALAPPDATA 'Programs\ArsivAra'
$Exe          = Join-Path $InstallDir 'ArsivAra.exe'
$DataDir      = Join-Path $env:LOCALAPPDATA 'ArsivAra'
$TempDir      = Join-Path ([IO.Path]::GetTempPath()) 'ArsivAra'
$UninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ArsivAra'
$StartMenu    = Join-Path ([Environment]::GetFolderPath('Programs')) "$AppName.lnk"
$DesktopLnk   = Join-Path ([Environment]::GetFolderPath('Desktop')) "$AppName.lnk"
# The 1.0.0 installer wrote the shortcut names with a plain "s" (WScript.Shell mangles Unicode names);
# they are removed on update and uninstall.
$LegacyLinks  = @((Join-Path ([Environment]::GetFolderPath('Programs')) 'Arsiv Ara.lnk'), (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Arsiv Ara.lnk'))
if (-not $ReleaseApi) { $ReleaseApi = "https://api.github.com/repos/$Repo/releases/latest" }

# "Uninstall" in Settings > Apps runs this script (no internet needed).
$UninstallScript = @'
# Arsiv Ara uninstaller (written by install.ps1). Also delete settings and library: -RemoveData
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
    Write-Host ('{0} was uninstalled.' -f $AppName) -ForegroundColor Green
    if (-not $RemoveData -and (Test-Path -LiteralPath $DataDir)) {
        Write-Host ('Settings and library were kept: {0} (use -RemoveData to delete them)' -f $DataDir) -ForegroundColor DarkGray
    }
    return
}

# Processor: Win32_Processor.Architecture 9 = x64, 12 = ARM64 (correct even when x64 PowerShell runs emulated on ARM64).
function Get-Arch {
    $cpu = $null
    try { $cpu = (Get-CimInstance Win32_Processor -ErrorAction Stop | Select-Object -First 1).Architecture } catch { }
    if ($cpu -eq 12 -or $env:PROCESSOR_ARCHITECTURE -eq 'ARM64' -or $env:PROCESSOR_ARCHITEW6432 -eq 'ARM64') { return 'arm64' }
    if ($cpu -eq 9 -or $env:PROCESSOR_ARCHITECTURE -eq 'AMD64' -or $env:PROCESSOR_ARCHITEW6432 -eq 'AMD64') { return 'x64' }
    return $null
}

# Shortcuts are written through Windows' IShellLinkW interface: WScript.Shell converts names and paths to ANSI
# and mangles Unicode letters (the app name itself, or user names with non-ASCII letters).
function New-Shortcut([string]$Path) {
    if (-not ('ArsivAraShortcut' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
public static class ArsivAraShortcut {
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
    [ArsivAraShortcut]::Create($Path, $Exe, $InstallDir, 'Search inside ZIP, RAR, 7z, TAR and ISO archives')
}

# Windows PowerShell 5.1 may default to old TLS versions.
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$headers = @{ 'User-Agent' = 'ArsivAra-installer' }

$arch = Get-Arch
if (-not $arch) { throw '32-bit Windows is not supported; x64 or ARM64 is required.' }

Write-Host (U 'Looking for the latest release\u2026') -ForegroundColor Cyan
$release = Invoke-RestMethod $ReleaseApi -Headers $headers
$assetName = if ($arch -eq 'arm64') { 'ArsivAra-arm64.exe' } else { 'ArsivAra.exe' }
$asset = $release.assets | Where-Object name -eq $assetName | Select-Object -First 1
if (-not $asset -and $arch -eq 'arm64') {
    Write-Host 'This release has no ARM64 build; installing the x64 build (it runs under Windows emulation).' -ForegroundColor Yellow
    $assetName = 'ArsivAra.exe'
    $asset = $release.assets | Where-Object name -eq $assetName | Select-Object -First 1
}
if (-not $asset) { throw ('Release {0} does not contain {1}.' -f $release.tag_name, $assetName) }
$hashAsset = $release.assets | Where-Object name -eq "$assetName.sha256" | Select-Object -First 1
$version = "$($release.tag_name)".TrimStart('v')

$tmp = Join-Path ([IO.Path]::GetTempPath()) "ArsivAra-$([guid]::NewGuid().ToString('N')).exe"
try {
    Write-Host ('Downloading {0} {1} ({2}, {3} MB)' -f $AppName, $release.tag_name, $arch, [math]::Round($asset.size / 1MB, 1)) -ForegroundColor Cyan
    Invoke-WebRequest $asset.browser_download_url -OutFile $tmp -UseBasicParsing -Headers $headers

    if ($hashAsset) {
        $expected = ((Invoke-RestMethod $hashAsset.browser_download_url -Headers $headers) -split '\s+')[0].Trim()
        $actual = (Get-FileHash $tmp -Algorithm SHA256).Hash
        if ($actual -ne $expected) { throw ('SHA256 mismatch. Expected {0}, downloaded {1}. Installation stopped.' -f $expected, $actual) }
        Write-Host 'SHA256 verified.' -ForegroundColor DarkGray
    }
    else {
        Write-Host 'This release has no SHA256 file; verification skipped.' -ForegroundColor Yellow
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

# Settings > Apps > Installed apps: name, version, publisher, icon and uninstall command
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
Write-Host ('{0} {1} installed: {2}' -f $AppName, $version, $Exe) -ForegroundColor Green
Write-Host ('Find it as "{0}" in the Start menu. To uninstall: Settings > Apps > Installed apps.' -f $AppName)
Write-Host (U 'Verlora Studios \u00B7 Created By MaledictusMaggot\u00AE') -ForegroundColor DarkGray
if (-not $NoStart) { Start-Process $Exe }
