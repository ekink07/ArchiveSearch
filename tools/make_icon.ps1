param([string]$Logo = (Join-Path $PSScriptRoot '..\docs\images\logo-arsiv.svg'), [string]$Browser)
# Arşiv Ara's icons are produced from the main logo (docs/images/logo-arsiv.svg):
#   src/ArsivAra.App/Assets/app.ico        exe, window and taskbar (16–256 px)
#   src/ArsivAra.App/Assets/logo.png       About window and empty screen (256 px)
#   docs/images/icon-64.png, icon-256.png  website and README
# Edge/Chrome renders the SVG. Each size is rendered separately, so small sizes are sharper than a scaled-down large image.
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Logo = (Resolve-Path $Logo).Path
if (-not $Browser) {
  $Browser = @(
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
    "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe"
  ) | Where-Object { Test-Path $_ } | Select-Object -First 1
  if (-not $Browser) { throw 'Edge or Chrome is needed to render the SVG (pass its path with -Browser).' }
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$gap = 8
$tmp = Join-Path ([IO.Path]::GetTempPath()) ('arsivara-icon-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $tmp | Out-Null
try {
  # All sizes are drawn side by side on one page and cut out of a single screenshot.
  Copy-Item $Logo (Join-Path $tmp 'logo.svg')
  $x = 0
  $imgs = foreach ($n in $sizes) { "<img src=""logo.svg"" style=""left:${x}px;width:${n}px;height:${n}px"">"; $x += $n + $gap }
  $page = Join-Path $tmp 'sheet.html'
  [IO.File]::WriteAllText($page, '<!doctype html><meta charset="utf-8"><style>html,body{margin:0;background:transparent}img{position:absolute;top:0}</style>' + ($imgs -join ''))

  $sheetPng = Join-Path $tmp 'sheet.png'
  # Separate profile so it does not attach to an open browser window.
  $browserArgs = '--headless=new', '--disable-gpu', '--hide-scrollbars', '--no-first-run', '--force-device-scale-factor=1',
    '--default-background-color=00000000', "--window-size=$x,256", "--user-data-dir=""$tmp\profile""",
    "--screenshot=""$sheetPng""", ([Uri]$page).AbsoluteUri
  Start-Process -FilePath $Browser -ArgumentList $browserArgs -Wait -WindowStyle Hidden
  if (-not (Test-Path $sheetPng)) { throw "The browser produced no screenshot: $Browser" }

  $sheet = New-Object Drawing.Bitmap $sheetPng
  $pngs = @{}
  $x = 0
  foreach ($n in $sizes) {
    $b = $sheet.Clone((New-Object Drawing.Rectangle $x, 0, $n, $n), [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $ms = New-Object IO.MemoryStream; $b.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
    $pngs[$n] = $ms.ToArray()
    $x += $n + $gap
  }
  $sheet.Dispose()
}
finally {
  Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

$assets = Join-Path $root 'src\ArsivAra.App\Assets'
$images = Join-Path $root 'docs\images'

# ICO file: header + directory + PNG data (Windows Vista and later support PNG inside ICO)
$ico = Join-Path $assets 'app.ico'
$fs = [IO.File]::Create($ico); $w = New-Object IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($n in $sizes) {
  $len = $pngs[$n].Length
  $w.Write([byte]($(if ($n -ge 256) { 0 } else { $n }))); $w.Write([byte]($(if ($n -ge 256) { 0 } else { $n })))
  $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32)
  $w.Write([uint32]$len); $w.Write([uint32]$offset); $offset += $len
}
foreach ($n in $sizes) { $w.Write($pngs[$n]) }
$w.Close()

[IO.File]::WriteAllBytes((Join-Path $assets 'logo.png'), $pngs[256])
[IO.File]::WriteAllBytes((Join-Path $images 'icon-256.png'), $pngs[256])
[IO.File]::WriteAllBytes((Join-Path $images 'icon-64.png'), $pngs[64])

"ICO: $ico ($((Get-Item $ico).Length) bytes, $($sizes -join '/') px)"
"PNG: $(Join-Path $assets 'logo.png'), $(Join-Path $images 'icon-256.png'), $(Join-Path $images 'icon-64.png')"
