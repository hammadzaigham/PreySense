Param (
    [int] $Index = -1
    ,$Value = -1
    ,[Switch] $Default = $false
    ,[Switch] $Dump = $false
    ,[String] $Password = ""
)
# Run as administrator
if (!([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")) { 
    $arguments = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", "$PSCommandPath $($MyInvocation.Line.Replace($MyInvocation.InvocationName, '').Trim())")
    Start-Process -FilePath powershell.exe -ArgumentList $arguments -Verb RunAs; exit }

$cls = Get-WmiObject -Namespace root\wmi -Class AcerBiosConfigurationTool

if ($Dump -eq $false -and $Value -eq -1 -and $Index -eq -1) {
    Write-Output "Usage:"
    "-Index <offset>"
    "-Value <byte value>"
    "-Dump"
    "-Default"
    "-Password <bios password>"
    exit
}

$PasswordLength = if ($Password.Length -gt 15) { 15 } else {$Password.Length}
$PasswordBytes = [byte[]]::new(128)
if ($PasswordLength -gt 0) {
    $PasswordBytesOrig = [System.Text.Encoding]::ASCII.GetBytes($Password)
    [Array]::Copy($PasswordBytesOrig, $PasswordBytes, $PasswordLength)
}

# Authenticate session using UtilityFunction if available
$util = Get-WmiObject -Namespace root\wmi -Class UtilityFunction -ErrorAction SilentlyContinue
if ($util) {
    $emptyBytes = [byte[]]::new(128)
    
    # Step 1: CheckPassword(USER, empty)
    $inParamsUser = $util.GetMethodParameters("CheckPassword")
    $inParamsUser["Command"] = "USER"
    $inParamsUser["Password"] = $emptyBytes
    $inParamsUser["PasswordLen"] = [UInt16]0
    $null = $util.InvokeMethod("CheckPassword", $inParamsUser, [System.Management.InvokeMethodOptions]$null)

    # Step 2: CheckPassword(ADMIN, password)
    $inParamsAdmin = $util.GetMethodParameters("CheckPassword")
    $inParamsAdmin["Command"] = "ADMIN"
    $inParamsAdmin["Password"] = $PasswordBytes
    $inParamsAdmin["PasswordLen"] = [UInt16]$PasswordLength
    $resAdmin = $util.InvokeMethod("CheckPassword", $inParamsAdmin, [System.Management.InvokeMethodOptions]$null)
    
    if ($resAdmin -and $resAdmin.ReturnCode -ne 0) {
        Write-Error "BIOS password authentication failed (ReturnCode: $($resAdmin.ReturnCode)). Please check your password."
        exit
    }
}

# Fetch BIOS options
$result = if ($Default -eq $true) { 
    $inParams = $cls.GetMethodParameters("GetBiosDefaultOptions")
    $inParams["PasswordLen"] = [UInt16]$PasswordLength
    $inParams["Password"] = $PasswordBytes
    $cls.InvokeMethod("GetBiosDefaultOptions", $inParams, [System.Management.InvokeMethodOptions]$null)
} 
else { 
    $inParams = $cls.GetMethodParameters("GetBiosOptions")
    $inParams["PasswordLen"] = [UInt16]$PasswordLength
    $inParams["Password"] = $PasswordBytes
    $cls.InvokeMethod("GetBiosOptions", $inParams, [System.Management.InvokeMethodOptions]$null)
}

if ($null -eq $result -or $result.ReturnCode -ne 0) {
    $errCode = if ($result) { $result.ReturnCode } else { "Unknown" }
    Write-Error "Failed to retrieve BIOS options (ReturnCode: $errCode). Verify your supervisor password is correct."
    exit
}

$data = $result.Data
if ($null -eq $data) {
    Write-Error "BIOS options data buffer is null."
    exit
}

if ($Dump -eq $true) {
    $data
    exit
}

if ($Value -ne -1) {
    $data[$Index] = $Value
    $inParams = $cls.GetMethodParameters("SetBiosOptions")
    $inParams["PasswordLen"] = [UInt16]$PasswordLength
    $inParams["Password"] = $PasswordBytes
    $inParams["Data"] = $data
    $resultSet = $cls.InvokeMethod("SetBiosOptions", $inParams, [System.Management.InvokeMethodOptions]$null)
    
    if ($resultSet -and ($resultSet.ReturnCode -eq 0 -or $resultSet.ReturnCode -eq 8)) {
        Write-Output "Successfully updated BIOS setting at Index $Index to $Value."
        Write-Output "Please REBOOT your computer to apply the changes."
    }
    else {
        $errCode = if ($resultSet) { $resultSet.ReturnCode } else { "Unknown" }
        Write-Error "Failed to write BIOS options (ReturnCode: $errCode)."
    }
}
else {
    $data[$Index]
}
