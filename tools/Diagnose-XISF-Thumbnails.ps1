#Requires -Version 5.1
[CmdletBinding()]
param([string]$XisfFile = '')
$ErrorActionPreference = 'Stop'
$reportPath = Join-Path $env:LOCALAPPDATA 'AstroXisfThumbnail\diagnostics.txt'
$reportDir = Split-Path -Parent $reportPath
New-Item -ItemType Directory -Path $reportDir -Force | Out-Null
$script:lines = New-Object 'System.Collections.Generic.List[string]'
function Log([string]$message) {
    Write-Host $message
    $script:lines.Add($message)
}
function HrText([int]$hr) { return ('0x{0:X8}' -f [BitConverter]::ToUInt32([BitConverter]::GetBytes($hr), 0)) }
function RegDefault($base, [string]$path, [string]$valueName = '') {
    $key = $base.OpenSubKey($path)
    if ($null -eq $key) { return '[BRAK KLUCZA]' }
    try {
        $result = $key.GetValue($valueName, $null)
        if ($null -eq $result) { return '[BRAK WARTOSCI]' }
        return [string]$result
    } finally { $key.Close() }
}
$cid = '{68F756A5-4977-4DF0-B306-AE9812B57EC8}'
$iid = '{E357FCCD-A995-4576-B01F-234630154E96}'
$thumbRelative = 'shellex\' + $iid
if (-not [Environment]::Is64BitProcess) {
    throw 'Uruchom w 64-bitowym Windows PowerShell 5.1, nie w 32-bitowym.'
}
Log '=== XISF Thumbnail: DIAGNOSTYKA (TYLKO ODCZYT REJESTRU) ==='
Log ('Windows: ' + [Environment]::OSVersion.VersionString)
Log ('PowerShell: ' + $PSVersionTable.PSVersion)
Log ('Proces 64 bit: ' + [Environment]::Is64BitProcess)
Log ''
$cu = [Microsoft.Win32.RegistryKey]::OpenBaseKey('CurrentUser','Registry64')
$cr = [Microsoft.Win32.RegistryKey]::OpenBaseKey('ClassesRoot','Registry64')
try {
    $classPath = 'CLSID\' + $cid + '\InprocServer32'
    Log ('HKCR\' + $classPath)
    Log ('  DLL: ' + (RegDefault $cr $classPath))
    Log ('  Assembly: ' + (RegDefault $cr $classPath 'Assembly'))
    Log ('  CodeBase: ' + (RegDefault $cr $classPath 'CodeBase'))
    Log ('  ThreadingModel: ' + (RegDefault $cr $classPath 'ThreadingModel'))
    Log ('HKCU\Software\Classes\' + $classPath)
    Log ('  DLL: ' + (RegDefault $cu ('Software\Classes\' + $classPath)))
    Log ''
    $choice = RegDefault $cu 'Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.xisf\UserChoice' 'ProgId'
    $extProgId = RegDefault $cr '.xisf'
    Log ('Domyslny ProgID (UserChoice): ' + $choice)
    Log ('ProgID z HKCR\.xisf: ' + $extProgId)
    $assocKeys = @('.xisf','SystemFileAssociations\.xisf')
    if ($choice -and -not $choice.StartsWith('[')) { $assocKeys += $choice }
    if ($extProgId -and -not $extProgId.StartsWith('[')) { $assocKeys += $extProgId }
    $assocKeys = $assocKeys | Select-Object -Unique
    foreach ($assoc in $assocKeys) {
        Log ('HKCR\' + $assoc + '\' + $thumbRelative)
        Log ('  Provider: ' + (RegDefault $cr ($assoc + '\' + $thumbRelative)))
    }
    Log ''
    $dllCodebase = RegDefault $cr $classPath 'CodeBase'
    if ($dllCodebase -like 'file:*') {
        try {
            $uri = [Uri]$dllCodebase
            Log ('Sciezka COM DLL istnieje: ' + (Test-Path -LiteralPath $uri.LocalPath))
            Log ('Sciezka COM DLL: ' + $uri.LocalPath)
            $zstd = Join-Path (Split-Path -Parent $uri.LocalPath) 'ZstdSharp.dll'
            Log ('Biblioteka ZstdSharp istnieje: ' + (Test-Path -LiteralPath $zstd))
        } catch { Log ('Sciezka COM: ' + $_.Exception.Message) }
    }
} finally { $cu.Close(); $cr.Close() }
Log ''

# COM activation and actual Shell thumbnail request, independent of manual smoke test.
try {
$cs = @'
using System;
using System.Runtime.InteropServices;
namespace AstroXisfDiagnostics {
    [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx; public int cy; public SIZE(int a,int b) {cx=a;cy=b;} }
    [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItemImageFactory {
       [PreserveSig] int GetImage(SIZE size, uint flags, out IntPtr hBitmap);
    }
    public static class Native {
       [DllImport("ole32.dll", PreserveSig=true)]
       public static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, out IntPtr obj);
       [DllImport("shell32.dll", CharSet=CharSet.Unicode, PreserveSig=true)]
       public static extern int SHCreateItemFromParsingName([MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr bindCtx, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);
       [DllImport("gdi32.dll", SetLastError=true)] [return: MarshalAs(UnmanagedType.Bool)]
       public static extern bool DeleteObject(IntPtr hObject);
       public static string ProbeShell(string path) {
           Guid factoryGuid = new Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B");
           IShellItemImageFactory factory = null;
           int hr = SHCreateItemFromParsingName(path, IntPtr.Zero, ref factoryGuid, out factory);
           if (hr != 0) return "SHCreateItemFromParsingName: 0x" + hr.ToString("X8");
           IntPtr bitmap = IntPtr.Zero;
           try {
               // THUMBNAILONLY=8: an icon is not enough to count as success.
               hr = factory.GetImage(new SIZE(256, 256), 8u, out bitmap);
               return "IShellItemImageFactory.GetImage(THUMBNAILONLY): 0x" + hr.ToString("X8") +
                   (bitmap == IntPtr.Zero ? ", HBITMAP=NULL" : ", HBITMAP=OK");
           } finally {
               if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
               if (factory != null) Marshal.ReleaseComObject(factory);
           }
       }
    }
}
'@
    if (-not ('AstroXisfDiagnostics.Native' -as [type])) { Add-Type -TypeDefinition $cs -ErrorAction Stop }
    $guid = [Guid]$cid
    $iunknown = [Guid]'00000000-0000-0000-C000-000000000046'
    $pointer = [IntPtr]::Zero
    $hr = [AstroXisfDiagnostics.Native]::CoCreateInstance([ref]$guid, [IntPtr]::Zero, [uint32]1, [ref]$iunknown, [ref]$pointer)
    Log ('COM CoCreateInstance: ' + (HrText $hr))
    if ($hr -eq 0 -and $pointer -ne [IntPtr]::Zero) {
        try {
            foreach ($interfaceId in @('B824B49D-22AC-4161-AC8A-9916E8FA3F7F', 'E357FCCD-A995-4576-B01F-234630154E96')) {
                $interfaceGuid = [Guid]$interfaceId
                $found = [IntPtr]::Zero
                $qhr = [Runtime.InteropServices.Marshal]::QueryInterface($pointer, [ref]$interfaceGuid, [ref]$found)
                Log ('COM QueryInterface ' + $interfaceId + ': ' + (HrText $qhr))
                if ($found -ne [IntPtr]::Zero) { [void][Runtime.InteropServices.Marshal]::Release($found) }
            }
        } finally { [void][Runtime.InteropServices.Marshal]::Release($pointer) }
    }
} catch { Log ('COM diagnostyka nie powiodla sie: ' + $_.Exception.ToString()) }
Log ''

if ([string]::IsNullOrWhiteSpace($XisfFile)) {
    $candidate = Get-ChildItem -LiteralPath (Get-Location).Path -Filter '*.xisf' -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($candidate) { $XisfFile = $candidate.FullName }
}
if (-not [string]::IsNullOrWhiteSpace($XisfFile) -and (Test-Path -LiteralPath $XisfFile -PathType Leaf)) {
    try {
        $XisfFile = (Resolve-Path -LiteralPath $XisfFile).Path
        Log ('Test przez Windows Shell: ' + $XisfFile)
        Log ([AstroXisfDiagnostics.Native]::ProbeShell($XisfFile))
    } catch { Log ('Test przez Windows Shell nie powiodl sie: ' + $_.Exception.ToString()) }
} else { Log 'Brak wskazanego XISF. Aby sprawdzic Windows Shell, uruchom skrypt z -XisfFile "D:\...\plik.xisf".' }
Log ''
$logFile = Join-Path $reportDir 'errors.log'
if (Test-Path $logFile) {
    Log ('Plik errors.log istnieje. Ostatnie wpisy:')
    Get-Content -LiteralPath $logFile -Tail 8 | ForEach-Object { Log $_ }
} else { Log 'errors.log nie istnieje (brak wywolania GetThumbnail albo brak bledu wewnatrz metody).' }
[System.IO.File]::WriteAllLines($reportPath, [string[]]$script:lines, (New-Object System.Text.UTF8Encoding($false)))
Write-Host ''
Write-Host ('RAPORT ZAPISANY: ' + $reportPath) -ForegroundColor Green
Write-Host 'Wklej tu raport z diagnostics.txt (ewentualnie bez pelnej sciezki do prywatnych plikow).' -ForegroundColor Yellow
