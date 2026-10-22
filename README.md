# Windows XISF Thumbnails

**Free, auto-stretched `.xisf` thumbnails in Windows File Explorer.** No PixInsight or AstroThumbs license needed to generate thumbnails.

[Instrukcja po polsku](README_PL.md) · [QuickLook XISF Viewer](https://github.com/ImixVega/quicklook-xisf-viewer) · MIT license

![Status](https://img.shields.io/badge/status-community%20prototype-blue) ![Windows](https://img.shields.io/badge/Windows-10%2F11%20x64-informational)

## Features

- Native Explorer thumbnails using `IThumbnailProvider` / `IInitializeWithStream`.
- AutoSTF-like median/MAD stretching of XISF mono and RGB images, with common Bayer patterns.
- Support for XISF `zstd`, `zstd+sh`, `zlib`, `lz4`, `lz4hc` and subblocks.
- Per-user COM registration (HKCU); **no Administrator privileges** required.
- Preserves default `.xisf` application and FITS associations.
- Backs up prior per-user thumbnail handler values and provides an uninstall script.

## Requirements

Windows 10/11 x64, Windows PowerShell 5.1, [.NET SDK 8+](https://dotnet.microsoft.com/download) for building, and internet for initial NuGet restore.

## Build, verify, install

From the repository directory in 64-bit **Windows PowerShell**:

```powershell
powershell -ExecutionPolicy Bypass -File .\Build.ps1
powershell -ExecutionPolicy Bypass -File .\Test.ps1
powershell -ExecutionPolicy Bypass -File .\Install.ps1
```

Test output: `TestThumbnail.png`. To test with your own sample:

```powershell
powershell -ExecutionPolicy Bypass -File .\Test.ps1 -XisfFile "D:\Astro\example.xisf"
```

Open Explorer → **Large icons / Extra large icons**; if needed, restart Windows Explorer and test a copied `.xisf` under a new filename to avoid a stale thumbnail cache.

The installer registers the thumbnail provider for `.xisf`, `SystemFileAssociations`, and the active ProgID(s) — including PixInsight's association — **without modifying the default program**. This integrates the successful v0.3.4 registration repair into a fresh install.

## Uninstall / legacy installs

```powershell
powershell -ExecutionPolicy Bypass -File .\Uninstall.ps1
```

The uninstall script restores backed-up handler values. If using older v0.3.1 already repaired with v0.3.4, keep the current working installation unless you are intentionally upgrading; the new uninstall also recognizes the old repair backup. The legacy fix scripts are kept under `tools/` for troubleshooting. **Do not reinstall over an existing installation without uninstalling it first.**

Logs (only on decoder failures): `%LOCALAPPDATA%\AstroXisfThumbnail\errors.log`. Diagnostics: `tools/Diagnose-XISF-Thumbnails.ps1`.

## Status / limitations

Windows 10 user testing confirmed: code v0.3.1 builds, generates PNG with `Test.ps1` v0.3.3, and works in Explorer after applying registration fix v0.3.4. This consolidated v0.3.4 installer contains the same fixes but **has not yet been separately validated end-to-end**. Windows shell extensions should be evaluated carefully on the user's own machine; managed in-process extensions are not Microsoft's recommended production architecture.

Only the first 2D XISF image is previewed. Not all unusual XISF variants are supported; the stretch is similar to, not identical to, PixInsight STF.

## Related project

[QuickLook XISF Viewer](https://github.com/ImixVega/quicklook-xisf-viewer) provides full-window previews with the **Spacebar**; this project provides Explorer **thumbnail tiles**.

## Credits and license

Maintainer: [ImixVega](https://github.com/ImixVega). MIT license (see [LICENSE.txt](LICENSE.txt)). [ZstdSharp.Port](https://www.nuget.org/packages/ZstdSharp.Port/) is an independent MIT-licensed dependency.
