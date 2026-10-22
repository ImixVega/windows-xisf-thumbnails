#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
if (-not [Environment]::Is64BitProcess) { throw 'Uruchom 64-bitowy PowerShell.' }
$root = Join-Path $env:LOCALAPPDATA 'AstroXisfThumbnail'
$backupFile = Join-Path $root 'previous-handlers.json'
$hotfixBackup = Join-Path $root 'registry-associations-hotfix-backup.json'
if (-not (Test-Path $backupFile)) {
    Write-Host 'Brak instalacji XISF Thumbnails dla tego uzytkownika.'
    return
}
$clsid = '{68F756A5-4977-4DF0-B306-AE9812B57EC8}'
$clsKey = 'Software\Classes\CLSID\' + $clsid
$reg = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
    [Microsoft.Win32.RegistryHive]::CurrentUser,
    [Microsoft.Win32.RegistryView]::Registry64)
try {
    # Legacy v0.3.1 installs required a separate v0.3.4 registry repair.
    # Restore its entries before restoring the original installation snapshot.
    if (Test-Path -LiteralPath $hotfixBackup) {
        $hotfixSaved = @(Get-Content -LiteralPath $hotfixBackup -Raw | ConvertFrom-Json)
        foreach ($old in $hotfixSaved) {
            $k = $reg.OpenSubKey([string]$old.Path, $true)
            if ($null -eq $k) { continue }
            try {
                if ([string]$k.GetValue('', '') -ne $clsid) { continue }
                if ($old.HadDefaultValue) {
                    $k.SetValue('', [string]$old.PreviousValue)
                } else {
                    $k.DeleteValue('', $false)
                }
            } finally { $k.Dispose() }
        }
        Remove-Item -LiteralPath $hotfixBackup -Force
    }
    $saved = @(Get-Content -LiteralPath $backupFile -Raw | ConvertFrom-Json)
    foreach ($old in $saved) {
        $k = $reg.OpenSubKey($old.Path, $true)
        if ($null -eq $k) { continue }
        try {
            $current = $k.GetValue('', $null)
            # Do not overwrite an association replaced by another app later.
            if ($current -ne $clsid) {
                Write-Warning "Pomijam zmienione skojarzenie: $($old.Path)"
                continue
            }
            if ($old.HasDefaultValue) {
                $k.SetValue('', [string]$old.Value)
            } else {
                $k.DeleteValue('', $false)
            }
        } finally { $k.Dispose() }
    }
    $reg.DeleteSubKeyTree($clsKey, $false)
    Remove-Item -LiteralPath $backupFile -Force
} finally { $reg.Dispose() }
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class XisfShellNotify {
    [DllImport("shell32.dll")] public static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
'@
[XisfShellNotify]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)
Write-Host 'Usunieto skojarzenia miniatur XISF i przywrocono poprzednie.' -ForegroundColor Green
Write-Host "Zainstalowane DLL pozostaja w $root - mozesz je usunac po restarcie Windows."
