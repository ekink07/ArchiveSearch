# Produces the screenshots used by the docs: docs/images/{empty,search,grid,about}.png
#   pwsh tools/make_screenshots.ps1 [-Exe <ArsivAra.exe>] [-Language en]
# Sample archives are built in a temporary folder with .NET and Windows' tar.exe; for 7z, the encrypted RAR and the
# ISO, test archives are copied. The app runs with a temporary data folder, is driven through UI Automation and the
# window is captured with PrintWindow: no keyboard, mouse or focus is used, and windows on top or the mouse pointer
# never end up in the image.
param(
  [string]$Exe = (Join-Path $PSScriptRoot '..\src\ArsivAra.App\bin\Release\net10.0-windows\ArsivAra.exe'),
  [string]$Out = (Join-Path $PSScriptRoot '..\docs\images'),
  [string]$Language = 'en',
  [int]$Width = 1180,
  [int]$Height = 720
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing, System.IO.Compression, UIAutomationClient, UIAutomationTypes
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Shot {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int s);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  public static RECT Frame(IntPtr h) { RECT r; DwmGetWindowAttribute(h, 9, out r, Marshal.SizeOf(typeof(RECT))); return r; }
  public static RECT Outer(IntPtr h) { RECT r; GetWindowRect(h, out r); return r; }
}
"@
[Shot]::SetProcessDPIAware() | Out-Null
[Text.Encoding]::RegisterProvider([Text.CodePagesEncodingProvider]::Instance)

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Exe = (Resolve-Path $Exe).Path
$Out = (Resolve-Path $Out).Path
$fixtures = Join-Path $root 'tests\ArsivAra.Core.Tests\Fixtures'
$work = Join-Path ([IO.Path]::GetTempPath()) ('arsivara-shots-' + [guid]::NewGuid().ToString('N'))
$samples = Join-Path $work 'Archives'
New-Item -ItemType Directory -Force $samples | Out-Null

# ---------------------------------------------------------------- sample archives

function New-Photo([int]$n) {
  $palettes = @(@(142, 45, 226, 255, 112, 67), @(33, 150, 243, 233, 30, 99), @(46, 125, 50, 129, 199, 132),
    @(255, 152, 0, 233, 30, 99), @(63, 81, 181, 0, 188, 212), @(198, 40, 40, 255, 193, 7))
  $p = $palettes[$n % $palettes.Count]
  $w, $h = @(@(300, 450), @(450, 300), @(400, 400), @(450, 260))[$n % 4]
  $bmp = New-Object Drawing.Bitmap $w, $h
  $g = [Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'
  $brush = New-Object Drawing.Drawing2D.LinearGradientBrush ((New-Object Drawing.Point 0, 0), (New-Object Drawing.Point $w, $h),
    [Drawing.Color]::FromArgb($p[0], $p[1], $p[2]), [Drawing.Color]::FromArgb($p[3], $p[4], $p[5]))
  $g.FillRectangle($brush, 0, 0, $w, $h)
  $r = [Math]::Min($w, $h) / 6
  $g.FillEllipse([Drawing.Brushes]::White, $w / 2 - $r, $h / 2 - $r, 2 * $r, 2 * $r)
  $font = New-Object Drawing.Font 'Segoe UI', ($r * 0.62), ([Drawing.FontStyle]::Bold), ([Drawing.GraphicsUnit]::Pixel)
  $format = New-Object Drawing.StringFormat; $format.Alignment = 'Center'; $format.LineAlignment = 'Center'
  $g.DrawString("$n", $font, (New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(40, 40, 40))), (New-Object Drawing.RectangleF 0, 0, $w, $h), $format)
  $g.Dispose()
  $ms = New-Object IO.MemoryStream
  $bmp.Save($ms, $(if ($n % 2) { [Drawing.Imaging.ImageFormat]::Jpeg } else { [Drawing.Imaging.ImageFormat]::Png }))
  $bmp.Dispose()
  return , $ms.ToArray()
}

function Bytes($data) { if ($data -is [string]) { , [Text.Encoding]::UTF8.GetBytes($data) } else { , [byte[]]$data } }

