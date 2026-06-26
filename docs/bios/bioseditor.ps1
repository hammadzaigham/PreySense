Param (
    [Parameter(Mandatory=$false)]
    [String]$Setting,

    [Parameter(Mandatory=$false)]
    [String]$Value,

    [Parameter(Mandatory=$false)]
    [Switch]$List,

    [Parameter(Mandatory=$false)]
    [Switch]$Get,

    [Parameter(Mandatory=$false)]
    [String]$Password = ""
)

# Reconstruct arguments array
$passedArgs = @()
foreach ($key in $PSBoundParameters.Keys) {
    $passedArgs += "-$key"
    $passedArgs += $PSBoundParameters[$key]
}
foreach ($arg in $args) {
    $passedArgs += $arg
}

# Admin elevation check & auto-elevation
if (!([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")) {
    $argList = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-NoExit", "-File", $PSCommandPath)
    $argList += $passedArgs
    Start-Process -FilePath powershell.exe -ArgumentList $argList -Verb RunAs -WorkingDirectory (Get-Location).Path
    exit 0
}


# Resolve JSON Map Path relative to this script
$mapPath = Join-Path (Split-Path $PSScriptRoot -Parent) "AcerWmiMap.json"
if (!(Test-Path $mapPath)) {
    Write-Error "WMI mapping catalogue not found at: $mapPath"
    exit 1
}

# Load Catalogue
try {
    $rawJson = Get-Content -Path $mapPath -Raw -Encoding utf8
    $catalogue = ConvertFrom-Json $rawJson
} catch {
    Write-Error "Failed to load WMI mapping JSON: $_"
    exit 1
}

# Helper to authenticate and fetch WMI bios tool
function Get-WmiBiosTool {
    $biosTool = $null
    $utilFunc = $null
    
    try {
        $biosTool = Get-CimInstance -Namespace root\wmi -ClassName AcerBiosConfigurationTool -ErrorAction SilentlyContinue
        $utilFunc = Get-CimInstance -Namespace root\wmi -ClassName UtilityFunction -ErrorAction SilentlyContinue
    } catch {}

    if ($null -eq $biosTool) {
        try {
            $biosTool = Get-WmiObject -Namespace root\wmi -Class AcerBiosConfigurationTool -ErrorAction SilentlyContinue
            $utilFunc = Get-WmiObject -Namespace root\wmi -Class UtilityFunction -ErrorAction SilentlyContinue
        } catch {}
    }

    if ($null -eq $biosTool) {
        Write-Error "AcerBiosConfigurationTool WMI class not found."
        exit 1
    }

    # Setup Password Bytes
    $pwLength = if ($Password.Length -gt 15) { 15 } else { $Password.Length }
    $pwBuffer = [byte[]]::new(128)
    if ($pwLength -gt 0) {
        $asciiBytes = [System.Text.Encoding]::ASCII.GetBytes($Password)
        [Array]::Copy($asciiBytes, $pwBuffer, $pwLength)
    }
    $emptyBuffer = [byte[]]::new(128)

    # Perform Authentication using UtilityFunction
    if ($utilFunc) {
        try {
            if ($utilFunc.CimClass) {
                # Check USER empty password
                $null = Invoke-CimMethod -InputObject $utilFunc -MethodName CheckPassword -Arguments @{
                    Command = "USER"
                    Password = $emptyBuffer
                    PasswordLen = [UInt16]0
                } -ErrorAction SilentlyContinue
                
                # Check ADMIN supervisor password
                $resAdmin = Invoke-CimMethod -InputObject $utilFunc -MethodName CheckPassword -Arguments @{
                    Command = "ADMIN"
                    Password = $pwBuffer
                    PasswordLen = [UInt16]$pwLength
                } -ErrorAction Stop
                
                if ($resAdmin.ReturnCode -ne 0) {
                    Write-Error "BIOS Authentication failed (ReturnCode: $($resAdmin.ReturnCode))."
                    exit 1
                }
            } else {
                $inParamsUser = $utilFunc.GetMethodParameters("CheckPassword")
                $inParamsUser["Command"] = "USER"
                $inParamsUser["Password"] = $emptyBuffer
                $inParamsUser["PasswordLen"] = [UInt16]0
                $null = $utilFunc.InvokeMethod("CheckPassword", $inParamsUser, [System.Management.InvokeMethodOptions]$null)

                $inParamsAdmin = $utilFunc.GetMethodParameters("CheckPassword")
                $inParamsAdmin["Command"] = "ADMIN"
                $inParamsAdmin["Password"] = $pwBuffer
                $inParamsAdmin["PasswordLen"] = [UInt16]$pwLength
                $resAdmin = $utilFunc.InvokeMethod("CheckPassword", $inParamsAdmin, [System.Management.InvokeMethodOptions]$null)

                if ($resAdmin -and $resAdmin.ReturnCode -ne 0) {
                    Write-Error "BIOS Authentication failed (ReturnCode: $($resAdmin.ReturnCode))."
                    exit 1
                }
            }
        } catch {
            Write-Warning "UtilityFunction authentication failed or skipped: $_"
        }
    }

    return [PSCustomObject]@{
        WmiObject = $biosTool
        PasswordBuffer = $pwBuffer
        PasswordLength = $pwLength
    }
}

# Fetch raw options buffer from BIOS
function Get-RawBiosData($session) {
    $tool = $session.WmiObject
    $pwBuf = $session.PasswordBuffer
    $pwLen = $session.PasswordLength

    $res = $null
    try {
        if ($tool.CimClass) {
            $res = Invoke-CimMethod -InputObject $tool -MethodName GetBiosOptions -Arguments @{
                Password = $pwBuf
                PasswordLen = [UInt16]$pwLen
            } -ErrorAction Stop
        } else {
            $inParams = $tool.GetMethodParameters("GetBiosOptions")
            $inParams["PasswordLen"] = [UInt16]$pwLen
            $inParams["Password"] = $pwBuf
            $res = $tool.InvokeMethod("GetBiosOptions", $inParams, [System.Management.InvokeMethodOptions]$null)
        }
    } catch {
        Write-Error "Failed to retrieve BIOS options buffer: $_"
        exit 1
    }

    if ($null -eq $res -or $res.ReturnCode -ne 0 -or $null -eq $res.Data) {
        $err = if ($res) { $res.ReturnCode } else { "Unknown" }
        Write-Error "Failed to retrieve BIOS options data. ReturnCode: $err. Check your supervisor password."
        exit 1
    }

    return $res.Data
}

# Write raw options buffer back to BIOS
function Set-RawBiosData($session, $data) {
    $tool = $session.WmiObject
    $pwBuf = $session.PasswordBuffer
    $pwLen = $session.PasswordLength

    $res = $null
    try {
        if ($tool.CimClass) {
            $res = Invoke-CimMethod -InputObject $tool -MethodName SetBiosOptions -Arguments @{
                Password = $pwBuf
                PasswordLen = [UInt16]$pwLen
                Data = $data
            } -ErrorAction Stop
        } else {
            $inParams = $tool.GetMethodParameters("SetBiosOptions")
            $inParams["PasswordLen"] = [UInt16]$pwLen
            $inParams["Password"] = $pwBuf
            $inParams["Data"] = $data
            $res = $tool.InvokeMethod("SetBiosOptions", $inParams, [System.Management.InvokeMethodOptions]$null)
        }
    } catch {
        Write-Error "Failed to write BIOS options buffer: $_"
        exit 1
    }

    if ($null -eq $res -or ($res.ReturnCode -ne 0 -and $res.ReturnCode -ne 8)) {
        $err = if ($res) { $res.ReturnCode } else { "Unknown" }
        Write-Error "Failed to commit BIOS options. ReturnCode: $err"
        exit 1
    }

    return $true
}

# Find setting entry in catalogue
function Find-SettingEntry([string]$query) {
    if ([string]::IsNullOrEmpty($query)) { return $null }

    # Try matching by decimal offset
    if ($query -match '^\d+$') {
        $dec = [int]$query
        return $catalogue | Where-Object { $_.wmiOffsetDec -eq $dec } | Select-Object -First 1
    }

    # Try matching by hex offset
    if ($query -match '^0[xX][0-9a-fA-F]+$') {
        $dec = [System.Convert]::ToInt32($query, 16)
        return $catalogue | Where-Object { $_.wmiOffsetDec -eq $dec } | Select-Object -First 1
    }

    # Exact case-insensitive match on settingName
    $entry = $catalogue | Where-Object { $_.settingName -ieq $query } | Select-Object -First 1
    if ($entry) { return $entry }

    # Fuzzy match on settingName
    $entry = $catalogue | Where-Object { $_.settingName -ilike "*$query*" } | Select-Object -First 1
    return $entry
}

# Map WMI byte/word value to text option
function Get-ValueText($entry, $val) {
    if (!$entry.options -or $entry.options.Count -eq 0) {
        return "Raw value: $val (0x$($val.ToString('X2')))"
    }
    foreach ($opt in $entry.options) {
        $optVal = [System.Convert]::ToInt32($opt.Value, 16)
        if ($optVal -eq $val) {
            return "$($opt.Text) ($($opt.Value))"
        }
    }
    return "Unknown option: $val (0x$($val.ToString('X2')))"
}

# Parse user input value into the target integer/byte
function Parse-InputValue($entry, $valStr) {
    # Check if direct matching options exist
    if ($entry.options -and $entry.options.Count -gt 0) {
        # Match case-insensitive exact text
        foreach ($opt in $entry.options) {
            if ($opt.Text -ieq $valStr) {
                return [System.Convert]::ToInt32($opt.Value, 16)
            }
        }

        # Match fuzzy text
        foreach ($opt in $entry.options) {
            if ($opt.Text -ilike "*$valStr*") {
                return [System.Convert]::ToInt32($opt.Value, 16)
            }
        }

        # Normalize common aliases
        $aliases = @{
            "off" = "Disabled"; "on" = "Enabled";
            "0" = "Disabled"; "1" = "Enabled";
            "false" = "Disabled"; "true" = "Enabled"
        }
        if ($aliases.ContainsKey($valStr.ToLower())) {
            $mapped = $aliases[$valStr.ToLower()]
            foreach ($opt in $entry.options) {
                if ($opt.Text -ieq $mapped) {
                    return [System.Convert]::ToInt32($opt.Value, 16)
                }
            }
        }

        # Try to match by direct hex or integer value in the option
        foreach ($opt in $entry.options) {
            # Try parsing opt.Value (e.g. "0x1") and comparing to valStr
            try {
                $parsedOptVal = [System.Convert]::ToInt32($opt.Value, 16)
                if ($valStr -match '^0[xX][0-9a-fA-F]+$') {
                    if ($parsedOptVal -eq [System.Convert]::ToInt32($valStr, 16)) { return $parsedOptVal }
                } elseif ($valStr -match '^\d+$') {
                    if ($parsedOptVal -eq [int]$valStr) { return $parsedOptVal }
                }
            } catch {}
        }
    }

    # If it is a numeric index, try to parse it directly
    if ($valStr -match '^0[xX][0-9a-fA-F]+$') {
        return [System.Convert]::ToInt32($valStr, 16)
    }
    if ($valStr -match '^\d+$') {
        return [int]$valStr
    }

    return $null
}

# --- Action Executions ---

# Helper functions for executions
function Show-AllSettings {
    Write-Host "Fetching WMI BIOS settings... (Total active entries: $($catalogue.Count))" -ForegroundColor Cyan
    $tableData = @()
    foreach ($entry in $catalogue) {
        $offset = $entry.wmiOffsetDec
        if ($offset -lt $script:data.Length) {
            # Handle size
            $val = 0
            if ($entry.size -eq 1) {
                $val = $script:data[$offset]
            } elseif ($entry.size -eq 2) {
                $val = [System.BitConverter]::ToUInt16($script:data, $offset)
            } else {
                # Multi-byte setting (buffers / strings)
                $val = -1
            }

            $currentValStr = if ($val -eq -1) {
                "Buffer/String length $($entry.size) bytes"
            } else {
                Get-ValueText $entry $val
            }

            $tableData += [PSCustomObject]@{
                "WMI Offset" = "0x$($offset.ToString('X4')) ($offset)"
                "Setting Name" = $entry.settingName
                "Current Value" = $currentValStr
                "VarStore" = $entry.varStore
                "Type" = $entry.type
            }
        }
    }
    $tableData | Format-Table -AutoSize
}

function Show-SettingDetails($entry) {
    $offset = $entry.wmiOffsetDec
    $val = 0
    if ($entry.size -eq 1) {
        $val = $script:data[$offset]
    } elseif ($entry.size -eq 2) {
        $val = [System.BitConverter]::ToUInt16($script:data, $offset)
    } else {
        $val = -1
    }

    $currentValStr = if ($val -eq -1) {
        "Buffer/String of $($entry.size) bytes"
    } else {
        Get-ValueText $entry $val
    }

    Write-Host "--- BIOS Setting Details ---" -ForegroundColor Cyan
    Write-Host "Name:          $($entry.settingName)"
    Write-Host "WMI Offset:    0x$($offset.ToString('X4')) ($offset)"
    Write-Host "UEFI Offset:   $($entry.uefiOffsetHex) in $($entry.varStore)"
    Write-Host "Size (Bytes):  $($entry.size)"
    Write-Host "Mapping Type:  $($entry.type)"
    Write-Host "Current State: $currentValStr" -ForegroundColor Green
    
    if ($entry.options -and $entry.options.Count -gt 0) {
        Write-Host "`nAvailable Options:" -ForegroundColor Yellow
        foreach ($opt in $entry.options) {
            Write-Host "  - $($opt.Text) ($($opt.Value))"
        }
    } else {
        Write-Host "`nNumeric or dynamic buffer setting (no static text options)." -ForegroundColor Yellow
    }
}

function Set-SettingValue($entry, $valStr) {
    $parsedVal = Parse-InputValue $entry $valStr
    if ($null -eq $parsedVal) {
        Write-Error "Invalid value '$valStr' for setting '$($entry.settingName)'."
        if ($entry.options -and $entry.options.Count -gt 0) {
            Write-Error "Allowed option texts are: $(($entry.options.Text) -join ', ')"
        }
        return $false
    }

    $offset = $entry.wmiOffsetDec
    
    # Save current value for diff logging
    $oldVal = 0
    if ($entry.size -eq 1) {
        $oldVal = $script:data[$offset]
        $script:data[$offset] = [byte]$parsedVal
    } elseif ($entry.size -eq 2) {
        $oldVal = [System.BitConverter]::ToUInt16($script:data, $offset)
        $bytes = [System.BitConverter]::GetBytes([UInt16]$parsedVal)
        $script:data[$offset] = $bytes[0]
        $script:data[$offset + 1] = $bytes[1]
    } else {
        Write-Error "Writing directly to multi-byte buffers is not supported via this interface."
        return $false
    }

    Write-Host "Modifying '$($entry.settingName)' (Offset 0x$($offset.ToString('X4'))):" -ForegroundColor Cyan
    $oldText = if ($entry.options) { Get-ValueText $entry $oldVal } else { $oldVal }
    $newText = if ($entry.options) { Get-ValueText $entry $parsedVal } else { $parsedVal }
    Write-Host "  Old: $oldText" -ForegroundColor Red
    Write-Host "  New: $newText" -ForegroundColor Green

    # Commit changes
    if (Set-RawBiosData $script:session $script:data) {
        Write-Host "`nSuccess: BIOS configuration written successfully!" -ForegroundColor Green
        Write-Host "Please REBOOT your computer to apply the changes." -ForegroundColor Yellow
        
        # Refresh the data cache from the BIOS
        $script:data = Get-RawBiosData $script:session
        return $true
    }
    return $false
}

# Initialize Session
$script:session = Get-WmiBiosTool
$script:data = Get-RawBiosData $script:session

# Determine if we should run in interactive mode or command line mode
$isInteractive = $true
if ($List -or $Get -or ![string]::IsNullOrEmpty($Setting) -or ![string]::IsNullOrEmpty($Value)) {
    $isInteractive = $false
}

if ($isInteractive) {
    Write-Host "==================================================" -ForegroundColor Cyan
    Write-Host "       Acer BIOS WMI Configuration CLI            " -ForegroundColor Cyan
    Write-Host "==================================================" -ForegroundColor Cyan
    Write-Host "Interactive Mode. Type 'exit' or 'quit' to exit." -ForegroundColor Yellow
    
    while ($true) {
        Write-Host "`nSelect an action:" -ForegroundColor Cyan
        Write-Host "  1) List all settings and current values"
        Write-Host "  2) Get details / options for a specific setting"
        Write-Host "  3) Change a setting's value"
        Write-Host "  4) Exit / Quit"
        
        $choice = (Read-Host "Enter option (1-4, 'exit', or 'quit')").Trim()
        
        if ($choice -eq "4" -or $choice -eq "exit" -or $choice -eq "quit" -or $choice -eq "q") {
            Write-Host "Exiting. Goodbye!" -ForegroundColor Green
            break
        }
        
        switch ($choice) {
            "1" {
                Show-AllSettings
            }
            "2" {
                $query = (Read-Host "Enter setting name or WMI offset (hex/dec)").Trim()
                if ([string]::IsNullOrEmpty($query)) {
                    Write-Warning "Setting name/offset cannot be empty."
                    continue
                }
                $entry = Find-SettingEntry $query
                if ($null -eq $entry) {
                    Write-Error "Setting '$query' could not be found in WMI mappings."
                } else {
                    Show-SettingDetails $entry
                }
            }
            "3" {
                $query = (Read-Host "Enter setting name or WMI offset to change").Trim()
                if ([string]::IsNullOrEmpty($query)) {
                    Write-Warning "Setting name/offset cannot be empty."
                    continue
                }
                $entry = Find-SettingEntry $query
                if ($null -eq $entry) {
                    Write-Error "Setting '$query' could not be found in WMI mappings."
                    continue
                }
                
                Show-SettingDetails $entry
                
                $newValue = (Read-Host "Enter new value").Trim()
                if ([string]::IsNullOrEmpty($newValue)) {
                    Write-Warning "Value cannot be empty."
                    continue
                }
                
                $null = Set-SettingValue $entry $newValue
            }
            default {
                Write-Warning "Invalid choice. Please select 1, 2, 3, or 4."
            }
        }
    }
    exit 0
} else {
    # Non-interactive CLI Execution
    if ($List) {
        Show-AllSettings
        exit 0
    }

    # Match Setting if provided
    $entry = Find-SettingEntry $Setting
    if ($null -eq $entry) {
        if (![string]::IsNullOrEmpty($Setting)) {
            Write-Error "Setting '$Setting' could not be found in WMI mappings."
            exit 1
        } else {
            # Output usage help if no parameters given
            Write-Host "Acer BIOS WMI Configuration CLI" -ForegroundColor Cyan
            Write-Host "Usage:"
            Write-Host "  bioseditor.bat -List                                       List all WMI settings and values"
            Write-Host "  bioseditor.bat -Get <Name/Offset>                          Query configuration and options for a setting"
            Write-Host "  bioseditor.bat -Setting <Name/Offset> -Value <NewValue>    Change a setting's value"
            Write-Host "  Options:"
            Write-Host "    -Password <SupervisorPassword>                           Required if BIOS is supervisor-locked"
            exit 0
        }
    }

    # Case B: Get / Query specific setting
    if ($Get -or [string]::IsNullOrEmpty($Value)) {
        Show-SettingDetails $entry
        exit 0
    }

    # Case C: Set setting value
    if (![string]::IsNullOrEmpty($Value)) {
        if (Set-SettingValue $entry $Value) {
            exit 0
        } else {
            exit 1
        }
    }
}

