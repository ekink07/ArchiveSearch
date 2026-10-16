<div align="center">

<img src="docs/images/icon-256.png" width="112" alt="Arşiv Ara icon">

# Arşiv Ara

**Search inside ZIP, RAR, 7z and TAR archives instantly, without extracting them.**

Drop your archives in, type a few letters, and see every matching file in milliseconds.

**English** · [Türkçe](README.tr.md) · [Deutsch](README.de.md) · [Español](README.es.md)

[![Release](https://img.shields.io/github/v/release/YOUR_GITHUB_USER/ArsivAra?color=F7861F&label=release)](https://github.com/YOUR_GITHUB_USER/ArsivAra/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/YOUR_GITHUB_USER/ArsivAra/total?color=0F6BBF)](https://github.com/YOUR_GITHUB_USER/ArsivAra/releases)
![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows&logoColor=white)
![.NET 9](https://img.shields.io/badge/.NET-9-512BD4?logo=dotnet&logoColor=white)
[![License: MIT](https://img.shields.io/badge/license-MIT-0B1B2E)](LICENSE)

<img src="docs/images/grid.png" width="860" alt="Grid view with photo thumbnails and Windows file-type icons">

</div>

## Why

Backups, project folders and downloads end up as dozens of large archives. Finding one file means opening them one by one.
Arşiv Ara reads only the archives' table of contents, keeps it in memory and lets you search all of them at once,
the way [Everything](https://www.voidtools.com/) searches file names on your disk.

## Features

| | |
|---|---|
| **Drag and drop** | Drop archives or whole folders onto the window (or onto the exe). The list is saved for next time. |
| **Instant search** | 1 million file names searched in 2–20 ms. Every keystroke updates the results. |
| **Turkish-aware matching** | Case and accent insensitive: `sirket` finds `Şirket`, `istanbul` finds `İSTANBUL`. Old ZIPs with DOS (CP857) file names are decoded correctly. |
| **Filters** | `ext:pdf;docx`, `arsiv:2025`, `klasor:` (folders only), `dosya:` (files only). Several words are combined with AND. |
| **List and grid views** | Real Windows icons per file type. In grid view, photos show their actual thumbnails. **Ctrl + mouse wheel** zooms from 16 to 256 px. |
| **Open in one click** | Double-click a result to extract just that file to a temp folder and open it. |
| **Fast on large RARs** | Reads the WinRAR *Quick Open* record, so a 14 GB backup with 22,000 files is listed in seconds instead of minutes. |
| **Portable** | One 61 MB exe, no installation, no .NET required. Settings travel next to the exe on a USB stick. |
| **Private** | Works fully offline. Nothing is uploaded anywhere. |

## Install

**PowerShell (one line)**: downloads the latest release to `%LOCALAPPDATA%\Programs\ArsivAra` and adds a Start menu shortcut.

```powershell
irm https://raw.githubusercontent.com/YOUR_GITHUB_USER/ArsivAra/main/install.ps1 | iex
```

**winget**

```powershell
winget install YOUR_GITHUB_USER.ArsivAra
```

**Manual**: download `ArsivAra.exe` from the [latest release](https://github.com/YOUR_GITHUB_USER/ArsivAra/releases/latest) and run it. Nothing else is needed.

## Usage

<img src="docs/images/search.png" width="720" alt="Searching for 'sirket' finds 'Şirket Raporu 2023.pdf' in two archives">

| Query | Meaning |
|---|---|
| `invoice 2024` | Both words must appear in the path inside the archive |
| `ext:pdf;docx` | Only these extensions |
| `arsiv:backup` | Only archives whose path contains "backup" |
| `klasor:` / `dosya:` | Only folders / only files |

| Key | Action |
|---|---|
| `Ctrl + wheel`, `Ctrl +` / `Ctrl −` | Zoom icons (48 px and up switches to grid view) |
| `Ctrl + 0` | Back to the compact list |
| `Enter` / double-click | Open the selected file |
| `Ctrl + C` | Copy the full path |
| `Esc` | Clear the search, then the archive filter |
| `Ctrl + O` | Add archives |

Click an archive in the sidebar to search only inside it; with an empty search box you see its whole content.

## How it is fast

- **ZIP**: only the *central directory* at the end of the file is read. File data is never touched.
- **RAR5**: RAR stores each file header in front of its data, so listing normally means one disk seek per file.
  WinRAR also writes a *Quick Open* copy of those headers at the end of the archive; Arşiv Ara reads that block in one go and verifies every cached header with CRC32
  ([format spec](https://www.rarlab.com/technote.htm)). Archives without it fall back to [SharpCompress](https://github.com/adamhathcock/sharpcompress).
- **Search**: every path is case-folded and accent-stripped into one UTF-8 buffer, which is scanned in parallel with vectorized `IndexOf`.
- **External HDDs**: archives on spinning disks are read one at a time, because parallel reads make the drive head jump between files.

## Build from source

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) on Windows.

```powershell
git clone https://github.com/YOUR_GITHUB_USER/ArsivAra.git
cd ArsivAra
dotnet run --project src/ArsivAra.App -c Release
```

Portable single-file exe:

```powershell
dotnet publish src/ArsivAra.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

| Project | Contents |
|---|---|
| `src/ArsivAra.Core` | Archive readers (ZIP, RAR5 Quick Open, SharpCompress), index file, search engine |
| `src/ArsivAra.App` | WPF user interface with the Windows 11 (Fluent) theme |
| `src/ArsivAra.Cli` | Command-line tool for testing and benchmarks |

Pushing a tag such as `v1.0.0` builds the exe and publishes a GitHub release ([workflow](.github/workflows/release.yml)).

## Limitations

- Archives with encrypted headers cannot be listed without the password; they are marked with ⚠.
- For multi-part RARs only the first part is read.
- Archives inside archives are not indexed yet.

## License

[MIT](LICENSE) © 2026 MaledictusMaggot. Uses [SharpCompress](https://github.com/adamhathcock/sharpcompress) (MIT).

---

<div align="center">

<img src="docs/images/icon-64.png" width="40" alt="Arşiv Ara logo"><br>
<sub>Created By <b>MaledictusMaggot®</b></sub>

</div>
