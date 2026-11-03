# Exercises only the file-copy section, using temporary folders. No storage,
# registry, WMI, elevation, or boot configuration commands are executed.
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$scriptPath = Join-Path $repository 'tools\OpenCore_PreyBoost_EFI\Reinstall-To-ESP.ps1'
$scriptText = [IO.File]::ReadAllText($scriptPath)
$tokens = $null
$errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw ($errors | Out-String) }
$forbidden = @('Remove-Partition', 'New-Partition', 'Resize-Partition', 'Format-Volume', 'Clear-Disk', 'Set-Partition', 'diskpart', 'bcdedit', 'bcdboot')
foreach ($command in $ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.CommandAst] }, $true)) {
    if ($command.GetCommandName() -in $forbidden) { throw 'Reinstall must not change partition layout or boot entries.' }
}
$start = $scriptText.IndexOf('    # Reject redirected directories')
$end = $scriptText.IndexOf('    & $helper -Action Arm', $start)
if ($start -lt 0 -or $end -lt 0) { throw 'Copy section was not found.' }
$copySection = [scriptblock]::Create($scriptText.Substring($start, $end - $start))
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('PreySense-Reinstall-Test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
try {
    $sourceEfi = Join-Path $testRoot 'source\EFI'
    $targetEfi = Join-Path $testRoot 'target\EFI'
    New-Item -ItemType Directory -Path (Join-Path $sourceEfi 'BOOT'), (Join-Path $sourceEfi 'OC\ACPI'), (Join-Path $targetEfi 'OC\ACPI') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $sourceEfi 'BOOT\BOOTX64.EFI'), 'new boot')
    [IO.File]::WriteAllText((Join-Path $sourceEfi 'OC\ACPI\SSDT-PREYDEV.aml'), 'new ACPI')
    [IO.File]::WriteAllText((Join-Path $targetEfi 'OC\ACPI\SSDT-PREYDEV.aml'), 'old ACPI')
    [IO.File]::WriteAllText((Join-Path $targetEfi 'unrelated.efi'), 'preserve this')
    $sourcePrefix = $sourceEfi.TrimEnd('\') + '\'
    $targetPrefix = $targetEfi.TrimEnd('\') + '\'
    $written = [System.Collections.Generic.List[object]]::new()
    . $copySection
    if ([IO.File]::ReadAllText((Join-Path $targetEfi 'OC\ACPI\SSDT-PREYDEV.aml')) -ne 'new ACPI') { throw 'ACPI was not updated.' }
    if ([IO.File]::ReadAllText((Join-Path $targetEfi 'unrelated.efi')) -ne 'preserve this') { throw 'Unrelated EFI file changed.' }
    if (-not (Test-Path -LiteralPath (Join-Path $targetEfi 'BOOT\BOOTX64.EFI') -PathType Leaf)) { throw 'New subdirectory copy failed.' }
    $written = [System.Collections.Generic.List[object]]::new()
    . $copySection
    if ([IO.File]::ReadAllText((Join-Path $targetEfi 'OC\ACPI\SSDT-PREYDEV.aml')) -ne 'new ACPI') { throw 'Repeated reinstall failed.' }
    if ($scriptText.Contains('EfiBackups') -or $scriptText.Contains('$backupRoot') -or $scriptText.Contains('entry.Backup')) { throw 'Updater must not create previous-file backups.' }
    Write-Host 'PASS: PowerShell 5.1 syntax; no partition mutations; in-place overwrite; no previous-file backups; new directories; unrelated file preservation; repeated reinstall.'
}
finally {
    $resolvedTest = [IO.Path]::GetFullPath($testRoot)
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if ($resolvedTest.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedTest).StartsWith('PreySense-Reinstall-Test-')) {
        Remove-Item -LiteralPath $resolvedTest -Recurse -Force
    }
}
