# PowerShell script to back up Acer services before uninstalling PredatorSense
# Can be run from either a normal or an elevated PowerShell console.

$ErrorActionPreference = "SilentlyContinue"

# Check for Administrator privileges
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "Warning: Running as non-administrator. Service stopping/starting will be skipped, but files and registry settings will still be backed up." -ForegroundColor Yellow
}

$BackupDir = "c:\dev\PreySense\backup"
if (-not (Test-Path $BackupDir)) {
    New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null
}

Write-Host "==========================================" -ForegroundColor Green
Write-Host "Backing up Acer Services..." -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green

# 1. Capture Service Info/Diagnostics
Write-Host "Recording service configurations..." -ForegroundColor Cyan
$infoFile = Join-Path $BackupDir "service_info_backup.txt"
$servicesToQuery = @("AcerLightingService", "AcerServiceSvc", "AcerQAAgentSvis", "AcerApplicationBaseDriver_Device")

# Clear existing info file
if (Test-Path $infoFile) { Remove-Item $infoFile -Force }

foreach ($svc in $servicesToQuery) {
    Add-Content -Path $infoFile -Value "=== $svc ==="
    Add-Content -Path $infoFile -Value "--- sc qc ---"
    $qc = & sc.exe qc $svc
    Add-Content -Path $infoFile -Value $qc
    Add-Content -Path $infoFile -Value "--- sc queryex ---"
    $qex = & sc.exe queryex $svc
    Add-Content -Path $infoFile -Value $qex
    Add-Content -Path $infoFile -Value "--- Get-CimInstance ---"
    $cim = Get-CimInstance Win32_Service -Filter "Name='$svc'" | Format-List * | Out-String
    Add-Content -Path $infoFile -Value $cim
    Add-Content -Path $infoFile -Value "`n"
}
Write-Host "Saved diagnostics to: $infoFile" -ForegroundColor Yellow

# 2. Stop services cleanly (if admin)
if ($isAdmin) {
    Write-Host "Stopping services..." -ForegroundColor Cyan
    Stop-Service -Name AcerLightingService -Force
    Stop-Service -Name AcerServiceSvc -Force
    Stop-Service -Name AcerQAAgentSvis -Force
}

# 3. Export Registry Settings
Write-Host "Exporting service registry settings..." -ForegroundColor Cyan
reg export "HKLM\SYSTEM\CurrentControlSet\Services\AcerLightingService" (Join-Path $BackupDir "AcerLightingService.reg") /y | Out-Null
reg export "HKLM\SYSTEM\CurrentControlSet\Services\AcerServiceSvc" (Join-Path $BackupDir "AcerServiceSvc.reg") /y | Out-Null
reg export "HKLM\SYSTEM\CurrentControlSet\Services\AcerQAAgentSvis" (Join-Path $BackupDir "AcerQAAgentSvis.reg") /y | Out-Null
reg export "HKLM\SYSTEM\CurrentControlSet\Services\AcerApplicationBaseDriver_Device" (Join-Path $BackupDir "AcerApplicationBaseDriver_Device.reg") /y | Out-Null

# 4. Backup files
Write-Host "Copying service and driver files..." -ForegroundColor Cyan

# Find current DriverStore paths dynamically from registry
function Get-RegistryImagePath($svcName) {
    $path = (Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\$svcName" -Name ImagePath).ImagePath
    if ($path) {
        # Clean paths starting with \SystemRoot\
        $path = $path -replace '^\\SystemRoot\\', "$env:SystemRoot\"
        return $path
    }
    return $null
}

# AcerLightingService Files
$lightingPath = Get-RegistryImagePath "AcerLightingService"
if ($lightingPath) {
    $folder = Split-Path $lightingPath -Parent
    $folderName = Split-Path $folder -Leaf
    Write-Host "Backing up AcerLightingService from $folder..." -ForegroundColor Yellow
    Copy-Item -Path $folder -Destination (Join-Path $BackupDir $folderName) -Recurse -Force
}

# AcerServiceSvc Files
$serviceSvcPath = Get-RegistryImagePath "AcerServiceSvc"
if ($serviceSvcPath) {
    $folder = Split-Path $serviceSvcPath -Parent
    $folderName = Split-Path $folder -Leaf
    Write-Host "Backing up AcerServiceSvc from $folder..." -ForegroundColor Yellow
    Copy-Item -Path $folder -Destination (Join-Path $BackupDir $folderName) -Recurse -Force
}

# AcerApplicationBaseDriver Files
$baseDriverPath = Get-RegistryImagePath "AcerApplicationBaseDriver_Device"
if ($baseDriverPath) {
    $folder = Split-Path $baseDriverPath -Parent
    $folderName = Split-Path $folder -Leaf
    Write-Host "Backing up AcerApplicationBaseDriver_Device from $folder..." -ForegroundColor Yellow
    Copy-Item -Path $folder -Destination (Join-Path $BackupDir $folderName) -Recurse -Force
}

# AcerQAAgent.exe File
$qaAgentPath = Get-RegistryImagePath "AcerQAAgentSvis"
if ($qaAgentPath -and (Test-Path $qaAgentPath)) {
    Write-Host "Backing up AcerQAAgent.exe from $qaAgentPath..." -ForegroundColor Yellow
    Copy-Item -Path $qaAgentPath -Destination (Join-Path $BackupDir "AcerQAAgent.exe") -Force
}

# 5. Restart services (if admin)
if ($isAdmin) {
    Write-Host "Restarting services..." -ForegroundColor Cyan
    Start-Service -Name AcerLightingService
    Start-Service -Name AcerServiceSvc
    # QAAgent was disabled originally, start it only if it was running before
    $qaStatus = Get-Service -Name AcerQAAgentSvis
    if ($qaStatus.StartType -ne "Disabled") {
        Start-Service -Name AcerQAAgentSvis
    }
}

Write-Host "==========================================" -ForegroundColor Green
Write-Host "Backup completed successfully!" -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green
