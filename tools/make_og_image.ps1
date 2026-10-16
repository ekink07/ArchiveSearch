param([string]$Browser)
# Produces the social preview card: tools/og-card.html -> docs/images/og.png (1200x630).
# The card uses the logo and docs/images/search.png; run it again when the screenshots change (tools/make_screenshots.ps1).
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $Browser) {
  $Browser = @(
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
    "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe"
  ) | Where-Object { Test-Path $_ } | Select-Object -First 1
  if (-not $Browser) { throw 'Edge or Chrome is needed to render the card (pass its path with -Browser).' }
}

$page = Join-Path $PSScriptRoot 'og-card.html'
$png = Join-Path $root 'docs\images\og.png'
$profile = Join-Path ([IO.Path]::GetTempPath()) ('arsivara-og-' + [guid]::NewGuid().ToString('N'))
try {
  if (Test-Path $png) { Remove-Item $png }
  # Separate profile so it does not attach to an open browser window.
  $browserArgs = '--headless=new', '--disable-gpu', '--hide-scrollbars', '--no-first-run', '--force-device-scale-factor=1',
    '--window-size=1200,630', "--user-data-dir=""$profile""", "--screenshot=""$png""", ([Uri]$page).AbsoluteUri
  Start-Process -FilePath $Browser -ArgumentList $browserArgs -Wait -WindowStyle Hidden
  if (-not (Test-Path $png)) { throw "The browser produced no screenshot: $Browser" }
}
finally {
  Remove-Item $profile -Recurse -Force -ErrorAction SilentlyContinue
}
"OG: $png ($((Get-Item $png).Length) bytes)"