# $items: @( @('path/name', <text or bytes>, 'yyyy-MM-dd HH:mm'), ... ); a path ending in '/' is a folder.
function New-Zip([string]$path, [object[]]$items, [Text.Encoding]$encoding = [Text.Encoding]::UTF8) {
  $fs = [IO.File]::Create($path)
  $zip = New-Object IO.Compression.ZipArchive ($fs, [IO.Compression.ZipArchiveMode]::Create, $false, $encoding)
  foreach ($it in $items) {
    $e = $zip.CreateEntry($it[0])
    $e.LastWriteTime = [datetime]$it[2]
    if (-not $it[0].EndsWith('/')) { $b = Bytes $it[1]; $s = $e.Open(); $s.Write($b, 0, $b.Length); $s.Dispose() }
  }
  $zip.Dispose(); $fs.Dispose()
}

$notes2025 = @"
Meeting Notes - 12 September 2025

Attendees: Alice, Mehmet, Sofia, Lukas

1. Backup routine
   - Weekly backups are added to backups.zip on Sunday night.
   - Old backups are kept; search inside them with Arsiv Ara.

2. Budget 2025
   - Sheets/Budget 2025.xlsx is updated; approval expected by the end of October.

3. Istanbul office
   - Legal will follow up on the contract renewal.

Next meeting: 26 September 2025, 10:00
"@
$notes2023 = @"
Meeting Notes - 3 November 2023

1. Project backup taken (2023/project_backup.zip).
2. First draft of the company report shared.
"@

# Old ZIP with DOS (CP857) Turkish file names: shows that legacy names are decoded correctly.
New-Zip "$samples\old_dos_names.zip" @(
  @('eski/Belgeler/Şirket Raporu 2019.pdf', '%PDF-1.4 report 2019', '2019-04-02 16:40'),
  @('eski/Fotoğraflar/İstanbul Gezisi/IMG_0001.jpg', (New-Photo 1), '2018-07-21 11:05'),
  @('eski/Müşteri Listesi.xlsx', 'customers', '2019-02-11 09:12'),
  @('eski/Faturalar/2019/Ocak/fatura_00123.pdf', '%PDF-1.4 invoice 00123', '2019-01-31 17:30')
) ([Text.Encoding]::GetEncoding(857))

$gallery = foreach ($n in 1..16) { , @("Photos/Trip 2025/IMG_$('{0:D4}' -f $n).$(if ($n % 2) { 'jpg' } else { 'png' })", (New-Photo $n), "2025-08-$('{0:D2}' -f (2 + $n)) 1$($n % 10):2$($n % 6)") }
$gallery += , @('Photos/Empty folder/', '', '2025-08-20 12:00')
foreach ($f in @(@('Quote_2025.docx', 'quote'), @('Price List.xlsx', 'prices'), @('Installation Guide.pdf', '%PDF guide'), @('Slides.pptx', 'slides'),
    @('Assembly.dwg', 'dwg'), @('Part-01.step', 'ISO-10303-21;'), @('readme.txt', 'readme'), @('intro.mp4', [byte[]]::new(2048)),
    @('Program.cs', 'Console.WriteLine("Hello");'))) {
  $gallery += , @("Files/$($f[0])", $f[1], '2025-06-18 14:45')
}
New-Zip "$samples\photos.zip" $gallery

New-Zip "$samples\documents.zip" @(
  @('Documents/Company Report 2024.pdf', '%PDF-1.4 company report 2024', '2025-02-14 18:20'),
  @('Documents/Contract.pdf', '%PDF-1.4 contract', '2025-03-03 11:15'),
  @('Sheets/Budget 2025.xlsx', 'budget', '2025-09-10 08:55'),
  @('Notes/Meeting Notes.txt', $notes2025, '2025-09-12 15:30'),
  @('Photos/Cappadocia.jpg', (New-Photo 5), '2025-05-01 07:10')
)

