# Changelog

## Unreleased

- New logo (`docs/images/logo-arsiv.svg`) for the app icon, window and taskbar, the empty screen, the website and the README. `tools/make_icon.ps1` regenerates every icon size from it.
- About window (F1, or click the "Created By MaledictusMaggot®" signature in the status bar) with logo, version and credits.
- Product, company and copyright details (MaledictusMaggot) in the exe's file properties for all projects (`Directory.Build.props`); `ArsivAra.Cli --version`.
- Website: complete HTML document head (charset, mobile viewport), colors matched to the new logo, credit in the footer.

## 1.0.0

- Drag and drop archives or folders; the list is saved between sessions (next to the exe when portable).
- Instant, case- and accent-insensitive search with Turkish-aware matching and `ext:`, `arsiv:`, `klasor:`, `dosya:` filters.
- ZIP, RAR, 7z and TAR support; RAR5 archives are listed through the WinRAR Quick Open record.
- Old ZIP file names in DOS code pages (CP857 on Turkish systems) are decoded correctly.
- List and grid views with Windows file-type icons and photo thumbnails; Ctrl + mouse wheel zoom from 16 to 256 px.
- Open any file inside an archive with a double click.
- Progress bar and taskbar progress while archives are read.
- Portable single-file exe for Windows 10/11 x64, no installation or .NET required.
