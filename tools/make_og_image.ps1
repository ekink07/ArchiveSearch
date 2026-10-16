param([string]$Browser)
# Sosyal medya önizleme kartını üretir: tools/og-card.html -> docs/images/og.png (1200x630).
# Kart logoyu ve docs/images/search.png'yi kullanır; ekran görüntüleri değişince (tools/make_screenshots.ps1) yeniden çalıştırın.
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $Browser) {
  $Browser = @(
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
    "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe"
  ) | Where-Object { Test-Path $_ } | Select-Object -First 1
  if (-not $Browser) { throw 'Kartı çizmek için Edge ya da Chrome gerekli (-Browser ile yol verilebilir).' }
}

$page = Join-Path $PSScriptRoot 'og-card.html'
$png = Join-Path $root 'docs\images\og.png'
$profile = Join-Path ([IO.Path]::GetTempPath()) ('arsivara-og-' + [guid]::NewGuid().ToString('N'))
try {
  if (Test-Path $png) { Remove-Item $png }
  # Ayrı profil: açık bir tarayıcı penceresine bağlanmasın.
  $browserArgs = '--headless=new', '--disable-gpu', '--hide-scrollbars', '--no-first-run', '--force-device-scale-factor=1',
    '--window-size=1200,630', "--user-data-dir=""$profile""", "--screenshot=""$png""", ([Uri]$page).AbsoluteUri
  Start-Process -FilePath $Browser -ArgumentList $browserArgs -Wait -WindowStyle Hidden
  if (-not (Test-Path $png)) { throw "Tarayıcı ekran görüntüsü üretmedi: $Browser" }
}
finally {
  Remove-Item $profile -Recurse -Force -ErrorAction SilentlyContinue
}
"OG: $png ($((Get-Item $png).Length) bayt)"
