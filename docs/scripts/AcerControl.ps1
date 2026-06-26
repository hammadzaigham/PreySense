Param (
    [ValidateSet("Quiet", "Balanced", "Performance", "Turbo", "Eco")]
    [string]$Profile = "",

    [ValidateRange(0, 100)]
    [int]$CpuFan = -1,

    [ValidateRange(0, 100)]
    [int]$GpuFan = -1,

    [Switch]$FanAuto,
    [Switch]$FanMax,

    [ValidateSet("Optimized", "Full")]
    [string]$BatteryMode = "",

    [ValidateSet("Enabled", "Disabled")]
    [string]$BootAnimation = "",

    [ValidateSet("Enabled", "Disabled")]
    [string]$LedTimeout = "",

    [ValidateSet(0, 10, 20, 30)]
    [int]$UsbCharging = -1,

    [ValidateSet("Integrated", "Optimus", "Discrete")]
    [string]$GpuMode = "",

    [string]$Password = "",

    [Switch]$WatchRPM,

    [string]$RgbValues = "",
    [Switch]$GetRgb,
    [Switch]$QuietRgb
)

# Run as administrator
if (!([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")) { 
    $arguments = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", "$PSCommandPath $($MyInvocation.Line.Replace($MyInvocation.InvocationName, '').Trim())")
    Start-Process -FilePath powershell.exe -ArgumentList $arguments -Verb RunAs; exit 
}

# WMI Class Connections
$gaming = Get-WmiObject -Namespace root\wmi -Class AcerGamingFunction -ErrorAction SilentlyContinue
$apge = Get-WmiObject -Namespace root\wmi -Class APGeAction -ErrorAction SilentlyContinue
$battery = Get-WmiObject -Namespace root\wmi -Class BatteryControl -ErrorAction SilentlyContinue
$biosTool = Get-CimInstance -Namespace root\wmi -ClassName AcerBiosConfigurationTool -ErrorAction SilentlyContinue

# Helper Functions
function Set-MiscSetting ($setting, $value) {
    if ($gaming) {
        $gaming.SetGamingMiscSetting($setting -bor ($value -shl 8)) | Out-Null
    }
}

function Get-WmiMethodInputName($wmiObject, [string]$methodName) {
    $params = $wmiObject.GetMethodParameters($methodName)
    foreach ($prop in $params.Properties) {
        $name = $prop.Name
        if ($name -like '__*') { continue }
        if ($name -in @('ReturnValue', 'RETURNVALUE')) { continue }
        return $name
    }
    return 'gmInput'
}

function Get-CimMethodInputName([string]$methodName) {
    $method = (Get-CimClass -Namespace root/wmi -ClassName AcerGamingFunction).CimClassMethods[$methodName]
    foreach ($key in $method.Parameters.Keys) {
        if ($key -like '__*') { continue }
        return $key
    }
    return 'gmInput'
}

function New-UInt16BufferFromBytes([byte[]]$byteBuffer) {
    $ushortBuffer = New-Object UInt16[] $byteBuffer.Length
    for ($i = 0; $i -lt $byteBuffer.Length; $i++) {
        $ushortBuffer[$i] = [UInt16]$byteBuffer[$i]
    }
    return $ushortBuffer
}

function New-BacklightBufferFromProfile([int]$backlightCount, $profileValues) {
    $buffer = New-Object byte[] $backlightCount
    for ($i = 0; $i -lt $backlightCount; $i++) {
        $buffer[$i] = [byte]$profileValues[$i]
    }
    return $buffer
}

function Get-RgbZoneStartIndex([int]$backlightCount, $profileValues, [int]$valsCount) {
    # Profiles exported with 16 header bytes include a separator 0 at index backlightCount on 15-byte machines
    if ($valsCount -gt $backlightCount -and [uint64]$profileValues[$backlightCount] -eq 0) {
        return $backlightCount + 1
    }
    return $backlightCount
}

function Test-RgbCimSuccess($result) {
    if ($null -eq $result) { return $false }
    $rv = $result.ReturnValue
    if ($rv -eq 0 -or $rv -eq $true) { return $true }
    if ($null -ne $result.gmOutput) {
        $raw = [uint64]$result.gmOutput
        if (($raw -band 0xFF) -eq 0) { return $true }
    }
    return $false
}

function Test-RgbWmiOutSuccess($out) {
    if ($null -eq $out) { return $false }
    try {
        $rv = $out['ReturnValue']
        if ($rv -eq 0 -or $rv -eq $true) { return $true }
    } catch {}
    try {
        $raw = [uint64]$out['gmOutput']
        if (($raw -band 0xFF) -eq 0) { return $true }
    } catch {}
    return $false
}

function Set-GamingKbBacklightBuffer([byte[]]$byteBuffer, $wmiInstance) {
    $methodName = 'SetGamingKBBacklight'
    $errors = [System.Collections.Generic.List[string]]::new()

    # Try 16-byte padded buffer first (often required even when Get returns 15 bytes)
    $payloads = New-Object System.Collections.Generic.List[object]
    if ($byteBuffer.Length -eq 15) {
        $padded = New-Object byte[] 16
        [Array]::Copy($byteBuffer, $padded, 15)
        [void]$payloads.Add($padded)
    }
    [void]$payloads.Add($byteBuffer)

    try {
        $cim = Get-CimInstance -Namespace root/wmi -ClassName AcerGamingFunction
        foreach ($payload in $payloads) {
            try {
                $result = Invoke-CimMethod -InputObject $cim -MethodName $methodName -Arguments @{ gmInput = $payload } -ErrorAction Stop
                if (Test-RgbCimSuccess $result) {
                    return [PSCustomObject]@{ ReturnValue = 0; Method = "cim-$($payload.Length)b"; gmOutput = $result.gmOutput }
                }
                [void]$errors.Add("cim-$($payload.Length)b: ReturnValue=$($result.ReturnValue) gmOutput=$($result.gmOutput)")
            } catch {
                [void]$errors.Add("cim-$($payload.Length)b: $($_.Exception.Message)")
            }
        }
    } catch {
        [void]$errors.Add("cim-init: $($_.Exception.Message)")
    }

    foreach ($payload in $payloads) {
        try {
            $inParams = $wmiInstance.GetMethodParameters($methodName)
            $inParams['gmInput'] = $payload
            $out = $wmiInstance.InvokeMethod($methodName, $inParams, $null)
            if (Test-RgbWmiOutSuccess $out) {
                return [PSCustomObject]@{ ReturnValue = 0; Method = "instance-$($payload.Length)b"; gmOutput = $out['gmOutput'] }
            }
            [void]$errors.Add("instance-$($payload.Length)b: ReturnValue=$($out['ReturnValue']) gmOutput=$($out['gmOutput'])")
        } catch {
            [void]$errors.Add("instance-$($payload.Length)b: $($_.Exception.Message)")
        }
    }

    throw "SetGamingKBBacklight failed: $($errors -join '; ')"
}

function Set-FanSpeed ($fanIndex, $speedPercent) {
    if ($gaming) {
        $fanSpeedValue = ((($speedPercent * 25600) / 100) -band 0xFF00) + $fanIndex
        $gaming.SetGamingFanSpeed($fanSpeedValue) | Out-Null
    }
}

# --- Action 1: Set Operating Profile ---
if ($Profile -ne "") {
    $modeVal = switch ($Profile.ToLower()) {
        "quiet"       { 0x00 }
        "balanced"    { 0x01 }
        "performance" { 0x04 }
        "turbo"       { 0x05 }
        "eco"         { 0x06 }
    }
    Write-Output "Setting operating profile to: $Profile"
    Set-MiscSetting 0x000B $modeVal
}

# --- Action 2: Set Fan Speeds & Behavior ---
if ($FanAuto) {
    if ($gaming) {
        $gaming.SetGamingFanBehavior(0x410009) | Out-Null
        Write-Output "Setting fans to Automatic control."
    }
}
elseif ($FanMax) {
    if ($gaming) {
        $gaming.SetGamingFanBehavior(0x820009) | Out-Null
        Write-Output "Setting fans to Maximum speed."
    }
}
elseif ($CpuFan -ge 0 -or $GpuFan -ge 0) {
    if ($gaming) {
        $gaming.SetGamingFanBehavior(0xC30009) | Out-Null
        if ($CpuFan -ge 0) {
            Write-Output "Setting CPU fan speed to: $CpuFan%"
            Set-FanSpeed 1 $CpuFan
        }
        if ($GpuFan -ge 0) {
            Write-Output "Setting GPU fan speed to: $GpuFan%"
            Set-FanSpeed 4 $GpuFan
        }
    }
}

# --- Action 3: Set Battery Conservation Limit ---
if ($BatteryMode -ne "") {
    if ($battery) {
        $val = if ($BatteryMode -eq "Optimized") { 1 } else { 0 }
        Write-Output "Setting Battery Health limit to: $BatteryMode (Limit: $(if($val -eq 1){'80%'}else{'100%'}))"
        $battery.SetBatteryHealthControl(1, 1, $val, @(0,0,0,0,0)) | Out-Null
    } else {
        Write-Warning "BatteryControl WMI class is not available."
    }
}

# --- Action 4: Toggle Boot Animation / Startup Sound ---
if ($BootAnimation -ne "") {
    if ($gaming) {
        $val = if ($BootAnimation -eq "Enabled") { 0x106 } else { 0x6 }
        Write-Output "Setting Boot Animation to: $BootAnimation"
        $gaming.SetGamingMiscSetting($val) | Out-Null
    }
}

# --- Action 5: Toggle Keyboard LED sleep timer ---
if ($LedTimeout -ne "") {
    if ($apge) {
        $val = if ($LedTimeout -eq "Enabled") { 0x1E0000088402 } else { 0x88402 }
        Write-Output "Setting Keyboard Backlight 30s Timeout to: $LedTimeout"
        $apge.SetFunction($val) | Out-Null
    } else {
        Write-Warning "APGeAction WMI class is not available."
    }
}

# --- Action 6: Set Power-Off USB Charging battery limit ---
if ($UsbCharging -ge 0) {
    if ($apge) {
        $val = switch ($UsbCharging) {
            0  { 663300 }
            10 { 659204 }
            20 { 1314564 }
            30 { 1969924 }
        }
        Write-Output "Setting Power-Off USB Charging Limit to: $(if($UsbCharging -eq 0){'Disabled'}else{$UsbCharging.ToString() + '%'})"
        $apge.SetFunction($val) | Out-Null
    } else {
        Write-Warning "APGeAction WMI class is not available."
    }
}

# --- Action 7: Set GPU MUX Switch (Display Mode) ---
if ($GpuMode -ne "") {
    if ($biosTool) {
        $val = switch ($GpuMode.ToLower()) {
            "integrated" { 1 }
            "optimus"    { 2 }
            "discrete"   { 3 }
        }
        
        $pwLength = if ($Password.Length -gt 15) { 15 } else {$Password.Length}
        $pwBytes = [byte[]]::new(128)
        if ($pwLength -gt 0) {
            $pwBytesOrig = [System.Text.Encoding]::ASCII.GetBytes($Password)
            [Array]::Copy($pwBytesOrig, $pwBytes, $pwLength)
        }
        
        Write-Output "Retrieving current BIOS options..."
        $resGet = Invoke-CimMethod -InputObject $biosTool -MethodName GetBiosOptions -Arguments @{
            Password = $pwBytes
            PasswordLen = [UInt16]$pwLength
        } -ErrorAction SilentlyContinue
        
        if ($resGet -and $resGet.ReturnCode -eq 0 -and $null -ne $resGet.Data) {
            $data = $resGet.Data
            $data[80] = $val
            
            Write-Output "Writing new GPU Mode ($GpuMode) to WMI BIOS Offset 80..."
            $resSet = Invoke-CimMethod -InputObject $biosTool -MethodName SetBiosOptions -Arguments @{
                Password = $pwBytes
                PasswordLen = [UInt16]$pwLength
                Data = $data
            } -ErrorAction SilentlyContinue
            
            if ($resSet -and ($resSet.ReturnCode -eq 0 -or $resSet.ReturnCode -eq 8)) {
                Write-Host "[+] GPU Mode set successfully! Please REBOOT your computer to apply the change." -ForegroundColor Green
            } else {
                Write-Error "Failed to write BIOS options. ReturnCode: $($resSet.ReturnCode)"
            }
        } else {
            Write-Error "Failed to retrieve BIOS options. Verify your supervisor password is correct (current: '$Password')."
        }
    } else {
        Write-Warning "AcerBiosConfigurationTool WMI class is not available."
    }
}

# --- Action 8: Get Real-Time RPM values ---
if ($GetRPM) {
    if ($gaming) {
        while ($true) {
            $cpuRpm = $gaming.GetGamingSysInfo(513).gmOutput / 256
            $gpuRpm = $gaming.GetGamingSysInfo(1537).gmOutput / 256
            Write-Output "CPU Fan Speed: $cpuRpm RPM | GPU Fan Speed: $gpuRpm RPM"
            if (!$WatchRPM) { break }
            Start-Sleep 1
        }
    } else {
        Write-Error "AcerGamingFunction WMI class is not available."
    }
    exit
}

# --- Action 10: Set Keyboard RGB Profile / Values ---
if ($RgbValues -ne "") {
    if ($gaming) {
        # Zone colors are uint32 (e.g. 4294967040) and exceed Int32.MaxValue
        $valArray = $RgbValues.Split(",") | ForEach-Object { [uint64]$_.Trim() }
        $valsCount = $valArray.Count
        if ($valsCount -gt 0) {
            Write-Output "Applying custom Keyboard RGB profile values..."
            $kbBacklight = $gaming.GetGamingKBBacklight(1).gmOutput
            $kbBacklightCount = $kbBacklight.Count
            Write-Output "Hardware backlight buffer length: $kbBacklightCount bytes"

            $zoneStart = Get-RgbZoneStartIndex $kbBacklightCount $valArray $valsCount
            if ($valsCount -lt ($zoneStart + 6)) {
                Write-Error "Invalid RGB profile values count. Expected at least $($zoneStart + 6) elements, got $valsCount (backlight=$kbBacklightCount, zoneStart=$zoneStart)."
            }
            else {
                Write-Warning "WMI backlight buffer apply is legacy. For lighting modes use PreySense or docs/acer_service_rgb.md (AcerService TCP)."
                try {
                    $backlightBuffer = New-Object byte[] 16
                    for ($i = 0; $i -lt 16; $i++) { $backlightBuffer[$i] = [byte]$valArray[$i] }
                    $r = Set-GamingKbBacklightBuffer $backlightBuffer $gaming
                    Write-Output "SetGamingKBBacklight via $($r.Method)"
                    Write-Output "[+] Backlight buffer sent (may not change dynamic effects without AcerService)."
                } catch {
                    Write-Error "SetGamingKBBacklight failed: $($_.Exception.Message)"
                }
            }
        }
    } else {
        Write-Warning "AcerGamingFunction WMI class is not available."
    }
    exit
}

# --- Action 11: Get Keyboard RGB Profile / Values ---
if ($GetRgb) {
    if ($gaming) {
        $kbBacklight = $gaming.GetGamingKBBacklight(1).gmOutput
        $joined = $kbBacklight -join ","
        $joined = $($joined,
            0,
            $gaming.GetGamingRgbKb(1).gmOutput, 
            $gaming.GetGamingRgbKb(2).gmOutput, 
            $gaming.GetGamingRgbKb(4).gmOutput, 
            $gaming.GetGamingRgbKb(8).gmOutput,
            $gaming.GetGamingLEDBehavior(1).gmOutput,
            $gaming.GetGamingLEDColor(1).gmOutput) -join ","
            
        if (!$QuietRgb) {
            Write-Output "Current Keyboard/Backlight RGB Profile String:"
        }
        Write-Output "$joined"
    } else {
        Write-Error "AcerGamingFunction WMI class is not available."
    }
    exit
}

# --- Action 9: Get Current Settings Overview ---
if ($GetSettings -or ($Profile -eq "" -and !$FanAuto -and !$FanMax -and $CpuFan -lt 0 -and $GpuFan -lt 0 -and $BatteryMode -eq "" -and $BootAnimation -eq "" -and $LedTimeout -eq "" -and $UsbCharging -lt 0 -and $GpuMode -eq "")) {
    Write-Output "========================================"
    Write-Output "       ACER PREDATOR SYSTEM STATUS      "
    Write-Output "========================================"
    
    if ($gaming) {
        # Profile
        $profileStatus = ($gaming.GetGamingMiscSetting(0x000B).gmOutput -shr 8) -band 0xFF
        $currentProfile = switch ($profileStatus) {
            0x00 { "Quiet" }
            0x01 { "Balanced" }
            0x04 { "Performance" }
            0x05 { "Turbo" }
            0x06 { "Eco" }
            default { "Unknown ($profileStatus)" }
        }
        Write-Output "Operating Profile    : $currentProfile"

        # Fan Speeds
        $cpuFanSpeed = $gaming.GetGamingFanSpeed(1).gmOutput / 256
        $gpuFanSpeed = $gaming.GetGamingFanSpeed(4).gmOutput / 256
        Write-Output "CPU Fan Speed Target : $(if($cpuFanSpeed -eq 0){'Auto'}else{$cpuFanSpeed.ToString() + '%'})"
        Write-Output "GPU Fan Speed Target : $(if($gpuFanSpeed -eq 0){'Auto'}else{$gpuFanSpeed.ToString() + '%'})"

        # Fan RPMs
        $cpuRpm = $gaming.GetGamingSysInfo(513).gmOutput / 256
        $gpuRpm = $gaming.GetGamingSysInfo(1537).gmOutput / 256
        Write-Output "CPU Fan Active Speed : $cpuRpm RPM"
        Write-Output "GPU Fan Active Speed : $gpuRpm RPM"

        # Boot Animation
        $bootStatus = $gaming.GetGamingMiscSetting(0x06).gmOutput
        $bootAnim = if ($bootStatus -eq 0x100) { "Enabled" } elseif ($bootStatus -eq 0x0) { "Disabled" } else { "Unknown (0x" + $bootStatus.ToString('X') + ")" }
        Write-Output "Boot Animation/Sound : $bootAnim"
    } else {
        Write-Warning "AcerGamingFunction WMI class is not available."
    }

    if ($apge) {
        # LED Backlight Sleep Timeout
        $timeoutStatus = $apge.GetFunction(0x88401).uiOutput
        $ledTime = if ($timeoutStatus -eq 0x1E0000080000) { "Enabled (30 seconds)" } elseif ($timeoutStatus -eq 0x80000) { "Disabled (Always On)" } else { "Unknown (0x" + $timeoutStatus.ToString('X') + ")" }
        Write-Output "Backlight Sleep 30s  : $ledTime"

        # Power-Off USB Charging
        $usbStatus = $apge.GetFunction(0x4).uiOutput
        $usbCharge = if ($usbStatus -band 4096) { "Disabled" } else { "Enabled above " + ((($usbStatus -shr 17) -band 0x6F) * 2).ToString() + "% battery" }
        Write-Output "Power-Off USB Charge : $usbCharge"
    } else {
        Write-Warning "APGeAction WMI class is not available."
    }

    if ($battery) {
        # Battery Health mode
        $batStatus = $battery.GetBatteryHealthControlStatus(1, 1, @(0,0)).uFunctionStatus
        $batLimit = if ($batStatus[0] -eq 0) { "Full (100% Limit)" } elseif ($batStatus[0] -eq 1) { "Optimized (80% Limit)" } elseif ($batStatus[0] -eq 2) { "Calibration Mode" } else { "Unknown" }
        Write-Output "Battery Charge Limit : $batLimit"
    } else {
        Write-Warning "BatteryControl WMI class is not available."
    }
    
    if ($biosTool) {
        # GPU Mode (MUX Switch) from WMI Offset 80
        $pwLength = if ($Password.Length -gt 15) { 15 } else {$Password.Length}
        $pwBytes = [byte[]]::new(128)
        if ($pwLength -gt 0) {
            $pwBytesOrig = [System.Text.Encoding]::ASCII.GetBytes($Password)
            [Array]::Copy($pwBytesOrig, $pwBytes, $pwLength)
        }
        $resGet = Invoke-CimMethod -InputObject $biosTool -MethodName GetBiosOptions -Arguments @{
            Password = $pwBytes
            PasswordLen = [UInt16]$pwLength
        } -ErrorAction SilentlyContinue
        if ($resGet -and $resGet.ReturnCode -eq 0 -and $null -ne $resGet.Data) {
            $gpuModeVal = $resGet.Data[80]
            $gpuModeStr = switch ($gpuModeVal) {
                1 { "Integrated GPU Only (Eco)" }
                2 { "MSHybrid (Optimus)" }
                3 { "Discrete GPU Only (NVIDIA)" }
                default { "Unknown ($gpuModeVal)" }
            }
            Write-Output "GPU MUX Switch Mode  : $gpuModeStr"
        } else {
            Write-Output "GPU MUX Switch Mode  : Unknown (Auth Required / Wrong Password)"
        }
    } else {
        Write-Warning "AcerBiosConfigurationTool WMI class is not available."
    }
    
    Write-Output "========================================"
}
