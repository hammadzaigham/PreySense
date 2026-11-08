# Shared initialization loader for test tools
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

$nativeCs = Join-Path $PSScriptRoot 'PreySenseNative.cs'
$hash = (Get-FileHash -LiteralPath $nativeCs -Algorithm SHA256).Hash.Substring(0, 12)
$binDir = Join-Path $PSScriptRoot 'bin'
if (-not (Test-Path $binDir)) {
    New-Item -ItemType Directory -Path $binDir -Force | Out-Null
}
$dllPath = Join-Path $binDir "PreySenseNative_$hash.dll"

if (-not (Test-Path -LiteralPath $dllPath)) {
    Get-ChildItem -Path $binDir -Filter 'PreySenseNative_*.dll' -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue
    Add-Type -TypeDefinition (Get-Content -LiteralPath $nativeCs -Raw) -OutputAssembly $dllPath
}

if (-not ([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.Location -eq $dllPath })) {
    Add-Type -Path $dllPath
}