# Nested: backups.zip > 2023/project_backup.zip and 2024/source.tar.gz
$stage = Join-Path $work 'source'
New-Item -ItemType Directory -Force "$stage\source" | Out-Null
[IO.File]::WriteAllText("$stage\source\main.c", "#include <stdio.h>`nint main(void) { puts(""hello""); return 0; }`n")
[IO.File]::WriteAllText("$stage\source\util.h", "#pragma once`nint add(int a, int b);`n")
[IO.File]::WriteAllText("$stage\source\notes.txt", ("build notes`n" * 60))
Get-ChildItem $stage -Recurse | ForEach-Object { $_.LastWriteTime = [datetime]'2024-04-09 13:00' }
$tar = Join-Path $work 'source.tar.gz'
& "$env:SystemRoot\System32\tar.exe" -czf $tar -C $stage source
if ($LASTEXITCODE) { throw 'tar.exe could not create source.tar.gz' }
New-Zip "$work\project_backup.zip" @(
  @('Documents/Company Report 2023.pdf', '%PDF-1.4 company report 2023', '2023-10-30 17:45'),
  @('Notes/Meeting Notes.txt', $notes2023, '2023-11-03 10:20'),
  @('Source/Program.cs', 'Console.WriteLine("Hello");', '2023-09-18 21:05')
)
New-Zip "$samples\backups.zip" @(
  @('2023/project_backup.zip', [IO.File]::ReadAllBytes("$work\project_backup.zip"), '2023-11-03 18:00'),
  @('2024/source.tar.gz', [IO.File]::ReadAllBytes($tar), '2024-04-09 13:05'),
  @('readme.txt', 'Weekly backups.', '2025-09-14 23:00')
)
Copy-Item $tar "$samples\source.tar.gz"
Copy-Item "$fixtures\7z-solid.7z" "$samples\archive.7z"
Copy-Item "$fixtures\udf.iso" "$samples\Setup_Disc.iso"
Copy-Item "$fixtures\rar5-sifreli-baslik.rar" "$samples\Accounting_2024.rar"
$sampleNames = Get-ChildItem $samples -File | Sort-Object Name | ForEach-Object Name
Write-Host "Sample archives: $($sampleNames.Count)"

# ---------------------------------------------------------------- app and capture

$AE = [Windows.Automation.AutomationElement]
$userWindow = [Shot]::GetForegroundWindow()
function Cond($prop, $value) { New-Object Windows.Automation.PropertyCondition ($prop, $value) }
function Wait-For([scriptblock]$probe, [int]$seconds = 15) {
  $sw = [Diagnostics.Stopwatch]::StartNew()
  while ($sw.Elapsed.TotalSeconds -lt $seconds) {
    try { $r = & $probe; if ($r) { return $r } } catch { }
    Start-Sleep -Milliseconds 200
  }
  throw "Timed out: $probe"
}
function Give-Back-Focus {
  [uint32]$fgPid = 0; [Shot]::GetWindowThreadProcessId([Shot]::GetForegroundWindow(), [ref]$fgPid) | Out-Null
  if ((Get-Process -Id $fgPid -ErrorAction SilentlyContinue).Path -eq $Exe) { [Shot]::SetForegroundWindow($userWindow) | Out-Null }
}
function Find($el, [string]$id) { Wait-For { $el.FindFirst([Windows.Automation.TreeScope]::Descendants, (Cond $AE::AutomationIdProperty $id)) } 10 }
function Invoke($el, [string]$id) { (Find $el $id).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Give-Back-Focus }
function Search($el, [string]$text) { (Find $el 'SearchBox').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($text); Give-Back-Focus }

