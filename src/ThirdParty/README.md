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
| Kopyalanan | Depodaki `src/SharpCompress` klasörünün tamamı; `SharpCompress.csproj`, `packages.lock.json` ve `SharpCompress.pfx` hariç |
| Proje dosyası | [SharpCompress/SharpCompress.csproj](SharpCompress/SharpCompress.csproj) bizimdir (net9.0, uyarılar kapalı) |

### Yerel değişiklikler

Upstream kaynak .NET 10 SDK ve C# 14 ile derleniyor; Arşiv Ara .NET 9 SDK (C# 13) kullandığı için yalnızca
sözdizimi uyarlandı, davranış değişmedi:

1. 17 dosyadaki C# 14 `extension(Tür ad) { ... }` blokları klasik uzantı metotlarına çevrildi
   (`public static ... Metot(this Tür ad, ...)`; generic alıcıda tür parametresi metoda taşındı).
2. `Writers/WriterOptions.cs`: `CompressionLevel` özelliğindeki `field` anahtar sözcüğü yerine `_compressionLevel` alanı.
3. `Compressors/Rar/RarBLAKE2spStream.cs` ve `.Async.cs`: C# 14'ün diziyi örtük `Span`'e çevirdiği
   `GetCrc().SequenceEqual(...)` çağrıları `GetCrc().AsSpan().SequenceEqual(...)` olarak açıkça yazıldı.
4. `Compressors/Rar/UnRarLicense.cs` eklendi: unRAR lisansının kaynak yorumlarında bulunmasını istediği paragraf.

Proje .NET 10 SDK'ya geçtiğinde 1–3 gereksizleşir; upstream kaynak değiştirilmeden kopyalanabilir.

### Güncelleme

```powershell
git clone --depth 1 --branch <sürüm> https://github.com/adamhathcock/sharpcompress $env:TEMP\sharpcompress
robocopy $env:TEMP\sharpcompress\src\SharpCompress src\ThirdParty\SharpCompress /MIR /XD bin obj `
  /XF SharpCompress.csproj packages.lock.json SharpCompress.pfx UnRarLicense.cs LICENSE.txt
Copy-Item $env:TEMP\sharpcompress\LICENSE.txt src\ThirdParty\SharpCompress\LICENSE.txt
```

`/XF` ile dışlanan dosyalar `/MIR` sırasında silinmez; bizim proje dosyamız ve `UnRarLicense.cs` yerinde kalır.
Ardından yerel değişiklikleri yeniden uygulayın (ya da .NET 10 SDK kullanın), `THIRD-PARTY-NOTICES.txt`'i yeni
bileşenlere göre güncelleyin ve tüm testleri çalıştırın.
