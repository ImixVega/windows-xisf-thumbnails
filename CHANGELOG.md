# Changelog

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
