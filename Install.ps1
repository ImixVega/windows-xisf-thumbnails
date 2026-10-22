#Requires -Version 5.1
# Installs only .xisf thumbnail associations in HKCU; no administrator privileges needed.
$ErrorActionPreference = 'Stop'
if (-not [Environment]::Is64BitProcess -or -not [Environment]::Is64BitOperatingSystem) {
    throw 'Uruchom 64-bitowy PowerShell w 64-bitowym Windows.'
}

$root = $PSScriptRoot
$source = Join-Path $root 'dist'
$dll = Join-Path $source 'AstroXisf.ThumbnailProvider.dll'
if (-not (Test-Path $dll)) { throw 'Brak skompilowanej DLL. Uruchom najpierw .\Build.ps1.' }
if (-not (Test-Path (Join-Path $source 'ZstdSharp.dll'))) { throw 'Brak ZstdSharp.dll w dist; wykonaj Build.ps1.' }

$clsid = '{68F756A5-4977-4DF0-B306-AE9812B57EC8}'
$thumb = '{E357FCCD-A995-4576-B01F-234630154E96}'
$clsKey = 'Software\Classes\CLSID\' + $clsid
# Shell resolves thumbnails via the file association ProgID in many Windows setups.
# In particular PixInsight registers .xisf as Pleiades Astrophoto.PixInsight.xisf.
# Register under .xisf, SystemFileAssociations AND all active ProgIDs.
$assoc = New-Object 'System.Collections.Generic.List[string]'
$assoc.Add('Software\Classes\.xisf\ShellEx\' + $thumb)
$assoc.Add('Software\Classes\SystemFileAssociations\.xisf\ShellEx\' + $thumb)
$cr = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
    [Microsoft.Win32.RegistryHive]::ClassesRoot,
    [Microsoft.Win32.RegistryView]::Registry64)
try {
    $progIds = New-Object 'System.Collections.Generic.List[string]'
    $ext = $cr.OpenSubKey('.xisf')
    if ($null -ne $ext) {
        try {
            $p = [string]$ext.GetValue('', '')
            if (-not [string]::IsNullOrWhiteSpace($p)) { $progIds.Add($p) }
        } finally { $ext.Dispose() }
    }
    $choice = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey(
        'Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.xisf\UserChoice')
    if ($null -ne $choice) {
        try {
            $p = [string]$choice.GetValue('ProgId', '')
            if (-not [string]::IsNullOrWhiteSpace($p) -and -not $progIds.Contains($p)) {
                $progIds.Add($p)
            }
        } finally { $choice.Dispose() }
    }
    foreach ($p in $progIds) {
        $assoc.Add('Software\Classes\' + $p + '\ShellEx\' + $thumb)
    }
} finally { $cr.Dispose() }

$reg = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
    [Microsoft.Win32.RegistryHive]::CurrentUser,
    [Microsoft.Win32.RegistryView]::Registry64)

# Only one active installation per user. Avoid losing a previous backup.
$installRoot = Join-Path $env:LOCALAPPDATA 'AstroXisfThumbnail'
$backupFile = Join-Path $installRoot 'previous-handlers.json'
if (Test-Path $backupFile) {
    throw "Rozszerzenie juz zainstalowane. Odinstaluj je przed aktualizacja: .\Uninstall.ps1"
}
New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
$installFolder = Join-Path $installRoot ('v0.3.4-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $installFolder -Force | Out-Null
# Copy runtime dependencies as well as the COM assembly.
Get-ChildItem -LiteralPath $source -Filter '*.dll' -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $installFolder -Force
}
Copy-Item -LiteralPath (Join-Path $root 'Uninstall.ps1') -Destination $installRoot -Force
$installedDll = Join-Path $installFolder 'AstroXisf.ThumbnailProvider.dll'
$assemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($installedDll).FullName
$uri = (New-Object System.Uri($installedDll)).AbsoluteUri

# Snapshot previous per-user thumbnail handlers (including missing keys).
$existing = @()
foreach ($path in $assoc) {
    $key = $reg.OpenSubKey($path, $false)
    try {
        $hasValue = $false
        $value = $null
        if ($null -ne $key) {
            $hasValue = @($key.GetValueNames()) -contains ''
            if ($hasValue) { $value = [string]$key.GetValue('', '', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) }
        }
        $existing += [PSCustomObject]@{ Path = $path; HasDefaultValue = $hasValue; Value = $value }
    } finally { if ($null -ne $key) { $key.Dispose() } }
}
$existing | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $backupFile -Encoding UTF8

try {
    # Per-user COM registration analogous to RegAsm /codebase, scoped to HKCU.
    $k = $reg.CreateSubKey($clsKey)
    $k.SetValue('', 'Astro XISF Thumbnail Provider')
    $k.Dispose()
    $k = $reg.CreateSubKey($clsKey + '\InprocServer32')
    $k.SetValue('', 'mscoree.dll')
    $k.SetValue('ThreadingModel', 'Apartment')
    $k.SetValue('Class', 'AstroXisf.ThumbnailProvider.XisfThumbnailProvider')
    $k.SetValue('Assembly', $assemblyName)
    $k.SetValue('RuntimeVersion', 'v4.0.30319')
    $k.SetValue('CodeBase', $uri)
    $k.Dispose()

    foreach ($path in $assoc) {
        $k = $reg.CreateSubKey($path)
        $k.SetValue('', $clsid)
        $k.Dispose()
    }
}
catch {
    Write-Warning "Instalacja nieudana. Uruchom .\Uninstall.ps1, aby przywrocic ustawienia. $_"
    throw
}
finally { $reg.Dispose() }

Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class XisfShellNotify {
    [DllImport("shell32.dll")] public static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
'@
[XisfShellNotify]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)
Write-Host ''
Write-Host 'ZAINSTALOWANO: miniaturki XISF (tylko .xisf, bez zmian w FITS)' -ForegroundColor Green
Write-Host "Pliki: $installFolder"
Write-Host "Diagnostyka: $(Join-Path $installRoot 'errors.log')"
Write-Host 'Otworz Eksplorator, wybierz Duze ikony i sprawdz XISF.'
Write-Host 'Jesli poprzednio byla ikonka, skopiuj testowy XISF do nowej nazwy, aby ominac stary cache.'
Write-Host ('Zarejestrowano skojarzenia: ' + $assoc.Count)
Write-Host 'Aby wycofac: .\Uninstall.ps1'
