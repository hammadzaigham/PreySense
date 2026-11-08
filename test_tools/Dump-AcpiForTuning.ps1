#Requires -Version 5.1
<#
.SYNOPSIS
  Dump ACPI tables and scan for PreySense ERM4 / Predator markers (for tuning support issues).

.DESCRIPTION
  Writes a folder you can zip and attach to a GitHub issue:
    - DSDT.bin and other tables exposed by GetSystemFirmwareTable
    - XSDT/RSDT listing of physical table pointers (all SSDTs appear here)
    - report.txt with ERM4 / Predator scans and Windows table enumeration

  Windows GetSystemFirmwareTable('SSDT') only returns ONE SSDT. To capture every
  SSDT binary, run elevated with PreySenseDriver available so this script can
  follow XSDT physical pointers through the PreySense driver.

.PARAMETER OutputDirectory
  Destination folder. Default: Desktop\PreySense-ACPI-<computer>-<timestamp>

.PARAMETER IncludePhysicalSsdtDump
  When elevated and PreySenseDriver can open, dump every XSDT-referenced table.
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory = "",
    [switch]$IncludePhysicalSsdtDump
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class AcpiNative
{
    public const uint Provider = 0x41435049; // 'ACPI'

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint EnumSystemFirmwareTables(uint provider, byte[] buffer, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint GetSystemFirmwareTable(uint provider, uint tableId, IntPtr buffer, uint size);

    public static uint Sig(string s)
    {
        if (s == null || s.Length != 4) throw new ArgumentException("signature");
        return (uint)(s[0] | (s[1] << 8) | (s[2] << 16) | (s[3] << 24));
    }

    public static string SigToString(uint id)
    {
        return new string(new[] {
            (char)(id & 0xFF),
            (char)((id >> 8) & 0xFF),
            (char)((id >> 16) & 0xFF),
            (char)((id >> 24) & 0xFF)
        });
    }

    public static byte[] GetTable(uint tableId)
    {
        uint size = GetSystemFirmwareTable(Provider, tableId, IntPtr.Zero, 0);
        if (size == 0) return null;
        IntPtr buf = Marshal.AllocHGlobal((int)size);
        try
        {
            uint written = GetSystemFirmwareTable(Provider, tableId, buf, size);
            if (written == 0) return null;
            byte[] data = new byte[written];
            Marshal.Copy(buf, data, 0, (int)written);
            return data;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }
}
"@

function Get-DefaultOutputDirectory {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $name = "PreySense-ACPI-$env:COMPUTERNAME-$stamp"
    return Join-Path ([Environment]::GetFolderPath("Desktop")) $name
}

function Write-Report([string]$Path, [string]$Message) {
    Add-Content -LiteralPath $Path -Value $Message -Encoding UTF8
    Write-Host $Message
}

function Find-Ascii([byte[]]$Bytes, [string]$Text) {
    $needle = [Text.Encoding]::ASCII.GetBytes($Text)
    $hits = New-Object System.Collections.Generic.List[int]
    for ($i = 0; $i -le $Bytes.Length - $needle.Length; $i++) {
        $ok = $true
        for ($j = 0; $j -lt $needle.Length; $j++) {
            if ($Bytes[$i + $j] -ne $needle[$j]) { $ok = $false; break }
        }
        if ($ok) { [void]$hits.Add($i) }
    }
    return $hits
}

function Find-Erm4OpRegion([byte[]]$Bytes) {
    # DefOpRegion: ExtOp 5B, OpRegionOp 80, NameSeg "ERM4"
    $needle = [byte[]](0x5B, 0x80, 0x45, 0x52, 0x4D, 0x34)
    $hits = New-Object System.Collections.Generic.List[int]
    for ($i = 0; $i -le $Bytes.Length - $needle.Length; $i++) {
        $ok = $true
        for ($j = 0; $j -lt $needle.Length; $j++) {
            if ($Bytes[$i + $j] -ne $needle[$j]) { $ok = $false; break }
        }
        if ($ok) { [void]$hits.Add($i) }
    }
    return $hits
}

function Get-TableLength([byte[]]$Bytes) {
    if ($null -eq $Bytes -or $Bytes.Length -lt 8) { return 0 }
    return [BitConverter]::ToUInt32($Bytes, 4)
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Get-DefaultOutputDirectory
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$report = Join-Path $OutputDirectory "report.txt"
"" | Set-Content -LiteralPath $report -Encoding UTF8

Write-Report $report "PreySense ACPI dump"
Write-Report $report ("Time: {0:u}" -f (Get-Date).ToUniversalTime())
Write-Report $report ("Computer: {0}" -f $env:COMPUTERNAME)
Write-Report $report ("User: {0}" -f $env:USERNAME)
Write-Report $report ("Output: {0}" -f $OutputDirectory)
Write-Report $report ""

# Enumerate signatures (SSDT may appear many times).
$enumSize = [AcpiNative]::EnumSystemFirmwareTables([AcpiNative]::Provider, $null, 0)
$enumBuf = New-Object byte[] $enumSize
[void][AcpiNative]::EnumSystemFirmwareTables([AcpiNative]::Provider, $enumBuf, $enumSize)
$ids = New-Object System.Collections.Generic.List[uint32]
for ($i = 0; $i + 3 -lt $enumBuf.Length; $i += 4) {
    [void]$ids.Add([BitConverter]::ToUInt32($enumBuf, $i))
}
$grouped = $ids | Group-Object | Sort-Object Name
Write-Report $report "EnumSystemFirmwareTables (ACPI):"
foreach ($g in $grouped) {
    $sig = [AcpiNative]::SigToString([uint32]$g.Name)
    Write-Report $report ("  {0} x{1}" -f $sig, $g.Count)
}
Write-Report $report ""

# Dump one blob per unique signature Windows will return.
$unique = $ids | Select-Object -Unique
$tablesDir = Join-Path $OutputDirectory "windows-tables"
New-Item -ItemType Directory -Force -Path $tablesDir | Out-Null
Write-Report $report "GetSystemFirmwareTable dumps (one blob per signature):"
foreach ($id in $unique) {
    $sig = [AcpiNative]::SigToString($id)
    $safe = ($sig -replace '[^\w\-]', '_')
    try {
        $data = [AcpiNative]::GetTable($id)
        if ($null -eq $data) {
            Write-Report $report ("  {0}: unavailable" -f $sig)
            continue
        }
        $path = Join-Path $tablesDir ("{0}.bin" -f $safe)
        [IO.File]::WriteAllBytes($path, $data)
        $sha = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        $asciiErm4 = (Find-Ascii $data "ERM4").Count
        $asciiPredator = (Find-Ascii $data "Predator").Count
        $op = (Find-Erm4OpRegion $data).Count
        Write-Report $report ("  {0}: {1} bytes sha256={2} ERM4={3} Predator={4} OpRegion(ERM4)={5}" -f `
            $sig, $data.Length, $sha, $asciiErm4, $asciiPredator, $op)
    }
    catch {
        Write-Report $report ("  {0}: error {1}" -f $sig, $_.Exception.Message)
    }
}
Write-Report $report ""

# Parse XSDT pointers so missing SSDTs are visible even before a physical dump.
$xsdt = [AcpiNative]::GetTable([AcpiNative]::Sig("XSDT"))
$rsdt = [AcpiNative]::GetTable([AcpiNative]::Sig("RSDT"))
$pointerList = Join-Path $OutputDirectory "xsdt-pointers.txt"
$lines = New-Object System.Collections.Generic.List[string]
if ($null -ne $xsdt -and $xsdt.Length -ge 36) {
    $len = Get-TableLength $xsdt
    if ($len -gt $xsdt.Length) { $len = $xsdt.Length }
    for ($off = 36; $off + 7 -lt $len; $off += 8) {
        $addr = [BitConverter]::ToUInt64($xsdt, $off)
        [void]$lines.Add(("XSDT[{0}] phys=0x{1:X16}" -f (($off - 36) / 8), $addr))
    }
    Write-Report $report ("XSDT entries: {0}" -f $lines.Count)
}
elseif ($null -ne $rsdt -and $rsdt.Length -ge 36) {
    $len = Get-TableLength $rsdt
    if ($len -gt $rsdt.Length) { $len = $rsdt.Length }
    for ($off = 36; $off + 3 -lt $len; $off += 4) {
        $addr = [BitConverter]::ToUInt32($rsdt, $off)
        [void]$lines.Add(("RSDT[{0}] phys=0x{1:X8}" -f (($off - 36) / 4), $addr))
    }
    Write-Report $report ("RSDT entries: {0}" -f $lines.Count)
}
else {
    Write-Report $report "XSDT/RSDT unavailable from GetSystemFirmwareTable."
}
$lines | Set-Content -LiteralPath $pointerList -Encoding UTF8

$ssdtCount = @($grouped | Where-Object { [AcpiNative]::SigToString([uint32]$_.Name) -eq "SSDT" } | Select-Object -ExpandProperty Count)
if (-not $ssdtCount) { $ssdtCount = 0 }
Write-Report $report ""
Write-Report $report "NOTES"
Write-Report $report "- PreySense looks for AML OperationRegion(ERM4) in DSDT, then probes 0xFE800400 / 0xFE708400 for ASCII 'Predator'."
Write-Report $report "- Windows exposes only one SSDT via GetSystemFirmwareTable('SSDT'), even when Enum lists many."
Write-Report $report ("- This machine enumerates SSDT x{0}. Use -IncludePhysicalSsdtDump as Administrator with PreySenseDriver to dump all of them." -f $ssdtCount)
Write-Report $report "- Zip this folder and attach it to the GitHub issue (DSDT.bin + report.txt are the minimum)."

if ($IncludePhysicalSsdtDump) {
    Write-Report $report ""
    Write-Report $report "Physical XSDT dump requested."
    $helperDir = Join-Path $env:TEMP ("PreySenseAcpiDump-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $helperDir | Out-Null
    $proj = Join-Path $helperDir "Dump.csproj"
    $prog = Join-Path $helperDir "Program.cs"
    $repoCore = Join-Path $PSScriptRoot "..\PreySense.Core\PreySense.Core.csproj"
    if (-not (Test-Path -LiteralPath $repoCore)) {
        Write-Report $report "Skipped physical dump: PreySense.Core project not found (clone the repo and run from test_tools)."
    }
    else {
        @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <PlatformTarget>x64</PlatformTarget>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$([IO.Path]::GetFullPath($repoCore))" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath $proj -Encoding UTF8

        @"
using System.Text;
using PreySense.Core.Hardware;

static bool ReadBytes(IPhysicalMemoryByteAccess mem, ulong addr, byte[] buf)
{
    for (int i = 0; i < buf.Length; i++)
    {
        if (!mem.ReadPhysicalByte(addr + (ulong)i, out buf[i])) return false;
    }
    return true;
}

string outDir = args.Length > 0 ? args[0] : ".";
Directory.CreateDirectory(outDir);
var log = new StringBuilder();
void L(string s) { Console.WriteLine(s); log.AppendLine(s); }

var backend = new PreySenseDriverBackend();
if (!backend.Initialize(out string? error, attemptStartService: true))
{
    L(error ?? "Driver init failed");
    File.WriteAllText(Path.Combine(outDir, "physical-dump.log"), log.ToString());
    return 1;
}

try
{
    string xsdtPath = Path.Combine(outDir, "windows-tables", "XSDT.bin");
    if (!File.Exists(xsdtPath))
    {
        L("windows-tables/XSDT.bin missing; run the script without skipping the Windows dump first.");
        return 2;
    }
    byte[] xsdt = File.ReadAllBytes(xsdtPath);
    if (xsdt.Length < 44) { L("XSDT too small"); return 3; }
    uint length = BitConverter.ToUInt32(xsdt, 4);
    if (length > xsdt.Length) length = (uint)xsdt.Length;

    string physDir = Path.Combine(outDir, "physical-tables");
    Directory.CreateDirectory(physDir);
    int index = 0;
    for (int off = 36; off + 7 < length; off += 8, index++)
    {
        ulong addr = BitConverter.ToUInt64(xsdt, off);
        var header = new byte[8];
        if (!ReadBytes(backend, addr, header))
        {
            L($"[{index}] 0x{addr:X16}: read header failed");
            continue;
        }
        string sig = Encoding.ASCII.GetString(header, 0, 4);
        uint tableLen = BitConverter.ToUInt32(header, 4);
        if (tableLen < 8 || tableLen > 8 * 1024 * 1024)
        {
            L($"[{index}] 0x{addr:X16}: bad length {tableLen}");
            continue;
        }
        var body = new byte[tableLen];
        if (!ReadBytes(backend, addr, body))
        {
            L($"[{index}] 0x{addr:X16} {sig}: body read failed");
            continue;
        }
        string safe = string.Concat(sig.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
        string path = Path.Combine(physDir, $"{index:D2}_{safe}_0x{addr:X}.bin");
        File.WriteAllBytes(path, body);
        bool erm4 = Encoding.ASCII.GetString(body).Contains("ERM4", StringComparison.Ordinal);
        bool predator = Encoding.ASCII.GetString(body).Contains("Predator", StringComparison.Ordinal);
        L($"[{index}] 0x{addr:X16} {sig} {tableLen} bytes erm4={erm4} predator={predator} -> {Path.GetFileName(path)}");
    }

    // Known / reported candidate bases for Predator signature.
    ulong[] candidates = [0xFE800400UL, 0xFE708400UL, 0xFE0B0800UL, 0xFE0B0800UL + 0x400];
    L("");
    L("Predator signature probes:");
    foreach (ulong baseAddr in candidates)
    {
        bool hit = AcerErm4Table.HasPredatorSignature(backend, baseAddr);
        L($"  0x{baseAddr:X} Predator={hit}");
    }
}
finally
{
    backend.Dispose();
    File.WriteAllText(Path.Combine(outDir, "physical-dump.log"), log.ToString());
}
return 0;
"@ | Set-Content -LiteralPath $prog -Encoding UTF8

        Write-Report $report "Building physical dump helper..."
        dotnet build $proj -c Release --nologo -v q
        if ($LASTEXITCODE -ne 0) {
            Write-Report $report "Physical dump helper build failed."
        }
        else {
            $exe = Join-Path $helperDir "bin\Release\net10.0-windows\Dump.exe"
            # Prefer elevated if not admin.
            $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).
                IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
            if (-not $isAdmin) {
                Write-Report $report "Physical dump needs Administrator (driver device access). Relaunching elevated..."
                $p = Start-Process -FilePath $exe -ArgumentList @("`"$OutputDirectory`"") -Verb RunAs -Wait -PassThru
                Write-Report $report ("Physical dump exit code: {0}" -f $p.ExitCode)
            }
            else {
                & $exe $OutputDirectory
                Write-Report $report ("Physical dump exit code: {0}" -f $LASTEXITCODE)
            }
            $physLog = Join-Path $OutputDirectory "physical-dump.log"
            if (Test-Path -LiteralPath $physLog) {
                Write-Report $report "--- physical-dump.log ---"
                Get-Content -LiteralPath $physLog | ForEach-Object { Write-Report $report $_ }
            }
        }
    }
}

Write-Report $report ""
Write-Report $report "Done. Zip the output folder and attach it to the issue."
Write-Host ""
Write-Host "Output: $OutputDirectory"
