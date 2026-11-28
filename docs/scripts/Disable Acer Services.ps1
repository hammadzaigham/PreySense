# Disable Acer and PredatorSense OEM services and stop leftover background processes.
# Run as Administrator.

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator
)
if (-not $isAdmin) {
    Write-Error "Please run this script as Administrator."
    exit 1
}

$knownServices = @(
    "AASSvc",
    "AcerCCAgentSvis",
    "AcerDeviceEnablingServiceV2",
    "AcerDIAgentSvis",
    "AcerLightingService",
    "AcerQAAgentSvis",
    "AcerServiceSvc",
    "AICO2_svc",
    "AICO_svc",
    "ASMSvc",
    "AcerSysHardwareService",
    "AcerHardwareService",
    "PredatorSenseService",
    "AcerCentralService"
)

$excludedServices = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
@(
    "PreySense",
    "Psched",
    "IntcAzAudAddService",
    "DtsApo4Service",
    "AcerApplicationBaseDriver_Device"
) | ForEach-Object { [void]$excludedServices.Add($_) }

$targetServices = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($name in $knownServices) {
    [void]$targetServices.Add($name)
}

foreach ($svc in Get-Service) {
    if ($excludedServices.Contains($svc.Name)) { continue }
    if ($svc.Name -like "Acer*" -or
        $svc.Name -like "Predator*" -or
        $svc.Name -like "AICO*" -or
        $svc.DisplayName -like "*Acer*" -or
        $svc.DisplayName -like "*Predator*") {
        [void]$targetServices.Add($svc.Name)
    }
}

foreach ($serviceName in $targetServices) {
    $svc = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if (-not $svc) { continue }

    Write-Host "Stopping and disabling $serviceName..."
    try {
        if ($svc.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
            Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
        }
        Set-Service -Name $serviceName -StartupType Disabled -ErrorAction Stop
    } catch {
        Write-Warning "Failed to disable ${serviceName}: $($_.Exception.Message)"
    }
}

$extraProcesses = @(
    "PredatorSense",
    "AcerService",
    "AcerQuickAccess",
    "AcerAgentService",
    "AcerCentralService",
    "AcerHardwareService",
    "AcerQAAgent",
    "AcerCCAgent",
    "AcerDIAgent",
    "ADESv2Svc",
    "AcerServiceWrapper",
    "AcerSystemCentralService",
    "AICO_svc",
    "AICO2_svc"
)

foreach ($procName in ($extraProcesses + $targetServices | Select-Object -Unique)) {
    if ($procName -ieq "PreySense") { continue }
    Get-Process -Name $procName -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "Stopping process $($_.ProcessName) (PID $($_.Id))..."
        Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "Done. Acer/PredatorSense OEM services are stopped and disabled."
