# Belgelerdeki ekran görüntülerini üretir: docs/images/{empty,search,grid,about}.png
#   pwsh tools/make_screenshots.ps1 [-Exe <ArsivAra.exe>]
# Örnek arşivler geçici klasörde .NET ve Windows'un tar.exe'siyle hazırlanır; 7z, şifreli RAR ve ISO için test arşivleri kopyalanır.
# Program geçici bir veri klasörüyle açılır, UI Automation ile yönetilir, pencere PrintWindow ile yakalanır:
# klavye, fare ve odak kullanılmaz; üstteki pencereler ya da fare imleci görüntüye girmez.
param(
  [string]$Exe = (Join-Path $PSScriptRoot '..\src\ArsivAra.App\bin\Release\net10.0-windows\ArsivAra.exe'),
  [string]$Out = (Join-Path $PSScriptRoot '..\docs\images'),
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
$work = Join-Path ([IO.Path]::GetTempPath()) ('arsivara-ekran-' + [guid]::NewGuid().ToString('N'))
$samples = Join-Path $work 'ornekler'
New-Item -ItemType Directory -Force $samples | Out-Null

# ---------------------------------------------------------------- örnek arşivler

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

# $items: @( @('yol/ad', <metin ya da bayt>, 'yyyy-MM-dd HH:mm'), ... ); '/' ile biten yol klasördür.
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
Toplantı Notları - 12 Eylül 2025

Katılımcılar: Ayşe, Mehmet, Zeynep, Can

1. Yedekleme düzeni
   - Haftalık yedekler pazar gecesi yedekler.zip'e eklenecek.
   - Eski yedekler silinmeyecek; Arşiv Ara ile içlerinde aranacak.

2. Bütçe 2025
   - Tablolar/Bütçe 2025.xlsx güncellendi, ekim sonuna kadar onay bekleniyor.

3. İstanbul şubesi
   - Sözleşme yenilemesi için hukuk birimine dönüş yapılacak.

Sonraki toplantı: 26 Eylül 2025, 10:00
"@
$notes2023 = @"
Toplantı Notları - 3 Kasım 2023

1. Proje yedeği alındı (2023/proje_yedegi.zip).
2. Şirket raporu taslağı paylaşıldı.
"@

New-Zip "$samples\eski_cp857.zip" @(
  @('eski/Belgeler/Şirket Raporu 2019.pdf', '%PDF-1.4 sirket raporu 2019', '2019-04-02 16:40'),
  @('eski/Fotoğraflar/İstanbul Gezisi/IMG_0001.jpg', (New-Photo 1), '2018-07-21 11:05'),
  @('eski/Müşteri Listesi.xlsx', 'musteri listesi', '2019-02-11 09:12'),
  @('eski/Faturalar/2019/Ocak/fatura_00123.pdf', '%PDF-1.4 fatura 00123', '2019-01-31 17:30'),
  @('eski/ığüşöç ĞÜŞİÖÇ.txt', 'Türkçe karakter testi: ığüşöç ĞÜŞİÖÇ', '2019-05-06 10:00')
) ([Text.Encoding]::GetEncoding(857))

$gallery = foreach ($n in 1..16) { , @("Fotoğraflar/Gezi 2025/IMG_$('{0:D4}' -f $n).$(if ($n % 2) { 'jpg' } else { 'png' })", (New-Photo $n), "2025-08-$('{0:D2}' -f (2 + $n)) 1$($n % 10):2$($n % 6)") }
$gallery += , @('Fotoğraflar/Boş klasör/', '', '2025-08-20 12:00')
foreach ($f in @(@('Teklif_2025.docx', 'teklif'), @('Fiyat Listesi.xlsx', 'fiyat'), @('Kurulum Kılavuzu.pdf', '%PDF kilavuz'), @('Sunum.pptx', 'sunum'),
    @('Montaj.dwg', 'dwg'), @('Parça-01.step', 'ISO-10303-21;'), @('okubeni.txt', 'okubeni'), @('tanıtım.mp4', [byte[]]::new(2048)),
    @('Program.cs', 'Console.WriteLine("Merhaba");'))) {
  $gallery += , @("Dosyalar/$($f[0])", $f[1], '2025-06-18 14:45')
}
New-Zip "$samples\galeri.zip" $gallery

New-Zip "$samples\utf8_turkce.zip" @(
  @('Belgeler/Şirket Raporu 2024.pdf', '%PDF-1.4 sirket raporu 2024', '2025-02-14 18:20'),
  @('Belgeler/Sözleşme.pdf', '%PDF-1.4 sozlesme', '2025-03-03 11:15'),
  @('Tablolar/Bütçe 2025.xlsx', 'butce', '2025-09-10 08:55'),
  @('Notlar/Toplantı Notları.txt', $notes2025, '2025-09-12 15:30'),
  @('Fotoğraflar/Kapadokya.jpg', (New-Photo 5), '2025-05-01 07:10')
)

# İç içe: yedekler.zip > 2023/proje_yedegi.zip ve 2024/kaynak.tar.gz
$stage = Join-Path $work 'kaynak'
New-Item -ItemType Directory -Force "$stage\kaynak" | Out-Null
[IO.File]::WriteAllText("$stage\kaynak\main.c", "#include <stdio.h>`nint main(void) { puts(""merhaba""); return 0; }`n")
[IO.File]::WriteAllText("$stage\kaynak\util.h", "#pragma once`nint topla(int a, int b);`n")
[IO.File]::WriteAllText("$stage\kaynak\notlar.txt", ("derleme notlari`n" * 60))
Get-ChildItem $stage -Recurse | ForEach-Object { $_.LastWriteTime = [datetime]'2024-04-09 13:00' }
$tar = Join-Path $work 'kaynak.tar.gz'
& "$env:SystemRoot\System32\tar.exe" -czf $tar -C $stage kaynak
if ($LASTEXITCODE) { throw 'tar.exe kaynak.tar.gz üretemedi' }
New-Zip "$work\proje_yedegi.zip" @(
  @('Belgeler/Şirket Raporu 2023.pdf', '%PDF-1.4 sirket raporu 2023', '2023-10-30 17:45'),
  @('Notlar/Toplantı Notları.txt', $notes2023, '2023-11-03 10:20'),
  @('Kaynak/Program.cs', 'Console.WriteLine("Merhaba");', '2023-09-18 21:05')
)
New-Zip "$samples\yedekler.zip" @(
  @('2023/proje_yedegi.zip', [IO.File]::ReadAllBytes("$work\proje_yedegi.zip"), '2023-11-03 18:00'),
  @('2024/kaynak.tar.gz', [IO.File]::ReadAllBytes($tar), '2024-04-09 13:05'),
  @('okubeni.txt', 'Haftalık yedekler.', '2025-09-14 23:00')
)
Copy-Item $tar "$samples\kaynak.tar.gz"
Copy-Item "$fixtures\7z-solid.7z" "$samples\yedek.7z"
Copy-Item "$fixtures\udf.iso" "$samples\Kurulum_Diski.iso"
Copy-Item "$fixtures\rar5-sifreli-baslik.rar" "$samples\Muhasebe_2024.rar"
$archives = Get-ChildItem $samples -File | Sort-Object Name | ForEach-Object FullName
Write-Host "Örnek arşivler: $($archives.Count)"

# ---------------------------------------------------------------- program ve yakalama

$AE = [Windows.Automation.AutomationElement]
$userWindow = [Shot]::GetForegroundWindow()
function Cond($prop, $value) { New-Object Windows.Automation.PropertyCondition ($prop, $value) }
function Wait-For([scriptblock]$probe, [int]$seconds = 15) {
  $sw = [Diagnostics.Stopwatch]::StartNew()
  while ($sw.Elapsed.TotalSeconds -lt $seconds) {
    try { $r = & $probe; if ($r) { return $r } } catch { }
    Start-Sleep -Milliseconds 200
  }
  throw "Zaman aşımı: $probe"
}
function Find($el, [string]$id) { Wait-For { $el.FindFirst([Windows.Automation.TreeScope]::Descendants, (Cond $AE::AutomationIdProperty $id)) } 10 }
function Invoke($el, [string]$id) { (Find $el $id).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Give-Back-Focus }
function Search($el, [string]$text) { (Find $el 'SearchBox').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($text); Give-Back-Focus }
function Give-Back-Focus {
  [uint32]$fgPid = 0; [Shot]::GetWindowThreadProcessId([Shot]::GetForegroundWindow(), [ref]$fgPid) | Out-Null
  if ((Get-Process -Id $fgPid -ErrorAction SilentlyContinue).Path -eq $Exe) { [Shot]::SetForegroundWindow($userWindow) | Out-Null }
}

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

# Görünür çerçeve tam Width x Height olsun; pencere en alta (HWND_BOTTOM), etkinleştirmeden.
function Resize([IntPtr]$h) {
  $o = [Shot]::Outer($h); $f = [Shot]::Frame($h)
  $cx = $Width + (($o.R - $o.L) - ($f.R - $f.L)); $cy = $Height + (($o.B - $o.T) - ($f.B - $f.T))
  [Shot]::SetWindowPos($h, [IntPtr]1, 40, 40, $cx, $cy, 0x10) | Out-Null
}

function Save-Window([IntPtr]$h, [string]$name) {
  Start-Sleep -Milliseconds 900 # geçişler ve küçük resimler otursun
  Give-Back-Focus # pencere kendiliğinden öne geldiyse odak kullanıcıya döner (başlık çubuğu da her görüntüde aynı görünür)
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
try {
  # 1) İlk açılış
  $app = Start-App 'bos' '{ "Language": "tr", "ShowPreview": false }' @(); $apps += $app
  Find $app.Root 'EmptyScanDisks' | Out-Null
  Save-Window $app.Handle 'empty'
  Close-App $app

  # 2) Arama + önizleme, kareler, Hakkında
  $app = Start-App 'dolu' '{ "Language": "tr", "ShowPreview": false, "IconSize": 16 }' $archives; $apps += $app
  $root = $app.Root
  Wait-For { (Find $root 'LibraryStatus').Current.Name -match "^$($archives.Count) arşiv" } 60 | Out-Null

  Invoke $root 'PreviewButton'
  Search $root 'toplanti'
  $first = Wait-For { (Find $root 'Results').FindFirst([Windows.Automation.TreeScope]::Children, (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::ListItem))) } 10
  $first.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
  Wait-For { (Find $root 'PreviewText').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -like 'Toplantı*' } 10 | Out-Null
  Save-Window $app.Handle 'search'

  Invoke $root 'PreviewButton'
  Search $root 'ext:jpg;png'
  Invoke $root 'GridViewButton'
  Wait-For { -not (Find $root 'ResultsGrid').Current.IsOffscreen } 10 | Out-Null
  Start-Sleep -Seconds 2 # küçük resimler arka planda çözülür
  Save-Window $app.Handle 'grid'
  Invoke $root 'ListViewButton'

  Invoke $root 'CreditLink'
  # Sahipli iletişim kutusu UIA ağacında ana pencerenin çocuğudur.
  $about = Wait-For { $root.FindFirst([Windows.Automation.TreeScope]::Children, (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::Window))) } 10
  Give-Back-Focus
  Save-Window ([IntPtr]$about.Current.NativeWindowHandle) 'about'
  Invoke $about 'AboutOk'
  Close-App $app
}
finally {
  foreach ($a in $apps) { if (-not $a.Process.HasExited) { $a.Process.Kill() } }
  Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
