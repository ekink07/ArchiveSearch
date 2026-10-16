# Dışarıdan alınmış kaynak kod

Bu klasördeki kod Arşiv Ara ekibine ait değildir; kaynak kod olarak projeye alınmış ve programın içine derlenir.
Lisansları ve telif bildirimleri kökteki [THIRD-PARTY-NOTICES.txt](../../THIRD-PARTY-NOTICES.txt) dosyasında,
programda ise **Hakkında › Lisans metni › Üçüncü taraf bileşenler** altındadır. Bu dosyalardaki telif ve lisans
notları silinmemelidir.

## SharpCompress 0.50.4

| | |
|---|---|
| Kaynak | https://github.com/adamhathcock/sharpcompress |
| Sürüm | 0.50.4 (etiket `0.50.4`, commit `c083c6efd843a844b0c8f7878787360e815be781`) |
| Lisans | MIT, Copyright (c) 2014 Adam Hathcock ([LICENSE.txt](SharpCompress/LICENSE.txt)); içindeki bileşenlerin kendi lisansları THIRD-PARTY-NOTICES.txt'te |
| Kopyalanan | Depodaki `src/SharpCompress` klasörünün `.cs` dosyaları, **değiştirilmeden** |
| Alınmayan | `SharpCompress.csproj`, `packages.lock.json`, `SharpCompress.pfx` (imza sertifikası) ve geliştirici notu `Compressors/Rar/UnpackV2017/notes.txt` |

Bizim eklediklerimiz:

- [SharpCompress/SharpCompress.csproj](SharpCompress/SharpCompress.csproj): net10.0, C# 14; dışarıdan alınmış kod olduğu için uyarılar ve çözümleyiciler kapalı.
- [SharpCompress/Compressors/Rar/UnRarLicense.cs](SharpCompress/Compressors/Rar/UnRarLicense.cs): unRAR lisansının kaynak yorumlarında
  bulunmasını şart koştuğu paragraf (RAR çözücü kodu unRAR'dan türetilmiştir; Arşiv Ara bu kodu yalnızca açmak için kullanır).
- [SharpCompress/LICENSE.txt](SharpCompress/LICENSE.txt): upstream deposunun kökündeki lisans.

## Güncelleme

```powershell
git clone --depth 1 --branch <sürüm> https://github.com/adamhathcock/sharpcompress $env:TEMP\sharpcompress
robocopy $env:TEMP\sharpcompress\src\SharpCompress src\ThirdParty\SharpCompress /MIR /XD bin obj `
  /XF SharpCompress.csproj packages.lock.json SharpCompress.pfx notes.txt UnRarLicense.cs LICENSE.txt
Copy-Item $env:TEMP\sharpcompress\LICENSE.txt src\ThirdParty\SharpCompress\LICENSE.txt
```

`/XF` ile dışlanan dosyalar `/MIR` sırasında silinmez; bizim proje dosyamız ve `UnRarLicense.cs` yerinde kalır.
Ardından bu dosyadaki sürüm ve commit bilgisini, [THIRD-PARTY-NOTICES.txt](../../THIRD-PARTY-NOTICES.txt)'i (yeni bileşen ya da
lisans eklendiyse) güncelleyin ve tüm testleri çalıştırın. Upstream daha yeni bir .NET SDK isterse `global.json` da güncellenmelidir.
