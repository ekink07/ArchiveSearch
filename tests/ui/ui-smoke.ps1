# Arşiv Ara arayüz duman testi: derlenmiş uygulamayı geçici bir veri klasörüyle başlatır ve UI Automation ile
# (klavye/fare kullanmadan, odak çalmadan) ana işlevleri dener. Çıkış kodu = başarısız denetim sayısı.
#   pwsh tests/ui/ui-smoke.ps1 [-Exe <ArsivAra.exe>] [-Screenshots <klasör>]
param(
  [string]$Exe = (Join-Path $PSScriptRoot '..\..\src\ArsivAra.App\bin\Release\net9.0-windows\ArsivAra.exe'),
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
# Test klavye kullanmaz; pencere yine de öne gelirse odak hemen kullanıcının penceresine geri verilir (yazdıkları uygulamaya gitmesin).
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
'{ "Language": "tr", "ShowPreview": false, "IconSize": 16 }' | Set-Content (Join-Path $data 'settings.json') -Encoding utf8
$env:ARSIVARA_DATA = $data

$script:pass = 0; $script:fail = 0
function Check([bool]$ok, [string]$what) {
  if ($ok) { $script:pass++; Write-Host "  OK   $what" -ForegroundColor Green }
  else { $script:fail++; Write-Host "  HATA $what" -ForegroundColor Red }
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
  # Sahipli iletişim kutuları UIA ağacında masaüstünün değil sahip pencerenin çocuğu olarak görünür.
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
  if (-not $h) { throw 'Ana pencere açılmadı' }
  [UiWin]::SetWindowPos($h, [IntPtr](-1), 40, 40, 1194, 727, 0x10) | Out-Null # en üstte, etkinleştirmeden
  Give-Back-Focus
  return @{ Process = $p; Handle = $h; Root = $AE::FromHandle($h) }
}

$archives = 'zip-utf8.zip', '7z-solid.7z', 'ic-ice.zip', 'rar5-sifreli-baslik.rar', 'joliet.iso', 'kaynak.tar.gz' |
  ForEach-Object { Join-Path $fixtures $_ }

Write-Host "Arşiv Ara UI duman testi`n  exe: $Exe`n  veri: $data"
$app = Start-App $archives
$root = $app.Root
try {
  Write-Host '- Açılış, logo ve imza'
  Check ([bool](Wait-For { (TextOf $root 'LibraryStatus') -match '^6 arşiv' } 30)) "6 arşiv eklendi ($(TextOf $root 'LibraryStatus'))"
  Check ([bool](Find $root 'BrandLogo')) 'Üst çubukta logo var'
  $credit = Find $root 'CreditLink'
  Check ($credit -and $credit.Current.Name -eq 'Created By MaledictusMaggot®') 'Durum satırında "Created By MaledictusMaggot®" imzası'
  $archiveList = Find $root 'ArchiveList'
  $texts = $archiveList.FindAll([Windows.Automation.TreeScope]::Descendants, (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::Text))) | ForEach-Object { $_.Current.Name }
  Check ([bool]($texts | Where-Object { $_ -like '*Şifreli*' })) 'Başlığı şifreli RAR "Şifreli: parola" olarak görünüyor'
  Check ([bool]($texts | Where-Object { $_ -like '*3 iç arşiv*' })) 'İç içe arşivler sayıldı (ic-ice.zip: 3 iç arşiv)'
  Snap $app.Handle 'ana-liste'

  Write-Host '- Arama dili'
  Search $root 'sirket'
  Check ([bool](Wait-For { (ResultCount $root) -eq 7 } 10)) "sirket → 7 sonuç (iç içe ve ISO dahil): $(TextOf $root 'ResultStatus')"
  Search $root 'sirket|sozlesme arsiv:zip'
  Check ([bool](Wait-For { (ResultCount $root) -eq 8 } 10)) "VEYA + arsiv: → 8 sonuç: $(TextOf $root 'ResultStatus')"
  Search $root '*.png !istanbul'
  Check ([bool](Wait-For { (ResultCount $root) -eq 0 } 10)) "Joker + DEĞİL → 0 sonuç: $(TextOf $root 'ResultStatus')"
  Search $root 'boyut:>40kb'
  Check ([bool](Wait-For { (ResultCount $root) -eq 10 } 10)) "boyut:>40kb → 10 sonuç (7 rastgele.bin + 3 iç arşiv): $(TextOf $root 'ResultStatus')"
  Search $root 'boyut:abc'
  Check ([bool](Wait-For { (TextOf $root 'ResultStatus') -like 'Geçersiz boyut*' } 10)) "Geçersiz değer durum satırında açıklanıyor"

  Write-Host '- Sıralama ve önizleme'
  Search $root 'okubeni|rastgele dosya:'
  Wait-For { (ResultCount $root) -gt 0 } 10 | Out-Null
  Invoke $root 'SortSize'
  Check ([bool](Wait-For { (FirstResult $root).Current.Name -eq 'rastgele.bin' } 10)) 'Boyut başlığına tıklayınca büyükten küçüğe sıralandı'
  Invoke $root 'PreviewButton'
  Search $root 'ad:="Sözleşme.txt" arsiv:zip-utf8'
  Wait-For { (ResultCount $root) -eq 1 } 10 | Out-Null
  (FirstResult $root).GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
  Check ([bool](Wait-For { (TextOf $root 'PreviewTitle') -eq 'Sözleşme.txt' } 10)) 'Önizleme seçili dosyanın adını gösteriyor'
  Check ([bool](Wait-For { (Find $root 'PreviewText' 2).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -like '*İSTANBUL şubesi*' } 10)) 'Önizleme metin içeriğini gösteriyor'
  Snap $app.Handle 'onizleme'

  Write-Host '- Kareli görünüm'
  Invoke $root 'PreviewButton'
  Search $root ''
  Invoke $root 'GridViewButton'
  Check ([bool](Wait-For { (Find $root 'ResultsGrid' 2).Current.IsOffscreen -eq $false } 5)) 'Kareli görünüme geçildi'
  Snap $app.Handle 'kareli'
  Invoke $root 'ListViewButton'
  Check ([bool](Wait-For { (Find $root 'Results' 2).Current.IsOffscreen -eq $false } 5)) 'Listeye geri dönüldü'
  Invoke $root 'PreviewButton'

  Write-Host '- İçerik araması, sürümler, kopyalar'
  Search $root 'icerik:zumrut'
  Check ([bool](Wait-For { (TextOf $root 'ResultStatus') -match '^7 sonuç · ' } 20)) "icerik:zumrut → 7 dosya: $(TextOf $root 'ResultStatus')"
  Search $root 'ad:="Şirket Raporu 2023.txt" dosya:'
  Check ([bool](Wait-For { (ResultCount $root) -eq 7 } 10)) "Aynı adlı sürümler → 7"
  Search $root 'kopya: okubeni'
  Check ([bool](Wait-For { (ResultCount $root) -ge 7 } 10)) "kopya: → $(ResultCount $root) kopya"

  Write-Host '- Dil değiştirme (Ayarlar)'
  Invoke $root 'SettingsButton'
  $settings = Window 'Ayarlar'
  Check ([bool]$settings) 'Ayarlar penceresi açıldı'
  Snap ([IntPtr]$settings.Current.NativeWindowHandle) 'ayarlar'
  $combo = Find $settings 'LanguageBox'
  $combo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
  $english = Wait-For { $combo.FindFirst([Windows.Automation.TreeScope]::Descendants, (Cond $AE::NameProperty 'English')) } 5
  $english.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
  $combo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
  Check ([bool](Wait-For { $settings.Current.Name -eq 'Settings' } 5)) 'Ayarlar penceresi anında İngilizceye geçti'
  Invoke $settings 'SettingsClose'
  Check ([bool](Wait-For { (Find $root 'SearchBox').Current.Name -eq 'Search inside archives…' } 5)) 'Ana pencere İngilizce (arama kutusu)'
  Check ((TextOf $root 'LibraryStatus') -notmatch 'arşiv') "Durum satırında Türkçe kalıntı yok ($(TextOf $root 'LibraryStatus'))"
  Check ((Find $root 'SortName').Current.Name -eq 'Name') 'Sütun başlıkları İngilizce'
  Snap $app.Handle 'ingilizce'

  Write-Host '- Hakkında'
  Invoke $root 'CreditLink'
  $about = Window 'About Arşiv Ara'
  Check ([bool]$about) 'İmzaya tıklayınca Hakkında açıldı'
  if ($about) {
    Check ((TextOf $about 'AboutCredit') -like '*MaledictusMaggot®*') 'Hakkında: Created By MaledictusMaggot®'
    Check ((TextOf $about 'AboutVersion') -match '^Version \d') "Hakkında: $(TextOf $about 'AboutVersion')"
    Check ((TextOf $about 'AboutManufacturer') -eq 'Publisher: Verlora Studios') "Hakkında: $(TextOf $about 'AboutManufacturer')"
    Check ((TextOf $about 'AboutLicenseKind') -eq 'MIT') 'Hakkında: Lisans bölümü (MIT)'
    Check ((TextOf $about 'AboutCopyright') -like 'Copyright © 20*') "Hakkında: $(TextOf $about 'AboutCopyright')"
    Snap ([IntPtr]$about.Current.NativeWindowHandle) 'hakkinda'

    Invoke $about 'AboutLicenseButton'
    $license = Window 'Arşiv Ara license' 10 $about
    Check ([bool]$license) 'Lisans metni penceresi açıldı'
    if ($license) {
      $text = (Find $license 'LicenseText').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
      Check ($text -like 'MIT License*Permission is hereby granted*') 'Lisans: LICENSE metninin tamamı gösteriliyor'
      Check ((TextOf $license 'LicenseSubtitle') -like '*Verlora Studios*') "Lisans: $(TextOf $license 'LicenseSubtitle')"
      (Find $license 'NoticesTab').GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
      $notices = Wait-For { (Find $license 'NoticesText' 2).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value } 5
      Check ($notices -like '*SharpCompress*') 'Lisans: üçüncü taraf bileşenler sekmesi'
      Snap ([IntPtr]$license.Current.NativeWindowHandle) 'lisans'
      Invoke $license 'LicenseClose'
      Check ([bool](Wait-For { -not (Window 'Arşiv Ara license' 1 $about) } 5)) 'Lisans penceresi kapandı'
    }
    Invoke $about 'AboutOk'
    Check ([bool](Wait-For { -not (Window 'About Arşiv Ara' 1) } 5)) 'Hakkında kapandı'
  }

  Write-Host '- Tek örnek: ikinci açılış dosyayı açık olana iletir'
  $second = Start-Process $Exe -ArgumentList "`"$(Join-Path $fixtures 'kaynak.tar.bz2')`"" -PassThru
  Check ($second.WaitForExit(10000)) 'İkinci süreç hemen kapandı'
  Check ([bool](Wait-For { (TextOf $root 'LibraryStatus') -match '^7 archives' } 20)) "Dosya açık olan pencereye eklendi ($(TextOf $root 'LibraryStatus'))"

  Write-Host '- Kapatma ve kalıcılık'
  $root.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
  Check ($app.Process.WaitForExit(10000)) 'Program kapandı'
  $app = Start-App @()
  $root = $app.Root
  Check ([bool](Wait-For { (TextOf $root 'LibraryStatus') -match '^7 archives' } 20)) "Yeniden açılışta kütüphane ve dil korundu ($(TextOf $root 'LibraryStatus'))"
  Check (Test-Path (Join-Path $data 'index.bin')) 'index.bin veri klasöründe'
  Check (-not (Test-Path (Join-Path (Split-Path $Exe) 'ArsivAra-veri'))) "exe'nin yanına ArsivAra-veri klasörü açılmadı"
  Check (Test-Path (Join-Path $data 'gunluk')) 'Günlük klasörü oluştu'
}
finally {
  if ($app -and -not $app.Process.HasExited) { $app.Process.Kill() }
  Get-Process ArsivAra -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Exe } | Stop-Process -Force # yarıda kalan test örnekleri
  Remove-Item $data -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host "`nSonuç: $script:pass başarılı, $script:fail başarısız"
exit $script:fail
