# Changelog

## 1.1.0 - 2026-10-20

### Added

- **License section**: the About window (F1) shows the license, copyright and third-party components; *License text* opens the full
  `LICENSE` and `THIRD-PARTY-NOTICES.txt`. Also reachable from Settings, and `ArsivAra.Cli lisans` prints it. Publisher: Verlora Studios.
- **Scan all disks**: finds every archive on fixed disks (through the MFT with a small elevated helper, or by walking folders)
  and keeps the list up to date while the app runs (USN journal or folder watcher).
- **Search syntax**: OR (`a|b`), NOT (`!word`), `"exact phrase"`, `*` and `?` wildcards, `name:`/`ad:`, `path:`/`yol:`,
  `size:`/`boyut:`, `date:`/`tarih:` (also `today`, `thisweek`…), `regex:`, `dupe:`/`kopya:`, `content:`/`icerik:`, English and Turkish keys.
  Invalid values are explained in the status bar. Searches can be saved (Ctrl + S) and appear in the sidebar.
- **Formats**: ISO images (ISO 9660, Joliet, Rock Ridge, UDF), `.tar.gz`/`.tgz`/`.bz2`/`.xz`/`.zst`, multi-part RAR4 and RAR5,
  archives inside archives (one level deep).
- **Encrypted archives**: password prompt for RAR and 7z with encrypted file names; passwords can be remembered with Windows DPAPI.
- **Preview pane** (Alt + P): images, text and Office/OpenDocument documents, with every other version of the same file.
- **Versions and duplicates**: "which backup has the newest version of this file", identical files by size and CRC.
- Sortable columns (name, folder, size, date, archive) and a date column.
- Extract the selection to a folder (Ctrl + E) and drag results into Explorer.
- Content search inside text and Office files.
- Settings window (Ctrl + ,); user interface in Türkçe, English, Deutsch and Español.
- Single instance: opening another archive passes it to the running window.
- ARM64 build; the installer picks the right one and registers the app under *Settings › Apps* with an uninstaller.
- Unit tests (xUnit, real archives made by WinRAR, 7-Zip and IMAPI2), a UI smoke test through UI Automation, and CI on every push.
- Website: search syntax and license sections, social preview card, live demo with the new syntax.
- New logo (`docs/images/logo-arsiv.svg`) for the app icon, window, empty screen, website and README; "Created By MaledictusMaggot®" in the app, site and README.

### Changed

- English is the default language of the app, the installer, the command-line tool and the website. Türkçe, Deutsch and
  Español stay available under *Settings › Language*, where *System language* follows the Windows display language.
  Source code comments and the documentation are in English.
- Moved to .NET 10 (LTS). Building needs the .NET 10 SDK (selected by `global.json`); the portable exe includes the runtime.
- SharpCompress 0.50.4 is built from source in `src/ThirdParty/SharpCompress` instead of being referenced as a NuGet package.
  `THIRD-PARTY-NOTICES.txt` lists every component it contains with its license (UnRAR, Ms-PL, Apache 2.0, BSD, zlib, MIT).
- winget package identifier is now `VerloraStudios.ArsivAra`.
- Releases also contain the command-line tool (`ArsivAra.Cli.exe`, `ArsivAra.Cli-arm64.exe`) and a `.sha256` file for every exe.
- The index file format is version 2; older libraries are converted automatically and a backup of the old file is kept.
  An unreadable library is set aside instead of being overwritten.
- Settings and the library live in `%LOCALAPPDATA%\ArsivAra`; portable mode is used when the exe sits on a USB drive
  or next to a `portable.txt` file, instead of creating a data folder next to every copy of the exe.
- Temporary files from opened archives are reused and cleaned up after the configured number of days.

### Security

- Files extracted from downloaded archives keep the Mark of the Web, and executables and scripts ask before opening.
- Unhandled errors are written to a log (`gunluk` folder) instead of closing the app silently.

### Fixed

- `install.ps1` is pure ASCII, so it runs correctly in Windows PowerShell 5.1 both from a file and through `irm | iex`,
  and its shortcuts keep the Turkish name ("Arşiv Ara") instead of "Arsiv Ara".
- Header-encrypted RAR5 archives listed no files when the solid flag was read first.

## 1.0.0

- Drag and drop archives or folders; the list is saved between sessions (next to the exe when portable).
- Instant, case- and accent-insensitive search with Turkish-aware matching and `ext:`, `arsiv:`, `klasor:`, `dosya:` filters.
- ZIP, RAR, 7z and TAR support; RAR5 archives are listed through the WinRAR Quick Open record.
- Old ZIP file names in DOS code pages (CP857 on Turkish systems) are decoded correctly.
- List and grid views with Windows file-type icons and photo thumbnails; Ctrl + mouse wheel zoom from 16 to 256 px.
- Open any file inside an archive with a double click.
- Progress bar and taskbar progress while archives are read.
- Portable single-file exe for Windows 10/11 x64, no installation or .NET required.
