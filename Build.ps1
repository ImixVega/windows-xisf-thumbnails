#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Brak SDK dotnet. Zainstaluj .NET SDK 8 lub nowszy (jak do wtyczki QuickLook).'
}
Write-Host 'Kompilowanie dodatku miniatur XISF (Windows x64)...'
& dotnet build (Join-Path $root 'XisfThumbnail.csproj') -c Release -o (Join-Path $root 'dist') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Blad kompilacji. Wyslij wynik konsoli.' }
$dll = Join-Path $root 'dist\AstroXisf.ThumbnailProvider.dll'
$zstd = Join-Path $root 'dist\ZstdSharp.dll'
if (-not (Test-Path $dll) -or -not (Test-Path $zstd)) {
    throw 'Nie znaleziono skompilowanego dodatku lub biblioteki ZstdSharp.dll.'
}
Write-Host "GOTOWE: $dll" -ForegroundColor Green
Write-Host 'Kolejny krok: .\Install.ps1 (bez uprawnien administratora).'
