#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$XisfFile,
    [string]$OutputPng
)
$ErrorActionPreference = 'Stop'
if (-not [Environment]::Is64BitProcess) {
    throw 'Test wymaga 64-bitowego Windows PowerShell.'
}
# Don't access $PSScriptRoot from param() defaults in Windows PowerShell 5.1.
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($root)) { throw 'Nie mozna ustalic folderu Test.ps1.' }
if ([string]::IsNullOrWhiteSpace($XisfFile)) { $XisfFile = Join-Path $root 'tests\test_rgb_zstd.xisf' }
if ([string]::IsNullOrWhiteSpace($OutputPng)) { $OutputPng = Join-Path $root 'TestThumbnail.png' }
if (-not (Test-Path -LiteralPath $XisfFile -PathType Leaf)) {
    throw "Nie znaleziono pliku XISF: $XisfFile"
}
$dll = Join-Path $root 'dist\AstroXisf.ThumbnailProvider.dll'
$zstd = Join-Path $root 'dist\ZstdSharp.dll'
$helper = Join-Path $root 'tests\ThumbnailSmokeTest.cs'
if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) { throw 'Brak DLL. Uruchom najpierw Build.ps1.' }
if (-not (Test-Path -LiteralPath $zstd -PathType Leaf)) { throw 'Brak ZstdSharp.dll. Uruchom najpierw Build.ps1.' }
if (-not (Test-Path -LiteralPath $helper -PathType Leaf)) { throw 'Brak tests\ThumbnailSmokeTest.cs.' }

$XisfFile = (Resolve-Path -LiteralPath $XisfFile).Path
$OutputPng = [System.IO.Path]::GetFullPath($OutputPng)
$outputDir = Split-Path -Parent $OutputPng
if (-not (Test-Path -LiteralPath $outputDir -PathType Container)) {
    throw "Katalog docelowy nie istnieje: $outputDir"
}

# Load the runtime dependencies into this Windows PowerShell process.
Add-Type -AssemblyName System.Drawing
Add-Type -Path $zstd
Add-Type -Path $dll

# A C# helper calls both COM-style methods with strongly typed arguments:
# Initialize(IStream, uint) and GetThumbnail(uint, out IntPtr, out WtsAlpha).
# This avoids the Windows PowerShell overload-resolution error.
if (-not ('AstroXisf.Test.ThumbnailSmokeTest' -as [type])) {
    Add-Type -Path $helper -ReferencedAssemblies @(
        $dll,
        [System.Drawing.Bitmap].Assembly.Location
    ) -ErrorAction Stop
}
try {
    $result = [AstroXisf.Test.ThumbnailSmokeTest]::Run($XisfFile, $OutputPng)
    Write-Host "SUKCES: $OutputPng" -ForegroundColor Green
    Write-Host $result
}
catch {
    Write-Host 'Test miniatury nie powiodl sie.' -ForegroundColor Red
    Write-Host "Log dekodera (jesli utworzony): $env:LOCALAPPDATA\AstroXisfThumbnail\errors.log"
    throw
}
