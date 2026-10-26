#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Updates files on the existing PREYSENSE EFI partition in place.
.DESCRIPTION
    Validates the packaged ACPI table, backs up overwritten files, copies and
    verifies the payload, then arms WMI repair for the next boot. Partition
    layout, formatting, boot entries, and unrelated EFI files are preserved.
#>
param([string]$ResultPath)

$ErrorActionPreference = 'Stop'
$succeeded = $false
$failureMessage = $null
$mountedHere = $false
$partition = $null
$mountPath = $null
$backupRoot = $null
$written = [System.Collections.Generic.List[object]]::new()

try {
    $sourceEfi = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'EFI'))
    $helper = Join-Path $PSScriptRoot 'Repair-PreySenseWmi.ps1'
    foreach ($required in @('BOOT\BOOTX64.EFI', 'OC\OpenCore.efi', 'OC\config.plist', 'OC\ACPI\SSDT-PREYDEV.aml')) {
        if (-not (Test-Path -LiteralPath (Join-Path $sourceEfi $required) -PathType Leaf)) {
            throw "Missing packaged file: $required"
        }
    }
    if (-not (Test-Path -LiteralPath $helper -PathType Leaf)) { throw 'WMI repair helper is missing.' }
    $aml = [IO.File]::ReadAllBytes((Join-Path $sourceEfi 'OC\ACPI\SSDT-PREYDEV.aml'))
    if ($aml.Length -lt 36 -or [Text.Encoding]::ASCII.GetString($aml, 0, 4) -ne 'SSDT' -or
        [BitConverter]::ToUInt32($aml, 4) -ne $aml.Length) { throw 'Invalid packaged ACPI table.' }
    $checksum = 0
    foreach ($value in $aml) { $checksum = ($checksum + $value) -band 255 }
    if ($checksum -ne 0) { throw 'The packaged ACPI checksum is invalid.' }

    $volumes = @(Get-Volume | Where-Object { $_.FileSystemLabel -eq 'PREYSENSE' })
    if ($volumes.Count -ne 1) { throw 'Exactly one PREYSENSE volume is required. No partition was changed.' }
    $partitions = @(Get-Partition -Volume $volumes[0])
    if ($partitions.Count -ne 1) { throw 'PREYSENSE partition identity is ambiguous.' }
    $partition = $partitions[0]
    $windows = Get-Partition -DriveLetter $env:SystemDrive.Substring(0, 1)
    $allowedTypes = @([guid]'C12A7328-F81F-11D2-BA4B-00A0C93EC93B', [guid]'C0DE0001-5052-4559-534E-4F4330310001')
    if ($partition.DiskNumber -ne $windows.DiskNumber -or [guid]$partition.GptType -notin $allowedTypes) {
        throw 'PREYSENSE is not a recognized partition on the Windows disk.'
    }
    if ($partition.DriveLetter) {
        $mountPath = "$($partition.DriveLetter):\"
    }
    else {
        $used = @((Get-Volume).DriveLetter) + @((Get-PSDrive -PSProvider FileSystem).Name)
        $letter = @('S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z') | Where-Object { $_ -notin $used } | Select-Object -First 1
        if (-not $letter) { throw 'No free drive letter is available.' }
        $mountPath = "${letter}:\"
        Add-PartitionAccessPath -DiskNumber $partition.DiskNumber -PartitionNumber $partition.PartitionNumber -AccessPath $mountPath
        $mountedHere = $true
    }

    $targetEfi = [IO.Path]::GetFullPath((Join-Path $mountPath 'EFI'))
    $sourcePrefix = $sourceEfi.TrimEnd('\') + '\'
    $targetPrefix = $targetEfi.TrimEnd('\') + '\'
    $backupRoot = Join-Path $env:LOCALAPPDATA ('PreySense\EfiBackups\' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null

    # Reject redirected directories before copying any payload files.
    foreach ($item in @(Get-Item -LiteralPath $sourceEfi) + @(Get-ChildItem -LiteralPath $sourceEfi -Recurse -Force)) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'The source contains a redirected path.' }
    }
    if (Test-Path -LiteralPath $targetEfi) {
        foreach ($item in @(Get-Item -LiteralPath $targetEfi) + @(Get-ChildItem -LiteralPath $targetEfi -Recurse -Force)) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'The destination contains a redirected path.' }
        }
    }
    $files = @(Get-ChildItem -LiteralPath $sourceEfi -File -Recurse -Force)
    $plan = foreach ($file in $files) {
        $source = [IO.Path]::GetFullPath($file.FullName)
        if (-not $source.StartsWith($sourcePrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Source path escaped EFI.' }
        $relative = $source.Substring($sourcePrefix.Length)
        $destination = [IO.Path]::GetFullPath((Join-Path $targetEfi $relative))
        if (-not $destination.StartsWith($targetPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Destination path escaped EFI.' }
        $backup = Join-Path $backupRoot $relative
        $existed = Test-Path -LiteralPath $destination
        if ($existed) {
            if (-not (Test-Path -LiteralPath $destination -PathType Leaf)) { throw "A directory blocks $relative" }
            New-Item -ItemType Directory -Path (Split-Path -Parent $backup) -Force | Out-Null
            Copy-Item -LiteralPath $destination -Destination $backup -Force
        }
        [pscustomobject]@{ Source = $source; Destination = $destination; Backup = $backup; Existed = $existed }
    }

    # Create destination directories explicitly; never copy a container onto a leaf.
    foreach ($entry in $plan) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $entry.Destination) -Force | Out-Null
        $written.Add($entry)
        Copy-Item -LiteralPath $entry.Source -Destination $entry.Destination -Force
        if ((Get-FileHash -LiteralPath $entry.Source -Algorithm SHA256).Hash -ne
            (Get-FileHash -LiteralPath $entry.Destination -Algorithm SHA256).Hash) {
            throw "File verification failed: $($entry.Destination)"
        }
        Write-Host ("Updated and verified: EFI\" + $entry.Source.Substring($sourcePrefix.Length)) -ForegroundColor Green
    }
    & $helper -Action Arm
    if (-not $?) { throw 'Could not arm WMI registration. Reinstall is incomplete.' }
    $succeeded = $true
}
catch {
    $failure = $_
    for ($index = $written.Count - 1; $index -ge 0; $index--) {
        $entry = $written[$index]
        try {
            if ($entry.Existed) {
                Copy-Item -LiteralPath $entry.Backup -Destination $entry.Destination -Force
            }
            elseif ($entry.Destination.StartsWith($targetPrefix, [StringComparison]::OrdinalIgnoreCase)) {
                Remove-Item -LiteralPath $entry.Destination -Force -ErrorAction SilentlyContinue
            }
        }
        catch { Write-Warning "Could not roll back $($entry.Destination). Backup: $backupRoot" }
    }
    Write-Host "Reinstall failed: $failure" -ForegroundColor Red
    if ($backupRoot) { Write-Host "Backup: $backupRoot" }
    $failureMessage = "$failure"
}
finally {
    if ($mountedHere) {
        Remove-PartitionAccessPath -DiskNumber $partition.DiskNumber -PartitionNumber $partition.PartitionNumber -AccessPath $mountPath -ErrorAction Continue
    }
}

# Report completion before waiting for input so the app can leave its busy state
# while this console remains open.
if ($ResultPath) {
    try {
        $result = @{ Success = $succeeded; Message = $failureMessage } | ConvertTo-Json -Compress
        [IO.File]::WriteAllText($ResultPath + '.pending', $result)
        Move-Item -LiteralPath ($ResultPath + '.pending') -Destination $ResultPath -Force
    }
    catch { Write-Warning "Could not report completion to PreySense: $_" }
}

if ($succeeded) {
    Write-Host ''
    Write-Host 'FILES UPDATED SUCCESSFULLY' -ForegroundColor Green
    Write-Host ("Updated and SHA256-verified {0} files. ACPI interface: 0x{1:X4}." -f $written.Count, [BitConverter]::ToUInt32($aml, 24))
    Write-Host "Previous files are backed up at: $backupRoot"
    Write-Host 'Restart through the PreySense/OpenCore boot entry to load the updated ACPI files.' -ForegroundColor Yellow
    do {
        $restart = (Read-Host 'Restart Windows now? (Y/N)').Trim()
    } while ($restart -notmatch '^[YyNn]$')
    if ($restart -match '^[Yy]$') {
        Write-Host 'Restarting Windows...' -ForegroundColor Yellow
        & shutdown.exe /r /t 0 /c 'PreySense ACPI and OpenCore files updated.'
        if ($LASTEXITCODE -ne 0) { Write-Warning 'Windows could not restart. Restart manually through PreySense.' }
    }
    else {
        Write-Host 'Restart skipped. Files are updated; the current boot still uses the previous ACPI table.' -ForegroundColor Yellow
    }
}
