# Run as administrator
if (!([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")) { 
    $arguments = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $PSCommandPath)
    Start-Process -FilePath powershell.exe -ArgumentList $arguments -Verb RunAs
    exit
}

$logFile = "$PSScriptRoot\dump_save_results.txt"
$binPath = "$PSScriptRoot\bios_dump_unlocked.bin"
$hexTextPath = "$PSScriptRoot\bios_dump_unlocked_hex.txt"
$nzPath = "$PSScriptRoot\bios_non_zero_bytes.txt"
$log = @()

function Log($text) {
    $global:log += $text
    Write-Output $text
}

try {
    Log "============================================="
    Log "  BIOS OPTION UNLOCK & SAVE SERVICE          "
    Log "============================================="
    Log "Timestamp: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    Log ""

    # Get WMI instances
    $utilFunc = Get-CimInstance -Namespace root\wmi -ClassName UtilityFunction -ErrorAction Stop
    $biosTool = Get-CimInstance -Namespace root\wmi -ClassName AcerBiosConfigurationTool -ErrorAction Stop
    Log "[+] Located WMI instances successfully."

    # Construct the 128-byte buffers
    $pw = ""
    $pwLength = if ($pw.Length -gt 15) { 15 } else {$pw.Length}
    $asciiBytes = [System.Text.Encoding]::ASCII.GetBytes($pw)
    $pwBuffer = [byte[]]::new(128)
    if ($pwLength -gt 0) {
        [Array]::Copy($asciiBytes, $pwBuffer, $pwLength)
    }
    $emptyBuffer = [byte[]]::new(128)

    # 1. Step 1: CheckPassword(USER, empty)
    Log "[*] Step 1: CheckPassword(USER, empty)..."
    $resUser = Invoke-CimMethod -InputObject $utilFunc -MethodName CheckPassword -Arguments @{
        Command = "USER"
        Password = $emptyBuffer
        PasswordLen = [UInt16]0
    } -ErrorAction Stop
    Log "    ReturnCode: $($resUser.ReturnCode), PasswordIsDifferent: $($resUser.PasswordIsDifferent)"

    # 2. Step 2: CheckPassword(ADMIN, '$pw')
    Log "[*] Step 2: CheckPassword(ADMIN, '$pw')..."
    $resAdmin = Invoke-CimMethod -InputObject $utilFunc -MethodName CheckPassword -Arguments @{
        Command = "ADMIN"
        Password = $pwBuffer
        PasswordLen = [UInt16]$pwLength
    } -ErrorAction Stop
    Log "    ReturnCode: $($resAdmin.ReturnCode), PasswordIsDifferent: $($resAdmin.PasswordIsDifferent)"

    # 3. Step 3: GetBiosOptions('$pw', PasswordLen=$pwLength)
    Log "[*] Step 3: GetBiosOptions('$pw', PasswordLen=$pwLength)..."
    $res = Invoke-CimMethod -InputObject $biosTool -MethodName GetBiosOptions -Arguments @{
        Password = $pwBuffer
        PasswordLen = [UInt16]$pwLength
    } -ErrorAction Stop
    Log "    ReturnValue: $($res.ReturnValue)"
    Log "    ReturnCode: $($res.ReturnCode) (Hex: 0x$($res.ReturnCode.ToString('X')))"

    if ($null -ne $res.Data) {
        $data = $res.Data
        $nonZeroCount = 0
        for ($i=0; $i -lt $data.Length; $i++) {
            if ($data[$i] -ne 0) { $nonZeroCount++ }
        }
        Log "[+] Retrieved data: $($data.Length) bytes ($nonZeroCount non-zero bytes)."

        if ($res.ReturnCode -eq 0 -and $nonZeroCount -gt 0) {
            # Save raw binary file
            [System.IO.File]::WriteAllBytes($binPath, $data)
            Log "[+] Saved raw binary dump to: $binPath"

            # Format hex dump (16 bytes per line)
            $hexLines = @()
            for ($offset = 0; $offset -lt $data.Length; $offset += 16) {
                $chunkSize = [Math]::Min(16, $data.Length - $offset)
                $chunk = $data[$offset..($offset + $chunkSize - 1)]
                $hexStr = ($chunk | ForEach-Object { $_.ToString("X2") }) -join " "
                if ($chunkSize -lt 16) {
                    $hexStr += " " * ((16 - $chunkSize) * 3)
                }
                $asciiChars = ""
                foreach ($b in $chunk) {
                    if ($b -ge 32 -and $b -le 126) {
                        $asciiChars += [char]$b
                    } else {
                        $asciiChars += "."
                    }
                }
                $hexLines += "$($offset.ToString('X4')): $hexStr  | $asciiChars |"
            }
            $hexLines | Out-File -FilePath $hexTextPath -Encoding ascii
            Log "[+] Saved hex text dump to: $hexTextPath"

            # Save non-zero registers list
            $nonZeroRegisters = @()
            for ($i = 0; $i -lt $data.Length; $i++) {
                if ($data[$i] -ne 0) {
                    $nonZeroRegisters += [PSCustomObject]@{
                        OffsetDec = $i
                        OffsetHex = "0x$($i.ToString('X4'))"
                        ValueDec  = $data[$i]
                        ValueHex  = "0x$($data[$i].ToString('X2'))"
                    }
                }
            }
            $nonZeroRegisters | Format-Table -AutoSize | Out-File -FilePath $nzPath -Encoding utf8
            Log "[+] Saved non-zero registers list to: $nzPath"
            Log "[+] Done!"
        } else {
            Log "[-] Dump failed or returned empty data."
        }
    } else {
        Log "[-] Data array is null."
    }

} catch {
    Log "[-] Unhandled error: $_"
} finally {
    $log | Out-File -FilePath $logFile -Encoding utf8
    Write-Output "Completed. Results saved to $logFile"
}
