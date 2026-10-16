# Produces the sample archives for the Arşiv Ara tests: tests/ArsivAra.Core.Tests/Fixtures
# Requires WinRAR (Rar.exe: RAR5/RAR4 and multi-part archives), 7-Zip (7z.exe) and Windows' IMAPI2 component (ISO).
# The output is committed, so the tests run without these tools (also in CI). To regenerate: pwsh tools/make_test_fixtures.ps1
param(
  [string]$Rar = "$env:ProgramFiles\WinRAR\Rar.exe",
  [string]$SevenZip = "$env:ProgramFiles\7-Zip\7z.exe"
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression
foreach ($tool in $Rar, $SevenZip) { if (-not (Test-Path $tool)) { throw "Not found: $tool" } }

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$out = Join-Path $root 'tests\ArsivAra.Core.Tests\Fixtures'
$work = Join-Path ([IO.Path]::GetTempPath()) ('arsivara-fixture-' + [guid]::NewGuid().ToString('N'))
$src = Join-Path $work 'kaynak'
New-Item -ItemType Directory -Force $out, $src | Out-Null
Get-ChildItem $out -File | Remove-Item -Force

function Write-Utf8([string]$path, [string]$text) {
  New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
  [IO.File]::WriteAllText($path, $text, (New-Object Text.UTF8Encoding $false))
}

# ---- Source tree (Turkish names, an empty folder, incompressible data) ----
Write-Utf8 "$src\okubeni.txt" "Arşiv Ara test dosyası.`r`n"
Write-Utf8 "$src\Belgeler\Şirket Raporu 2023.txt" "Şirket raporu 2023. Gizli kelime: zümrüt.`r`n"
Write-Utf8 "$src\Belgeler\Sözleşme.txt" "Sözleşme metni, İSTANBUL şubesi.`r`n"
New-Item -ItemType Directory -Force "$src\Fotoğraflar\İstanbul", "$src\Boş Klasör", "$src\Veri" | Out-Null
$bmp = New-Object Drawing.Bitmap 16, 16
for ($x = 0; $x -lt 16; $x++) { for ($y = 0; $y -lt 16; $y++) { $bmp.SetPixel($x, $y, [Drawing.Color]::FromArgb(255, 16 * $x, 100, 16 * $y)) } }
$bmp.Save("$src\Fotoğraflar\İstanbul\kule.png", [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
$bytes = New-Object byte[] (48 * 1024); (New-Object Random 1234).NextBytes($bytes)
[IO.File]::WriteAllBytes("$src\Veri\rastgele.bin", $bytes)
# Fixed date: the tests also exercise the date filter
Get-ChildItem $src -Recurse -File | ForEach-Object { $_.LastWriteTime = [datetime]'2024-06-15 10:30:00' }

function Run([string]$exe, [string[]]$arguments, [string]$cwd) {
  $p = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $cwd -NoNewWindow -Wait -PassThru
  if ($p.ExitCode -ne 0) { throw "$exe $($arguments -join ' ') exit code $($p.ExitCode)" }
}

# ---- RAR (WinRAR) ----
Run $Rar @('a', '-idq', '-r', '-ma5', '-qo+', "`"$out\rar5-qo.rar`"", '*') $src
Run $Rar @('a', '-idq', '-r', '-ma5', '-s', '-qo+', "`"$out\rar5-solid.rar`"", '*') $src
Run $Rar @('a', '-idq', '-r', '-ma5', '-m0', '-v20k', "`"$out\rar5-cok-parca.rar`"", '*') $src
Run $Rar @('a', '-idq', '-r', '-ma4', '-m0', '-v20k', '-vn', "`"$out\rar4-cok-parca.rar`"", '*') $src
Run $Rar @('a', '-idq', '-r', '-ma5', '-hpParola123', "`"$out\rar5-sifreli-baslik.rar`"", '*') $src
Run $Rar @('a', '-idq', '-r', '-ma5', '-pParola123', "`"$out\rar5-sifreli-icerik.rar`"", '*') $src

# ---- 7-Zip ----
Run $SevenZip @('a', '-bso0', '-bsp0', '-t7z', '-ms=on', "`"$out\7z-solid.7z`"", '*') $src
Run $SevenZip @('a', '-bso0', '-bsp0', '-t7z', '-pParola123', '-mhe=on', "`"$out\7z-sifreli.7z`"", '*') $src
Run $SevenZip @('a', '-bso0', '-bsp0', '-tzip', '-pParola123', '-mem=AES256', "`"$out\zip-aes.zip`"", '*') $src
Run $SevenZip @('a', '-bso0', '-bsp0', '-ttar', "`"$out\kaynak.tar`"", '*') $src
Run $SevenZip @('a', '-bso0', '-bsp0', '-tgzip', "`"$out\kaynak.tar.gz`"", "`"$out\kaynak.tar`"") $out
Run $SevenZip @('a', '-bso0', '-bsp0', '-tbzip2', "`"$out\kaynak.tar.bz2`"", "`"$out\kaynak.tar`"") $out
Run $SevenZip @('a', '-bso0', '-bsp0', '-txz', "`"$out\kaynak.tar.xz`"", "`"$out\kaynak.tar`"") $out
Copy-Item "$out\kaynak.tar.gz" "$out\kaynak.tgz"

# ---- ZIP (.NET): with the UTF-8 flag and with old DOS Turkish (CP857) names ----
[Text.Encoding]::RegisterProvider([Text.CodePagesEncodingProvider]::Instance)
function New-Zip([string]$path, [Text.Encoding]$encoding) {
  $fs = [IO.File]::Create($path)
  $zip = New-Object IO.Compression.ZipArchive($fs, [IO.Compression.ZipArchiveMode]::Create, $false, $encoding)
  foreach ($f in Get-ChildItem $src -Recurse) {
    $rel = $f.FullName.Substring($src.Length + 1).Replace('\', '/')
    if ($f.PSIsContainer) { if (-not (Get-ChildItem $f.FullName)) { [void]$zip.CreateEntry("$rel/") } ; continue }
    $e = $zip.CreateEntry($rel); $e.LastWriteTime = $f.LastWriteTime
    $s = $e.Open(); $b = [IO.File]::ReadAllBytes($f.FullName); $s.Write($b, 0, $b.Length); $s.Dispose()
  }
  $zip.Dispose(); $fs.Dispose()
}
New-Zip "$out\zip-utf8.zip" ([Text.Encoding]::UTF8)
New-Zip "$out\zip-cp857.zip" ([Text.Encoding]::GetEncoding(857))

# ---- Nested archives: zip, tar.gz and 7z inside a zip ----
$nest = Join-Path $work 'ic'
New-Item -ItemType Directory -Force "$nest\arsivler" | Out-Null
Copy-Item "$src\okubeni.txt" "$nest\okubeni.txt"
Copy-Item "$out\zip-utf8.zip" "$nest\arsivler\ic.zip"
Copy-Item "$out\kaynak.tar.gz" "$nest\arsivler\ic.tar.gz"
Copy-Item "$out\7z-solid.7z" "$nest\arsivler\ic.7z"
Run $SevenZip @('a', '-bso0', '-bsp0', '-tzip', "`"$out\ic-ice.zip`"", '*') $nest

# ---- ISO (Windows IMAPI2): ISO 9660 + Joliet, UDF only, ISO 9660 only ----
Add-Type -TypeDefinition @"
using System; using System.IO; using System.Runtime.InteropServices.ComTypes;
public static class ImapiIso {
  public static void Save(object stream, string path, int blockSize, int totalBlocks) {
    var s = (IStream)stream; var buf = new byte[blockSize];
    using (var fs = File.Create(path))
      for (int i = 0; i < totalBlocks; i++) { s.Read(buf, blockSize, IntPtr.Zero); fs.Write(buf, 0, blockSize); }
  }
}
"@
function New-Iso([string]$path, [int]$fileSystems) {
  $fsi = New-Object -ComObject IMAPI2FS.MsftFileSystemImage
  $fsi.FileSystemsToCreate = $fileSystems
  if ($fileSystems -band 4) { $fsi.UDFRevision = 0x102 }
  $fsi.VolumeName = 'ARSIVARA'
  $fsi.Root.AddTree($src, $false)
  $img = $fsi.CreateResultImage()
  [ImapiIso]::Save($img.ImageStream, $path, $img.BlockSize, $img.TotalBlocks)
}
New-Iso "$out\joliet.iso" 3
New-Iso "$out\udf.iso" 4
New-Iso "$out\iso9660.iso" 1

Remove-Item $work -Recurse -Force
Get-ChildItem $out | Select-Object Name, Length | Format-Table -AutoSize
