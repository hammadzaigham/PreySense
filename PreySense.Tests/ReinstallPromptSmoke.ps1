# Exercises only completion reporting and the restart prompt. No EFI, registry,
# elevation, or actual shutdown operations are executed.
$ErrorActionPreference = 'Stop'
$scriptText = [IO.File]::ReadAllText((Join-Path (Split-Path -Parent $PSScriptRoot) 'tools\OpenCore_PreyBoost_EFI\Reinstall-To-ESP.ps1'))
$start = $scriptText.IndexOf('# Report completion before waiting for input')
if ($start -lt 0) { throw 'Completion section not found.' }
$completion = [scriptblock]::Create($scriptText.Substring($start))
$ResultPath = Join-Path ([IO.Path]::GetTempPath()) ('PreySense-Prompt-Test-' + [guid]::NewGuid().ToString('N') + '.json')
$written = @(1, 2)
$aml = New-Object byte[] 36
[BitConverter]::GetBytes([uint32]0x0201).CopyTo($aml, 24)
$backupRoot = 'test backup'
$failureMessage = $null
$script:restartRequested = $false
$script:answers = [System.Collections.Generic.Queue[string]]::new()
function Read-Host {
    param([string]$Prompt)
    if (-not (Test-Path -LiteralPath $ResultPath)) { throw 'App completion must be reported before waiting for input.' }
    if (-not $script:answers.Count) { throw 'Unexpected restart prompt.' }
    return $script:answers.Dequeue()
}
function shutdown.exe {
    $script:restartRequested = $true
    $global:LASTEXITCODE = 0
}
try {
    $succeeded = $true
    $script:answers.Enqueue('invalid')
    $script:answers.Enqueue('N')
    . $completion
    if ($script:answers.Count -or $script:restartRequested) { throw 'N must skip restart; invalid responses must be retried.' }
    if (-not ((Get-Content -LiteralPath $ResultPath -Raw | ConvertFrom-Json).Success)) { throw 'Success not reported.' }
    $script:answers.Enqueue('y')
    . $completion
    if (-not $script:restartRequested) { throw 'Y did not request restart.' }
    $script:restartRequested = $false
    $succeeded = $false
    $failureMessage = 'simulated copy failure'
    . $completion
    if ($script:restartRequested) { throw 'Failed updates must not restart.' }
    $result = Get-Content -LiteralPath $ResultPath -Raw | ConvertFrom-Json
    if ($result.Success -or $result.Message -ne $failureMessage) { throw 'Failure not reported.' }
    Write-Host 'PASS: completion before prompt; success/failure reporting; invalid input retried; N skips restart; Y requests restart; failure never restarts.'
}
finally {
    Remove-Item -LiteralPath $ResultPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath ($ResultPath + '.pending') -Force -ErrorAction SilentlyContinue
}
