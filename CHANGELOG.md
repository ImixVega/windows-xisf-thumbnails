# Changelog

## 0.3.5 (2026-10-22; large Drizzle support tested on Windows 10)
- Handle XISF images larger than 1000 MiB using memory-mapped disk data.
- Stream ZSTD and zlib compression blocks instead of allocating entire decoded image.
- Bounded 12 GiB decoded-size limit, disk-space checks and cancellation.
- Retain original AutoSTF algorithm for ordinary-sized files.
- Validated Explorer thumbnail of a large M42 Drizzle stack after Windows reboot (releases old COM DLL).

## 0.3.4 (2026-10-22)
- Include active `.xisf` ProgID(s) in initial COM thumbnail registration.
- Preserve previous per-user handlers for the extra association paths.
- Recognize the legacy v0.3.4 hotfix backup during uninstall.
- Retain read-only diagnostics and legacy repair scripts in `tools/`.
- Integrates a registration fix confirmed to enable thumbnails in Explorer on Windows 10.

## 0.3.3
- C# smoke test helper fixes PowerShell overload binding for `Initialize(IStream, uint)`.

## 0.3.2
- Fix `$PSScriptRoot` in parameter defaults and HBITMAP test output.

## 0.3.1
- Resolve ambiguous `STATSTG` type in COM stream wrapper.

## 0.3.0
- First thumbnail provider implementation.
