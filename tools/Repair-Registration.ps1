#Requires -Version 5.1
<##
XISF thumbnail association repair for existing AstroXisf ThumbnailProvider v0.3.1.
Touches only per-user IThumbnailProvider associations (HKCU\Software\Classes).
Does NOT modify .xisf default application / ProgID / existing COM registration.
Run in Windows PowerShell 5.1 x64 as ordinary user.
##>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
if (-not [Environment]::Is64BitProcess) {
    throw 'Uruchom 64-bitowy Windows PowerShell.'
}

$clsid = '{68F756A5-4977-4DF0-B306-AE9812B57EC8}'
$thumbIid = '{E357FCCD-A995-4576-B01F-234630154E96}'
$baseDir = Join-Path $env:LOCALAPPDATA 'AstroXisfThumbnail'
$backupPath = Join-Path $baseDir 'registry-associations-hotfix-backup.json'

$cu = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
    [Microsoft.Win32.RegistryHive]::CurrentUser,
    [Microsoft.Win32.RegistryView]::Registry64)
$cr = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
    [Microsoft.Win32.RegistryHive]::ClassesRoot,
    [Microsoft.Win32.RegistryView]::Registry64)

try {
    $com = $cr.OpenSubKey(('CLSID\' + $clsid + '\InprocServer32'))
    if ($null -eq $com) {
        throw 'Nie znalazlem naszej rejestracji COM. Najpierw zainstaluj obecna wersje XISF Thumbnails.'
    }
    try {
        $codeBase = [string]$com.GetValue('CodeBase', '')
        if (-not $codeBase.StartsWith('file:', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Rejestracja COM nie ma poprawnej sciezki CodeBase.'
        }
        $dllPath = ([Uri]$codeBase).LocalPath
        if (-not (Test-Path -LiteralPath $dllPath -PathType Leaf)) {
            throw ('Nie ma biblioteki COM: ' + $dllPath)
        }
    } finally { $com.Dispose() }

    # The file type may use PixInsight's ProgID, so cover both the extension
    # and the active ProgID. We do NOT change the default ProgID.
    $progIds = New-Object 'System.Collections.Generic.List[string]'
    $extensionKey = $cr.OpenSubKey('.xisf')
    if ($null -ne $extensionKey) {
        try {
            $p = [string]$extensionKey.GetValue('', '')
            if (-not [string]::IsNullOrWhiteSpace($p)) { $progIds.Add($p) }
        } finally { $extensionKey.Dispose() }
    }
    $userChoice = $cu.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.xisf\UserChoice')
    if ($null -ne $userChoice) {
        try {
            $p = [string]$userChoice.GetValue('ProgId', '')
            if (-not [string]::IsNullOrWhiteSpace($p) -and -not $progIds.Contains($p)) { $progIds.Add($p) }
        } finally { $userChoice.Dispose() }
    }

    $targets = New-Object 'System.Collections.Generic.List[string]'
    $targets.Add(('Software\Classes\.xisf\ShellEx\' + $thumbIid))
    $targets.Add(('Software\Classes\SystemFileAssociations\.xisf\ShellEx\' + $thumbIid))
    foreach ($p in $progIds) {
        $targets.Add(('Software\Classes\' + $p + '\ShellEx\' + $thumbIid))
    }

    New-Item -ItemType Directory -Path $baseDir -Force | Out-Null
    $alreadyBackedUp = Test-Path -LiteralPath $backupPath
    if (-not $alreadyBackedUp) {
        $snapshot = @()
        foreach ($path in $targets) {
            $k = $cu.OpenSubKey($path, $false)
            try {
                $hasDefault = ($null -ne $k) -and (@($k.GetValueNames()) -contains '')
                $oldValue = if ($hasDefault) { [string]$k.GetValue('', '') } else { $null }
                $snapshot += [PSCustomObject]@{
                    Path = $path
                    HadDefaultValue = [bool]$hasDefault
                    PreviousValue = $oldValue
                }
            } finally { if ($null -ne $k) { $k.Dispose() } }
        }
        [System.IO.File]::WriteAllText(
            $backupPath,
            ($snapshot | ConvertTo-Json -Depth 4),
            (New-Object System.Text.UTF8Encoding($false)))
    } else {
        Write-Warning 'Poprzednia kopia zapasowa istnieje; nie nadpisuje jej. Naprawa skojarzen zostanie ponowiona.'
    }

    foreach ($path in $targets) {
        $k = $cu.CreateSubKey($path)
        if ($null -eq $k) { throw ('Nie mozna utworzyc: HKCU\' + $path) }
        try {
            $k.SetValue('', $clsid, [Microsoft.Win32.RegistryValueKind]::String)
        } finally { $k.Dispose() }
    }

    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class XisfAssocNotifyV034 {
    [DllImport("shell32.dll")]
    public static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
'@ -ErrorAction Stop
    [XisfAssocNotifyV034]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)

    Write-Host ''
    Write-Host 'ZAPISANO SKOJARZENIA MINIATUREK XISF:' -ForegroundColor Green
    foreach ($path in $targets) {
        $cuKey = $cu.OpenSubKey($path)
        $rel = $path.Substring('Software\Classes\'.Length)
        $crKey = $cr.OpenSubKey($rel)
        try {
            $vCU = if ($null -eq $cuKey) { '[BRAK]' } else { [string]$cuKey.GetValue('', '[BRAK WARTOSCI]') }
            $vCR = if ($null -eq $crKey) { '[BRAK]' } else { [string]$crKey.GetValue('', '[BRAK WARTOSCI]') }
            Write-Host ('  HKCU\' + $path)
            Write-Host ('    HKCU=' + $vCU)
            Write-Host ('    HKCR=' + $vCR)
        } finally {
            if ($null -ne $cuKey) { $cuKey.Dispose() }
            if ($null -ne $crKey) { $crKey.Dispose() }
        }
    }
    Write-Host ''
    Write-Host ('Kopia poprzednich wartosci: ' + $backupPath)
    Write-Host 'W Eksploratorze ustaw: Widok -> Bardzo duze ikony.'
    Write-Host 'Jesli nic sie nie zmieni, uruchom ponownie Eksplorator Windows.'
    Write-Host 'Dalej testuj skrypt Diagnose-XISF-Thumbnails.ps1 z parametrem -XisfFile.'
    Write-Host 'Zmiany tej poprawki cofniesz przez: Undo-Registration-Fix.ps1'
} finally {
    $cu.Dispose()
    $cr.Dispose()
}
