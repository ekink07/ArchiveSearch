# Third-party source code

The code in this folder is not written by the Arşiv Ara authors; it is included as source code and compiled into the
program. Its licenses and copyright notices are in [THIRD-PARTY-NOTICES.txt](../../THIRD-PARTY-NOTICES.txt) at the
repository root, and in the app under **About › License text › Third-party components**. The copyright and license
notices in these files must not be removed.

## SharpCompress 0.50.4

| | |
|---|---|
| Source | https://github.com/adamhathcock/sharpcompress |
| Version | 0.50.4 (tag `0.50.4`, commit `c083c6efd843a844b0c8f7878787360e815be781`) |
| License | MIT, Copyright (c) 2014 Adam Hathcock ([LICENSE.txt](SharpCompress/LICENSE.txt)); the licenses of the components it contains are in THIRD-PARTY-NOTICES.txt |
| Copied | The `.cs` files of the upstream `src/SharpCompress` folder, **unchanged** |
| Not copied | `SharpCompress.csproj`, `packages.lock.json`, `SharpCompress.pfx` (signing certificate) and the developer note `Compressors/Rar/UnpackV2017/notes.txt` |

Added by us:

- [SharpCompress/SharpCompress.csproj](SharpCompress/SharpCompress.csproj): net10.0, C# 14; warnings and analyzers are off because this is third-party code.
- [SharpCompress/Compressors/Rar/UnRarLicense.cs](SharpCompress/Compressors/Rar/UnRarLicense.cs): the paragraph the UnRAR license requires in the
  source code comments (the RAR decompression code is derived from UnRAR; Arşiv Ara uses it only to extract archives).
- [SharpCompress/LICENSE.txt](SharpCompress/LICENSE.txt): the license from the root of the upstream repository.

## Updating

```powershell
git clone --depth 1 --branch <version> https://github.com/adamhathcock/sharpcompress $env:TEMP\sharpcompress
robocopy $env:TEMP\sharpcompress\src\SharpCompress src\ThirdParty\SharpCompress /MIR /XD bin obj `
  /XF SharpCompress.csproj packages.lock.json SharpCompress.pfx notes.txt UnRarLicense.cs LICENSE.txt
Copy-Item $env:TEMP\sharpcompress\LICENSE.txt src\ThirdParty\SharpCompress\LICENSE.txt
```

Files excluded with `/XF` are not deleted by `/MIR`, so our project file and `UnRarLicense.cs` stay in place.
Then update the version and commit in this file, update [THIRD-PARTY-NOTICES.txt](../../THIRD-PARTY-NOTICES.txt) if a
component or license was added, and run all tests. If upstream needs a newer .NET SDK, update `global.json` too.
