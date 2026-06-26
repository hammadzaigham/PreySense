param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Output = "publish\PreySense"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$publishRoot = Join-Path $root "publish"
$outputPath = if ([System.IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $root $Output }

if (Test-Path -LiteralPath $publishRoot) {
    try {
        Get-ChildItem -LiteralPath $publishRoot -Force | Remove-Item -Recurse -Force
    }
    catch {
        throw "Could not clear '$publishRoot'. Close any running PreySense.exe from that folder, or pass a different -Output path."
    }
}

New-Item -ItemType Directory -Path $outputPath -Force | Out-Null

dotnet publish app\PreySense.csproj `
    -c $Configuration `
    -r $Runtime `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $outputPath

$pdb = Join-Path $outputPath "PreySense.pdb"
if (Test-Path -LiteralPath $pdb) {
    Remove-Item -LiteralPath $pdb -Force
}

Write-Host "Published:"
Get-ChildItem -LiteralPath $outputPath -Force | Select-Object Name, Length, LastWriteTime
