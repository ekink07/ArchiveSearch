<div align="center">

<img src="docs/images/icon-256.png" width="112" alt="Arşiv Ara simgesi">

# Arşiv Ara

**ZIP, RAR, 7z ve TAR arşivlerinin içinde, açmadan anında arama.**

Arşivlerinizi bırakın, birkaç harf yazın, eşleşen her dosyayı milisaniyeler içinde görün.

[English](README.md) · **Türkçe** · [Deutsch](README.de.md) · [Español](README.es.md)

[![Sürüm](https://img.shields.io/github/v/release/YOUR_GITHUB_USER/ArsivAra?color=F7861F&label=s%C3%BCr%C3%BCm)](https://github.com/YOUR_GITHUB_USER/ArsivAra/releases/latest)
[![İndirme](https://img.shields.io/github/downloads/YOUR_GITHUB_USER/ArsivAra/total?color=0F6BBF&label=indirme)](https://github.com/YOUR_GITHUB_USER/ArsivAra/releases)
![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows&logoColor=white)
![.NET 9](https://img.shields.io/badge/.NET-9-512BD4?logo=dotnet&logoColor=white)
[![Lisans: MIT](https://img.shields.io/badge/lisans-MIT-0B1B2E)](LICENSE)

<img src="docs/images/grid.png" width="860" alt="Fotoğraf küçük resimleri ve Windows dosya türü simgeleriyle kareli görünüm">

</div>

## Neden

Yedekler, proje klasörleri ve indirilenler zamanla onlarca büyük arşive dönüşür. Tek bir dosyayı bulmak için hepsini tek tek açmak gerekir.
Arşiv Ara arşivlerin yalnızca içindekiler listesini okur, bellekte tutar ve hepsinde aynı anda arama yapmanızı sağlar;
[Everything](https://www.voidtools.com/)'in diskteki dosya adlarında yaptığı gibi.

## Özellikler

| | |
|---|---|
| **Sürükle-bırak** | Arşivleri ya da klasörleri pencereye (veya exe'nin üzerine) bırakın. Liste bir sonraki açılış için saklanır. |
| **Anında arama** | 1 milyon dosya adında arama 2–20 ms sürer. Her tuşta sonuçlar güncellenir. |
| **Türkçe duyarlı eşleştirme** | Büyük/küçük harf ve aksan duyarsız: `sirket` yazınca `Şirket`, `istanbul` yazınca `İSTANBUL` bulunur. DOS (CP857) adlı eski ZIP'ler doğru okunur. |
| **Filtreler** | `ext:pdf;docx`, `arsiv:2025`, `klasor:` (sadece klasörler), `dosya:` (sadece dosyalar). Birden fazla kelime VE ile birleşir. |
| **Liste ve kareli görünüm** | Her dosya türü Windows'taki kendi simgesiyle. Kareli görünümde fotoğrafların kendisi görünür. **Ctrl + fare tekerleği** 16'dan 256 px'e yakınlaştırır. |
| **Tek tıkla açma** | Sonuca çift tıklayınca sadece o dosya geçici klasöre çıkarılıp açılır. |
| **Büyük RAR'larda hızlı** | WinRAR'ın *Quick Open* kaydını okur; 22.000 dosyalı 14 GB'lık bir yedek dakikalar yerine saniyeler içinde listelenir. |
| **Taşınabilir** | Tek bir 61 MB exe; kurulum ve .NET gerekmez. Ayarlar USB bellekte exe'nin yanında taşınır. |
| **Gizli** | Tamamen çevrimdışı çalışır. Hiçbir şey bir yere gönderilmez. |

## Kurulum

**PowerShell (tek satır)**: son sürümü `%LOCALAPPDATA%\Programs\ArsivAra` klasörüne indirir ve Başlat menüsüne kısayol ekler.

```powershell
irm https://raw.githubusercontent.com/YOUR_GITHUB_USER/ArsivAra/main/install.ps1 | iex
```

**winget**

```powershell
winget install YOUR_GITHUB_USER.ArsivAra
```

**Elle**: [son sürümden](https://github.com/YOUR_GITHUB_USER/ArsivAra/releases/latest) `ArsivAra.exe`'yi indirip çalıştırın. Başka bir şey gerekmez.

## Kullanım

<img src="docs/images/search.png" width="720" alt="'sirket' araması iki arşivde 'Şirket Raporu 2023.pdf' buluyor">

| Sorgu | Anlamı |
|---|---|
| `fatura 2024` | İki kelime de arşiv içindeki yolda geçmeli |
| `ext:pdf;docx` | Sadece bu uzantılar |
| `arsiv:yedek` | Sadece yolunda "yedek" geçen arşivler |
| `klasor:` / `dosya:` | Sadece klasörler / sadece dosyalar |

| Tuş | İşlem |
|---|---|
| `Ctrl + tekerlek`, `Ctrl +` / `Ctrl −` | Simgeleri büyüt/küçült (48 px ve üstü kareli görünüm) |
| `Ctrl + 0` | Sıkı listeye dön |
| `Enter` / çift tık | Seçili dosyayı aç |
| `Ctrl + C` | Tam yolu kopyala |
| `Esc` | Önce aramayı, sonra arşiv filtresini temizle |
| `Ctrl + O` | Arşiv ekle |

Kenar çubuğunda bir arşive tıklarsanız arama yalnızca onun içinde yapılır; arama kutusu boşken arşivin tüm içeriği görünür.

## Neden hızlı

- **ZIP**: Dosyanın sonundaki *merkezi dizin* okunur, dosya verisine hiç dokunulmaz.
- **RAR5**: RAR her dosyanın başlığını kendi verisinin önüne yazar; listelemek normalde dosya başına bir disk atlaması demektir.
  WinRAR bu başlıkların bir *Quick Open* kopyasını arşivin sonuna da yazar. Arşiv Ara bu bloğu tek seferde okur ve her başlığı CRC32 ile doğrular
  ([biçim belgesi](https://www.rarlab.com/technote.htm)). Bu kaydı olmayan arşivlerde [SharpCompress](https://github.com/adamhathcock/sharpcompress) kullanılır.
- **Arama**: Tüm yollar küçük harfe çevrilip aksanlarından arındırılarak tek bir UTF-8 tampona yazılır ve bu tampon vektörize `IndexOf` ile paralel taranır.
- **Harici HDD**: Döner disklerdeki arşivler tek tek okunur; paralel okuma diskin kafasını dosyalar arasında gezdirip yavaşlatır.

## Kaynaktan derleme

Windows'ta [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) gerekir.

```powershell
git clone https://github.com/YOUR_GITHUB_USER/ArsivAra.git
cd ArsivAra
dotnet run --project src/ArsivAra.App -c Release
```

Taşınabilir tek dosya exe:

```powershell
dotnet publish src/ArsivAra.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

| Proje | İçerik |
|---|---|
| `src/ArsivAra.Core` | Arşiv okuyucular (ZIP, RAR5 Quick Open, SharpCompress), indeks dosyası, arama motoru |
| `src/ArsivAra.App` | Windows 11 (Fluent) temalı WPF arayüzü |
| `src/ArsivAra.Cli` | Test ve ölçüm için komut satırı aracı |

`v1.0.0` gibi bir etiket gönderildiğinde exe derlenir ve GitHub'da sürüm yayınlanır ([iş akışı](.github/workflows/release.yml)).

## Sınırlar

- Başlığı şifreli arşivler şifre olmadan listelenemez; ⚠ ile işaretlenir.
- Çok parçalı RAR'larda yalnızca ilk parça okunur.
- İç içe arşivler (zip içinde zip) henüz okunmuyor.

## Lisans

[MIT](LICENSE) © 2026 MaledictusMaggot. [SharpCompress](https://github.com/adamhathcock/sharpcompress) (MIT) kullanır.

---

<div align="center">

<img src="docs/images/icon-64.png" width="40" alt="Arşiv Ara logosu"><br>
<sub>Created By <b>MaledictusMaggot®</b></sub>

</div>
