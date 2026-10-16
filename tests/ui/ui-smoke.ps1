# Arşiv Ara UI smoke test: starts the built app with a temporary data folder and exercises the main features
# through UI Automation (no keyboard or mouse, without stealing focus). Exit code = number of failed checks.
#   pwsh tests/ui/ui-smoke.ps1 [-Exe <ArsivAra.exe>] [-Screenshots <folder>]
param(
  [string]$Exe = (Join-Path $PSScriptRoot '..\..\src\ArsivAra.App\bin\Release\net10.0-windows\ArsivAra.exe'),
  [string]$Screenshots
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class UiWin {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int s);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  public static RECT Bounds(IntPtr h) { RECT r; DwmGetWindowAttribute(h, 9, out r, Marshal.SizeOf(typeof(RECT))); return r; }
}
"@
[UiWin]::SetProcessDPIAware() | Out-Null
# The test never types; if the window still comes to the front, focus goes straight back to the user's window
# so that whatever they are typing does not end up in the app.
$script:userWindow = [UiWin]::GetForegroundWindow()
function Give-Back-Focus {
  $fg = [UiWin]::GetForegroundWindow(); [uint32]$fgPid = 0; [UiWin]::GetWindowThreadProcessId($fg, [ref]$fgPid) | Out-Null
  $ours = Get-Process -Name ArsivAra -ErrorAction SilentlyContinue | ForEach-Object Id
  if ($ours -contains $fgPid -and $script:userWindow -ne [IntPtr]::Zero) { [UiWin]::SetForegroundWindow($script:userWindow) | Out-Null }
}

