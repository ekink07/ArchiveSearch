<div align="center">

<img src="docs/images/icon-256.png" width="112" alt="Arşiv Ara icon">

# Arşiv Ara

**Search inside ZIP, RAR, 7z, TAR and ISO archives instantly, without extracting them.**

Drop your archives in (or let it find all of them), type a few letters, and see every matching file in milliseconds.

**English** · [Türkçe](README.tr.md) · [Deutsch](README.de.md) · [Español](README.es.md)

[![Release](https://img.shields.io/github/v/release/ekink07/ArchiveSearch?color=F7861F&label=release)](https://github.com/ekink07/ArchiveSearch/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/ekink07/ArchiveSearch/total?color=0F6BBF)](https://github.com/ekink07/ArchiveSearch/releases)
[![CI](https://github.com/ekink07/ArchiveSearch/actions/workflows/ci.yml/badge.svg)](https://github.com/ekink07/ArchiveSearch/actions/workflows/ci.yml)
![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows&logoColor=white)
![x64 | ARM64](https://img.shields.io/badge/x64%20%7C%20ARM64-0B1B2E)
[![License: MIT](https://img.shields.io/badge/license-MIT-0B1B2E)](#license)

<img src="docs/images/search.png" width="860" alt="Searching 'meeting' finds the meeting notes in two backups, one inside a nested archive; the preview pane shows the text and both versions">

</div>

## Why

Backups, project folders and downloads end up as dozens of large archives. Finding one file means opening them one by one.
Arşiv Ara reads only the archives' table of contents, keeps it in memory and lets you search all of them at once,
the way [Everything](https://www.voidtools.com/) searches file names on your disk.

## Features

| | |
|---|---|
| **Drag and drop, or all disks** | Drop archives or folders onto the window (or onto the exe), or let it find every archive on your computer. New and changed archives are picked up live. |
| **Instant search** | 1 million file names searched in 2–20 ms. Every keystroke updates the results. |
| **Search syntax** | OR `a\|b`, NOT `!draft`, `"exact phrase"`, wildcards `*.pdf`, `size:`, `date:`, `regex:`, `dupe:`, `content:`. Save searches with `Ctrl + S`. |
| **Turkish-aware matching** | Case and accent insensitive: `sirket` finds `Şirket`, `istanbul` finds `İSTANBUL`. Old ZIPs with DOS (CP857) file names are decoded correctly. |
| **Formats** | ZIP, RAR (including multi-part RAR4/RAR5), 7z, TAR (`.gz`, `.bz2`, `.xz`, `.zst`, `.tgz`), ISO (ISO 9660, Joliet, Rock Ridge, UDF). Archives inside archives are indexed too. |
| **Encrypted archives** | RAR and 7z with encrypted file names: double-click to enter the password, optionally remembered with Windows DPAPI. AES ZIPs are listed without a password and ask for it when opening. |
| **Preview pane** | `Alt + P`: images, text files and Word, Excel, PowerPoint and OpenDocument text, plus every other version of the same file. |
| **Versions and duplicates** | Right-click › *Versions* shows which backup holds the newest copy of a file; `dupe:` finds identical files by size and CRC. |
| **Extract and drag** | `Ctrl + E` extracts the selection to a folder; drag results straight into Explorer. |
| **List, grid, sorting** | Windows icons, photo thumbnails, `Ctrl + wheel` zoom from 16 to 256 px. Click a column header to sort by name, folder, size, date or archive. |
| **Safe** | Extracted files keep the archive's Mark of the Web, and programs or scripts (`.exe`, `.js`, `.ps1`…) ask before opening. Fully offline. |
| **Portable, 4 languages** | One exe for x64 or ARM64, no installation or .NET required. English by default; Türkçe, Deutsch and Español in *Settings*. |

## Install

**PowerShell (one line)**: picks the x64 or ARM64 build, verifies its SHA256, installs to `%LOCALAPPDATA%\Programs\ArsivAra`
and adds it to the Start menu and to *Settings › Apps* (uninstall from there). No admin rights needed.

```powershell
irm https://raw.githubusercontent.com/ekink07/ArchiveSearch/main/install.ps1 | iex
```

With options (`-Desktop` for a desktop shortcut, `-Uninstall`, `-Uninstall -RemoveData` to also delete settings and the library):

```powershell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/ekink07/ArchiveSearch/main/install.ps1))) -Desktop
```

**winget**

```powershell
winget install VerloraStudios.ArsivAra
```

(Available once the package has been accepted into the winget repository.)

**Manual**: download `ArsivAra.exe` (x64) or `ArsivAra-arm64.exe` from the [latest release](https://github.com/ekink07/ArchiveSearch/releases/latest) and run it.
The command-line tool is there too: `ArsivAra.Cli.exe` (x64) or `ArsivAra.Cli-arm64.exe`.

**Portable**: put an empty `portable.txt` next to the exe (or run it from a USB drive) and settings and the library are kept
in an `ArsivAra-veri` folder beside it. Otherwise they live in `%LOCALAPPDATA%\ArsivAra`.

## Usage

<img src="docs/images/grid.png" width="720" alt="Grid view with photo thumbnails">

| Query | Finds |
|---|---|
| `invoice 2024` | Both words in the path inside the archive (AND) |
| `report\|slides` | Either word (OR) |
| `!draft` | Everything without it (NOT) |
| `"bank statement"` | The exact phrase, spaces included |
| `*.pdf`, `IMG_00??.jpg` | Wildcards on the file name |
| `ext:pdf;docx` | Only these extensions |
| `archive:backup` | Only archives whose name or path contains "backup" |
| `folder:` / `file:` | Only folders / only files |
| `name:="Report.pdf"` | The exact file name |
| `size:>10mb`, `size:1mb..1gb` | Size |
| `date:2024`, `date:>=2024-06-01`, `date:thisweek` | Modified date (`today`, `yesterday`, `lastweek`, `thismonth`…) |
| `regex:^invoice_\d+` | Regular expression |
| `dupe:` | Identical copies (size and CRC) |
| `content:emerald` | Inside text and Office files; slow, so combine it with other terms |

Turkish keys work as well: `arsiv:`, `klasor:`, `dosya:`, `ad:`, `boyut:`, `tarih:`, `kopya:`, `icerik:`. The **?** button next to the search box shows the same list.

| Key | Action |
|---|---|
| `Ctrl + F` | Focus the search box |
| `↓` / `Enter` in the search box | Go to the results / open the first result |
| `Enter` / double-click | Open the selected file |
| `Ctrl + E` | Extract the selected files to a folder |
| `Ctrl + C` | Copy the full path |
| `Alt + P` | Show or hide the preview pane |
| `Ctrl + S` | Save the current search |
| `Ctrl + wheel`, `Ctrl +` / `Ctrl −` | Zoom icons (48 px and up switches to grid view) |
| `Ctrl + 0` | Back to the compact list |
| `Esc` | Clear the search, then the archive filter |
| `Ctrl + O` | Add archives |
| `Ctrl + ,` | Settings: language, disk scan, nested archives, temporary files, saved passwords |
| `F1` | About and license |
| `Delete` in the archive list | Remove the archive from the list |

Click an archive in the sidebar to search only inside it; with an empty search box you see its whole content.

## How it is fast

- **ZIP**: only the *central directory* at the end of the file is read. File data is never touched.
- **RAR5**: RAR stores each file header in front of its data, so listing normally means one disk seek per file.
  WinRAR also writes a *Quick Open* copy of those headers at the end of the archive; Arşiv Ara reads that block in one go and verifies every cached header with CRC32
  ([format spec](https://www.rarlab.com/technote.htm)). Archives without it fall back to [SharpCompress](https://github.com/adamhathcock/sharpcompress).
- **ISO**: the UDF and ISO 9660 directory records are parsed directly; no mounting needed.
- **Search**: every path is case-folded and accent-stripped into one UTF-8 buffer, which is scanned in parallel with vectorized `IndexOf`.
- **Whole disks**: on NTFS the archive files are found through the MFT (a small elevated helper, the window itself stays unelevated) and kept up to date through the USN journal.
- **External HDDs**: archives on spinning disks are read one at a time, because parallel reads make the drive head jump between files.

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) on Windows (`winget install Microsoft.DotNet.SDK.10`); `global.json` selects it.

```powershell
git clone https://github.com/ekink07/ArchiveSearch.git
cd ArchiveSearch
dotnet run --project src/ArsivAra.App -c Release
dotnet test tests/ArsivAra.Core.Tests        # unit tests on real sample archives
pwsh tests/ui/ui-smoke.ps1                   # UI smoke test through UI Automation (Release build)
```

Portable single-file exe (use `-r win-arm64` for ARM64):

```powershell
dotnet publish src/ArsivAra.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

| Folder | Contents |
|---|---|
| `src/ArsivAra.Core` | Archive readers (ZIP, RAR5 Quick Open, ISO/UDF, SharpCompress), index file, query language, search engine |
| `src/ArsivAra.App` | WPF user interface with the Windows 11 (Fluent) theme |
| `src/ArsivAra.Cli` | Command-line tool: `index`, `search`, `extract`, `watch`, `lisans` |
| `src/ThirdParty` | SharpCompress 0.50.4 source code (RAR, 7z, TAR, XZ, BZip2 and Zstandard decoders), see [its README](src/ThirdParty/README.md) |
| `tests/` | xUnit tests with sample archives made by WinRAR, 7-Zip and IMAPI2; UI smoke test |
| `tools/` | Icons, screenshots and social card and test archives |

## Limitations

- Archives with encrypted file names need the password before they can be listed.
- Nested archives are read one level deep, up to 64 per archive and 256 MB each.
- `content:` reads plain text and Office files only (no PDF) and can be slow on large solid archives.
- Fast whole-disk scanning needs NTFS and administrator permission; otherwise folders are walked, which is slower.

## License

Arşiv Ara is free and open source under the [MIT license](LICENSE): you may use, copy, modify, merge, publish, distribute,
sublicense and sell it, as long as the copyright notice and the license text are kept. It is provided without warranty.

| | |
|---|---|
| **Publisher** | Verlora Studios |
| **Creator** | MaledictusMaggot |
| **Copyright** | © 2026 MaledictusMaggot |
| **Third-party components** | [SharpCompress](https://github.com/adamhathcock/sharpcompress) 0.50.4, included as source code in [src/ThirdParty](src/ThirdParty/README.md) (MIT; it contains code under the UnRAR, Ms-PL, Apache 2.0, BSD and zlib licenses), and, in the portable build, the .NET runtime (MIT). All texts: [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt). |

The app shows the same information under **About › License text** (`F1`), and `ArsivAra.Cli lisans` prints it in the console.

<img src="docs/images/about.png" width="300" alt="About window with the License section">

---

<div align="center">

<img src="docs/images/icon-64.png" width="40" alt="Arşiv Ara logo"><br>
<sub>Verlora Studios · Created By <b>MaledictusMaggot®</b></sub>

</div>
