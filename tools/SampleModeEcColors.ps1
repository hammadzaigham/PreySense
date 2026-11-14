$ErrorActionPreference = 'Stop'
$logPath = Join-Path $PSScriptRoot '..\outputs\mode-key-ec-colors.log'
$logPath = [IO.Path]::GetFullPath($logPath)
New-Item -ItemType Directory -Force -Path (Split-Path $logPath) | Out-Null

$inst = Get-CimInstance -Namespace root\WMI -ClassName AcerGamingFunction | Select-Object -First 1

function Set-KbOpMode {
    $payload = [byte[]](0x22, 0, 100, 0, 0, 0, 0, 0, 3, 1, 0, 0, 0, 0, 0, 0)
    $r = Invoke-CimMethod -InputObject $inst -MethodName SetGamingKBBacklight -Arguments @{ gmInput = $payload }
    return $r.gmOutput
}

function Get-Mode {
    $u = [uint64](Invoke-CimMethod -InputObject $inst -MethodName GetGamingMiscSetting -Arguments @{ gmInput = [uint32]0x0B }).gmOutput
    return [byte](($u -shr 8) -band 0xFF)
}

function Read-Kb {
    $r = Invoke-CimMethod -InputObject $inst -MethodName GetGamingKBBacklight -Arguments @{ gmInput = [uint32]1 }
    $b = [byte[]]$r.gmOutput
    $hex = ($b | ForEach-Object { $_.ToString('X2') }) -join ' '
    $cRgb = if ($b.Length -ge 8) { '#{0:X2}{1:X2}{2:X2}' -f $b[5], $b[6], $b[7] } else { '?' }
    $cBgr = if ($b.Length -ge 8) { '#{0:X2}{1:X2}{2:X2}' -f $b[7], $b[6], $b[5] } else { '?' }
    [pscustomobject]@{ Ret = $r.gmReturn; Hex = $hex; Rgb = $cRgb; Bgr = $cBgr }
}

function ModeName([byte]$m) {
    switch ($m) {
        0 { 'Quiet' }
        1 { 'Balanced' }
        4 { 'Performance' }
        5 { 'Turbo' }
        6 { 'Eco' }
        default { '0x{0:X2}' -f $m }
    }
}

$setOut = Set-KbOpMode
Start-Sleep -Milliseconds 250
$before = Read-Kb
@(
    "=== OPMODE sample $(Get-Date -Format o) ==="
    "SetGamingKBBacklight OPMODE gmOutput=$setOut"
    "After Set OPMODE: ret=$($before.Ret) bytes=$($before.Hex) rgb=$($before.Rgb) bgr=$($before.Bgr)"
) | Set-Content -Encoding utf8 $logPath

$lastMode = Get-Mode
$lastHex = $before.Hex
$changeAt = Get-Date
$sw = [Diagnostics.Stopwatch]::StartNew()
"Start mode=$(ModeName $lastMode). Change modes for 75s..." | Tee-Object -FilePath $logPath -Append
Write-Host "READY log=$logPath — change Quiet/Eco/Balanced/Performance/Turbo now (75s)"

$end = (Get-Date).AddSeconds(75)
while ((Get-Date) -lt $end) {
    $mode = Get-Mode
    $kb = Read-Kb
    $ms = [int]$sw.Elapsed.TotalMilliseconds
    if ($mode -ne $lastMode) {
        "MODE_CHANGE t=${ms}ms $(ModeName $lastMode)->$(ModeName $mode) bytes=$($kb.Hex) rgb=$($kb.Rgb) bgr=$($kb.Bgr)" |
            Tee-Object -FilePath $logPath -Append | Out-Null
        Write-Host "MODE $(ModeName $lastMode)->$(ModeName $mode) rgb=$($kb.Rgb) bgr=$($kb.Bgr)"
        $lastMode = $mode
        $changeAt = Get-Date
        $lastHex = $kb.Hex
    }
    elseif ($kb.Hex -ne $lastHex) {
        $since = [int]((Get-Date) - $changeAt).TotalMilliseconds
        "COLOR_CHANGE t=${ms}ms sinceMode=${since}ms mode=$(ModeName $mode) bytes=$($kb.Hex) rgb=$($kb.Rgb) bgr=$($kb.Bgr)" |
            Tee-Object -FilePath $logPath -Append | Out-Null
        Write-Host "COLOR sinceMode=${since}ms mode=$(ModeName $mode) rgb=$($kb.Rgb) bgr=$($kb.Bgr)"
        $lastHex = $kb.Hex
    }
    Start-Sleep -Milliseconds 30
}

"DONE t=$([int]$sw.Elapsed.TotalMilliseconds)ms" | Tee-Object -FilePath $logPath -Append
Write-Host "DONE — see $logPath"