$Exe = (Resolve-Path $Exe).Path
$fixtures = (Resolve-Path (Join-Path $PSScriptRoot '..\ArsivAra.Core.Tests\Fixtures')).Path
$data = Join-Path ([IO.Path]::GetTempPath()) ('arsivara-ui-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $data | Out-Null
# No language in the settings: a first start must come up in English.
'{ "ShowPreview": false, "IconSize": 16 }' | Set-Content (Join-Path $data 'settings.json') -Encoding utf8
$env:ARSIVARA_DATA = $data

$script:pass = 0; $script:fail = 0
function Check([bool]$ok, [string]$what) {
  if ($ok) { $script:pass++; Write-Host "  OK   $what" -ForegroundColor Green }
  else { $script:fail++; Write-Host "  FAIL $what" -ForegroundColor Red }
}
$AE = [Windows.Automation.AutomationElement]
function Cond($prop, $value) { New-Object Windows.Automation.PropertyCondition ($prop, $value) }
function Wait-For([scriptblock]$probe, [int]$seconds = 15) {
  $sw = [Diagnostics.Stopwatch]::StartNew()
  while ($sw.Elapsed.TotalSeconds -lt $seconds) {
    try { $r = & $probe; if ($r) { return $r } } catch { }
    Start-Sleep -Milliseconds 200
  }
  return $null
}
function Find($root, [string]$id, [int]$seconds = 10) {
  Wait-For { $root.FindFirst([Windows.Automation.TreeScope]::Descendants, (Cond $AE::AutomationIdProperty $id)) } $seconds
}
function TextOf($root, [string]$id) { $e = Find $root $id 3; if ($e) { $e.Current.Name } else { '' } }
function Invoke($root, [string]$id) { (Find $root $id).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Search($root, [string]$text) { (Find $root 'SearchBox').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($text) }
function Window([string]$title, [int]$seconds = 10, $parent = $null) {
  # Owned dialogs appear in the UIA tree as children of their owner window, not of the desktop.
  $c = Cond $AE::NameProperty $title
  $w = Wait-For {
    $x = $AE::RootElement.FindFirst([Windows.Automation.TreeScope]::Children, $c)
    foreach ($owner in @($parent, $script:root)) { if (-not $x -and $owner) { $x = $owner.FindFirst([Windows.Automation.TreeScope]::Children, $c) } }
    $x
  } $seconds
  Give-Back-Focus
  return $w
}
function FirstResult($root) {
  $list = Find $root 'Results'
  $list.FindFirst([Windows.Automation.TreeScope]::Children, (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::ListItem)))
}
function ResultCount($root) {
  $s = TextOf $root 'ResultStatus'
  if ($s -match '^([\d.,]+)') { [int]($Matches[1] -replace '[.,]', '') } else { -1 }
}
function Snap([IntPtr]$h, [string]$name) {
  if (-not $Screenshots) { return }
  New-Item -ItemType Directory -Force $Screenshots | Out-Null
  Start-Sleep -Milliseconds 600
  $r = [UiWin]::Bounds($h); $bmp = New-Object Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
  $g = [Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size); $g.Dispose()
  $bmp.Save((Join-Path $Screenshots "$name.png"), [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}
function Start-App([string[]]$files) {
  $quoted = $files | ForEach-Object { "`"$_`"" }
  $p = if ($quoted) { Start-Process $Exe -ArgumentList $quoted -PassThru } else { Start-Process $Exe -PassThru }
  $h = Wait-For { $p.Refresh(); if ($p.MainWindowHandle -ne 0) { $p.MainWindowHandle } } 20
  if (-not $h) { throw 'The main window did not open' }
  [UiWin]::SetWindowPos($h, [IntPtr](-1), 40, 40, 1194, 727, 0x10) | Out-Null # topmost, without activating
  Give-Back-Focus
  return @{ Process = $p; Handle = $h; Root = $AE::FromHandle($h) }
}

$archives = 'zip-utf8.zip', '7z-solid.7z', 'ic-ice.zip', 'rar5-sifreli-baslik.rar', 'joliet.iso', 'kaynak.tar.gz' |
  ForEach-Object { Join-Path $fixtures $_ }

Write-Host "Arşiv Ara UI smoke test`n  exe:  $Exe`n  data: $data"
$app = Start-App $archives
$root = $app.Root
try {
  Write-Host '- Start-up, logo and credit (English by default)'
  Check ([bool](Wait-For { (TextOf $root 'LibraryStatus') -match '^6 archives' } 30)) "6 archives added ($(TextOf $root 'LibraryStatus'))"
  Check ([bool](Find $root 'BrandLogo')) 'Logo in the top bar'
  $credit = Find $root 'CreditLink'
  Check ($credit -and $credit.Current.Name -eq 'Created By MaledictusMaggot®') 'Credit "Created By MaledictusMaggot®" in the status bar'
  Check ((Find $root 'SearchBox').Current.Name -eq 'Search inside archives…') 'First start is in English'
  $archiveList = Find $root 'ArchiveList'
  $texts = $archiveList.FindAll([Windows.Automation.TreeScope]::Descendants, (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::Text))) | ForEach-Object { $_.Current.Name }
  Check ([bool]($texts | Where-Object { $_ -like '*Encrypted*' })) 'RAR with encrypted headers is shown as "Encrypted"'
  Check ([bool]($texts | Where-Object { $_ -like '*3 nested*' })) 'Nested archives counted (ic-ice.zip: 3 nested)'
  Snap $app.Handle 'main-list'

  Write-Host '- Search syntax'
  Search $root 'sirket'
  Check ([bool](Wait-For { (ResultCount $root) -eq 7 } 10)) "sirket -> 7 results (nested and ISO included): $(TextOf $root 'ResultStatus')"
  Search $root 'sirket|sozlesme archive:zip'
  Check ([bool](Wait-For { (ResultCount $root) -eq 8 } 10)) "OR + archive: -> 8 results: $(TextOf $root 'ResultStatus')"
  Search $root '*.png !istanbul'
  Check ([bool](Wait-For { (ResultCount $root) -eq 0 } 10)) "Wildcard + NOT -> 0 results: $(TextOf $root 'ResultStatus')"
  Search $root 'size:>40kb'
  Check ([bool](Wait-For { (ResultCount $root) -eq 10 } 10)) "size:>40kb -> 10 results (7 rastgele.bin + 3 nested archives): $(TextOf $root 'ResultStatus')"
  Search $root 'size:abc'
  Check ([bool](Wait-For { (TextOf $root 'ResultStatus') -like 'Invalid size*' } 10)) 'An invalid value is explained in the status bar'

  Write-Host '- Sorting and preview'
  Search $root 'okubeni|rastgele file:'
  Wait-For { (ResultCount $root) -gt 0 } 10 | Out-Null
  Invoke $root 'SortSize'
  Check ([bool](Wait-For { (FirstResult $root).Current.Name -eq 'rastgele.bin' } 10)) 'Clicking the Size header sorts largest first'
  Invoke $root 'PreviewButton'
  Search $root 'name:="Sözleşme.txt" archive:zip-utf8'
  Wait-For { (ResultCount $root) -eq 1 } 10 | Out-Null
  (FirstResult $root).GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
  Check ([bool](Wait-For { (TextOf $root 'PreviewTitle') -eq 'Sözleşme.txt' } 10)) 'Preview shows the selected file name'
  Check ([bool](Wait-For { (Find $root 'PreviewText' 2).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -like '*İSTANBUL şubesi*' } 10)) 'Preview shows the text content'
  Snap $app.Handle 'preview'

  Write-Host '- Grid view'
  Invoke $root 'PreviewButton'
  Search $root ''
  Invoke $root 'GridViewButton'
  Check ([bool](Wait-For { (Find $root 'ResultsGrid' 2).Current.IsOffscreen -eq $false } 5)) 'Switched to the grid view'
  Snap $app.Handle 'grid'
  Invoke $root 'ListViewButton'
  Check ([bool](Wait-For { (Find $root 'Results' 2).Current.IsOffscreen -eq $false } 5)) 'Back to the list'
  Invoke $root 'PreviewButton'

  Write-Host '- Content search, versions, duplicates'
  Search $root 'content:zumrut'
  Check ([bool](Wait-For { (TextOf $root 'ResultStatus') -match '^7 results · ' } 20)) "content:zumrut -> 7 files: $(TextOf $root 'ResultStatus')"
  Search $root 'name:="Şirket Raporu 2023.txt" file:'
  Check ([bool](Wait-For { (ResultCount $root) -eq 7 } 10)) 'Versions with the same name -> 7'
  Search $root 'dupe: okubeni'
  Check ([bool](Wait-For { (ResultCount $root) -ge 7 } 10)) "dupe: -> $(ResultCount $root) copies"

  Write-Host '- Switching the language (Settings)'
  Invoke $root 'SettingsButton'
  $settings = Window 'Settings'
  Check ([bool]$settings) 'Settings window opened'
  Snap ([IntPtr]$settings.Current.NativeWindowHandle) 'settings'
  $combo = Find $settings 'LanguageBox'
  $combo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
  $turkish = Wait-For { $combo.FindFirst([Windows.Automation.TreeScope]::Descendants, (Cond $AE::NameProperty 'Türkçe')) } 5
  $turkish.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
  $combo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
  Check ([bool](Wait-For { $settings.Current.Name -eq 'Ayarlar' } 5)) 'Settings window switched to Turkish immediately'
  Invoke $settings 'SettingsClose'
  Check ([bool](Wait-For { (Find $root 'SearchBox').Current.Name -eq 'Arşivlerin içinde ara…' } 5)) 'Main window in Turkish (search box)'
  Check ((TextOf $root 'LibraryStatus') -notmatch 'archives') "No English leftovers in the status bar ($(TextOf $root 'LibraryStatus'))"
  Check ((Find $root 'SortName').Current.Name -eq 'Ad') 'Column headers in Turkish'
  Snap $app.Handle 'turkish'

  Write-Host '- About and license'
  Invoke $root 'CreditLink'
  $about = Window 'Arşiv Ara hakkında'
  Check ([bool]$about) 'Clicking the credit opens About'
  if ($about) {
    Check ((TextOf $about 'AboutCredit') -like '*MaledictusMaggot®*') 'About: Created By MaledictusMaggot®'
    Check ((TextOf $about 'AboutVersion') -match '^Sürüm \d') "About: $(TextOf $about 'AboutVersion')"
    Check ((TextOf $about 'AboutManufacturer') -eq 'Üretici: Verlora Studios') "About: $(TextOf $about 'AboutManufacturer')"
    Check ((TextOf $about 'AboutLicenseKind') -eq 'MIT') 'About: license section (MIT)'
    Check ((TextOf $about 'AboutCopyright') -like 'Copyright © 20*MaledictusMaggot') "About: $(TextOf $about 'AboutCopyright')"
    Snap ([IntPtr]$about.Current.NativeWindowHandle) 'about'

    Invoke $about 'AboutLicenseButton'
    $license = Window 'Arşiv Ara lisansı' 10 $about
    Check ([bool]$license) 'License text window opened'
    if ($license) {
      $text = (Find $license 'LicenseText').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
      Check ($text -like 'MIT License*MaledictusMaggot*Permission is hereby granted*') 'License: full LICENSE text'
      Check ((TextOf $license 'LicenseSubtitle') -like '*Verlora Studios*') "License: $(TextOf $license 'LicenseSubtitle')"
      (Find $license 'NoticesTab').GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
      $notices = Wait-For { (Find $license 'NoticesText' 2).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value } 5
      Check ($notices -like '*SharpCompress*UnRAR source code may be used*') 'License: third-party components tab'
      Snap ([IntPtr]$license.Current.NativeWindowHandle) 'license'
      Invoke $license 'LicenseClose'
      Check ([bool](Wait-For { -not (Window 'Arşiv Ara lisansı' 1 $about) } 5)) 'License window closed'
    }
    Invoke $about 'AboutOk'
    Check ([bool](Wait-For { -not (Window 'Arşiv Ara hakkında' 1) } 5)) 'About closed'
  }

  Write-Host '- Single instance: a second start passes the file to the running window'
  $second = Start-Process $Exe -ArgumentList "`"$(Join-Path $fixtures 'kaynak.tar.bz2')`"" -PassThru
  Check ($second.WaitForExit(10000)) 'The second process exited right away'
  Check ([bool](Wait-For { (TextOf $root 'LibraryStatus') -match '^7 arşiv' } 20)) "The file was added to the running window ($(TextOf $root 'LibraryStatus'))"

  Write-Host '- Closing and persistence'
  $root.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
  Check ($app.Process.WaitForExit(10000)) 'The program closed'
  $app = Start-App @()
  $root = $app.Root
  Check ([bool](Wait-For { (TextOf $root 'LibraryStatus') -match '^7 arşiv' } 20)) "Library and language kept after restart ($(TextOf $root 'LibraryStatus'))"
  Check (Test-Path (Join-Path $data 'index.bin')) 'index.bin is in the data folder'
  Check (-not (Test-Path (Join-Path (Split-Path $Exe) 'ArsivAra-veri'))) 'No ArsivAra-veri folder was created next to the exe'
  Check (Test-Path (Join-Path $data 'gunluk')) 'The log folder was created'
}
finally {
  if ($app -and -not $app.Process.HasExited) { $app.Process.Kill() }
  Get-Process ArsivAra -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Exe } | Stop-Process -Force # leftover test instances
  Remove-Item $data -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host "`nResult: $script:pass passed, $script:fail failed"
exit $script:fail
