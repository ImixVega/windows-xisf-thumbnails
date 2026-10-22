#Requires -Version 5.1
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
if (-not [Environment]::Is64BitProcess) { throw 'Uruchom 64-bitowy Windows PowerShell.' }
$clsid = '{68F756A5-4977-4DF0-B306-AE9812B57EC8}'
$baseDir = Join-Path $env:LOCALAPPDATA 'AstroXisfThumbnail'
$backupPath = Join-Path $baseDir 'registry-associations-hotfix-backup.json'
if (-not (Test-Path -LiteralPath $backupPath)) {
    Write-Host 'Brak kopii rejestru dla tej poprawki; nic nie zmieniono.'
    return
}
$reg = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
    [Microsoft.Win32.RegistryHive]::CurrentUser,
    [Microsoft.Win32.RegistryView]::Registry64)
try {
    $entries = @(Get-Content -LiteralPath $backupPath -Raw | ConvertFrom-Json)
    foreach ($entry in $entries) {
        $k = $reg.OpenSubKey([string]$entry.Path, $true)
        if ($null -eq $k) { continue }
        try {
            $current = [string]$k.GetValue('', '')
            if ($current -ne $clsid) {
                Write-Warning ('Pozostawiam skojarzenie zmienione przez inny program: ' + $entry.Path)
                continue
            }
            if ($entry.HadDefaultValue) {
                $k.SetValue('', [string]$entry.PreviousValue, [Microsoft.Win32.RegistryValueKind]::String)
            } else {
                $k.DeleteValue('', $false)
            }
            Write-Host ('Przywrocono HKCU\' + $entry.Path)
        } finally { $k.Dispose() }
    }
} finally { $reg.Dispose() }
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class XisfAssocUndoV034 {
    [DllImport("shell32.dll")]
    public static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
'@
[XisfAssocUndoV034]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)
Remove-Item -LiteralPath $backupPath -Force
Write-Host 'Cofnieto skojarzenia dodane poprawka. Pozostawiono dzialajacy QuickLook i DLL XISF.' -ForegroundColor Green