function Start-App([string]$name, [string]$settingsJson, [string[]]$files) {
  $data = Join-Path $work $name
  New-Item -ItemType Directory -Force $data | Out-Null
  [IO.File]::WriteAllText((Join-Path $data 'settings.json'), $settingsJson)
  $env:ARSIVARA_DATA = $data
  $quoted = $files | ForEach-Object { "`"$_`"" }
  $p = if ($quoted) { Start-Process $Exe -ArgumentList $quoted -PassThru } else { Start-Process $Exe -PassThru }
  $h = Wait-For { $p.Refresh(); if ($p.MainWindowHandle -ne 0) { $p.MainWindowHandle } } 20
  Resize $h
  Give-Back-Focus
  return @{ Process = $p; Handle = $h; Root = $AE::FromHandle($h) }
}

# The visible frame is exactly Width x Height; the window goes to the bottom of the z-order (HWND_BOTTOM) without activation.
function Resize([IntPtr]$h) {
  $o = [Shot]::Outer($h); $f = [Shot]::Frame($h)
  $cx = $Width + (($o.R - $o.L) - ($f.R - $f.L)); $cy = $Height + (($o.B - $o.T) - ($f.B - $f.T))
  [Shot]::SetWindowPos($h, [IntPtr]1, 40, 40, $cx, $cy, 0x10) | Out-Null
}

function Save-Window([IntPtr]$h, [string]$name) {
  Start-Sleep -Milliseconds 900 # let transitions and thumbnails settle
  Give-Back-Focus # if the window came to the front by itself, focus goes back to the user (and every image has the same title bar)
  $o = [Shot]::Outer($h); $f = [Shot]::Frame($h)
  $full = New-Object Drawing.Bitmap ($o.R - $o.L), ($o.B - $o.T)
  $g = [Drawing.Graphics]::FromImage($full); $dc = $g.GetHdc()
  [Shot]::PrintWindow($h, $dc, 2) | Out-Null # PW_RENDERFULLCONTENT
  $g.ReleaseHdc($dc); $g.Dispose()
  $rect = New-Object Drawing.Rectangle ($f.L - $o.L), ($f.T - $o.T), ($f.R - $f.L), ($f.B - $f.T)
  $crop = $full.Clone($rect, $full.PixelFormat); $full.Dispose()
  $path = Join-Path $Out "$name.png"
  $crop.Save($path, [Drawing.Imaging.ImageFormat]::Png); $crop.Dispose()
  Write-Host "  $name.png  $($rect.Width)x$($rect.Height)"
}

function Close-App($app) {
  $app.Root.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
  if (-not $app.Process.WaitForExit(10000)) { $app.Process.Kill() }
}

$apps = @()
$drive = $null
try {
  # The preview pane shows each archive's full path; a temporary virtual drive keeps the user profile path out of the images.
  $drive = 'EFGHIJKLMNOPQRSTUVWXYZ'.ToCharArray() | ForEach-Object { "$($_):" } | Where-Object { -not (Test-Path "$_\") } | Select-Object -First 1
  if (-not $drive) { throw 'No free drive letter for the sample archives' }
  & "$env:SystemRoot\System32\subst.exe" $drive $work
  if ($LASTEXITCODE) { $drive = $null; throw 'subst could not map a drive for the sample archives' }
  $archives = $sampleNames | ForEach-Object { "$drive\Archives\$_" }

  # 1) First start
  $app = Start-App 'empty' "{ ""Language"": ""$Language"", ""ShowPreview"": false }" @(); $apps += $app
  Find $app.Root 'EmptyScanDisks' | Out-Null
  Save-Window $app.Handle 'empty'
  Close-App $app

  # 2) Search + preview, grid, About
  $app = Start-App 'library' "{ ""Language"": ""$Language"", ""ShowPreview"": false, ""IconSize"": 16 }" $archives; $apps += $app
  $root = $app.Root
  Wait-For { (Find $root 'LibraryStatus').Current.Name -match "^$($archives.Count) " } 60 | Out-Null

  Invoke $root 'PreviewButton'
  Search $root 'meeting'
  $first = Wait-For { (Find $root 'Results').FindFirst([Windows.Automation.TreeScope]::Children, (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::ListItem))) } 10
  $first.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
  Wait-For { (Find $root 'PreviewText').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -like 'Meeting*' } 10 | Out-Null
  Save-Window $app.Handle 'search'

  Invoke $root 'PreviewButton'
  Search $root 'ext:jpg;png'
  Invoke $root 'GridViewButton'
  Wait-For { -not (Find $root 'ResultsGrid').Current.IsOffscreen } 10 | Out-Null
  Start-Sleep -Seconds 2 # thumbnails are decoded in the background
  Save-Window $app.Handle 'grid'
  Invoke $root 'ListViewButton'

  Invoke $root 'CreditLink'
  # Owned dialogs are children of the main window in the UIA tree.
  $about = Wait-For { $root.FindFirst([Windows.Automation.TreeScope]::Children, (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::Window))) } 10
  Give-Back-Focus
  Save-Window ([IntPtr]$about.Current.NativeWindowHandle) 'about'
  Invoke $about 'AboutOk'
  Close-App $app
}
finally {
  foreach ($a in $apps) { if (-not $a.Process.HasExited) { $a.Process.Kill() } }
  if ($drive) { & "$env:SystemRoot\System32\subst.exe" $drive /d }
  Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
