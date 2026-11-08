<#
.SYNOPSIS
    Captures high-resolution PNG screenshot of PreySense windows using native GDI and PrintWindow.
.PARAMETER WindowTitle
    Filter for specific window (default: 'PreySense', or '*' for all).
.PARAMETER OutputPath
    Custom path to save the screenshot.
.PARAMETER Open
    Open the image in default image viewer after capturing.
#>
[CmdletBinding()]
param(
    [long]$Hwnd = 0,
    [string]$WindowTitle = "PreySense",
    [string]$OutputPath,
    [switch]$Open
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\Init-Tools.ps1"

$capturesDir = "$PSScriptRoot\captures"
if (-not (Test-Path $capturesDir)) {
    New-Item -ItemType Directory -Path $capturesDir -Force | Out-Null
}

$wins = [System.Collections.Generic.List[PreySense.TestTools.WindowInfo]]::new()
if ($Hwnd -ne 0) {
    $info = [PreySense.TestTools.WindowHelper]::GetWindowInfo([IntPtr]$Hwnd)
    if ($info) {
        $wins.Add($info)
    }
} else {
    $found = [PreySense.TestTools.WindowHelper]::FindPreySenseWindows($WindowTitle)
    if ($found.Count -eq 0 -and $WindowTitle -ne "*") {
        $found = [PreySense.TestTools.WindowHelper]::FindPreySenseWindows("*")
    }
    foreach ($w in $found) { $wins.Add($w) }
}

if ($wins.Count -eq 0) {
    Write-Warning "No capture-ready PreySense windows found. Launch it with: .\test_tools\Restart-PreySense.ps1"
    return
}

$results = [System.Collections.Generic.List[PSCustomObject]]::new()

foreach ($w in $wins) {
    $cleanTitle = if ($w.Title) { ($w.Title -replace '[^a-zA-Z0-9_\-]', '_') } else { "PreySense_Window" }
    $timestamp = (Get-Date).ToString("yyyyMMdd_HHmmss")
    $targetFile = if ($OutputPath) { $OutputPath } else { "$capturesDir\${cleanTitle}_${timestamp}.png" }
    $latestFile = "$capturesDir\latest.png"

    Write-Host "[*] Capturing HWND $($w.HWnd) ('$($w.Title)')..." -ForegroundColor Gray
    $capWidth = 0
    $capHeight = 0
    $shadow = New-Object 'PreySense.TestTools.WindowHelper+ShadowInsets'
    $hBmp = [PreySense.TestTools.WindowHelper]::CaptureHBitmap($w.HWnd, [ref]$capWidth, [ref]$capHeight, [ref]$shadow)

    if ($hBmp -ne [IntPtr]::Zero) {
        try {
            $bmp = [System.Drawing.Image]::FromHbitmap($hBmp)
            $bmp.Save($targetFile, [System.Drawing.Imaging.ImageFormat]::Png)
            $bmp.Dispose()
            Copy-Item -Path $targetFile -Destination $latestFile -Force
        } finally {
            [PreySense.TestTools.WindowHelper]::DeleteObject($hBmp) | Out-Null
        }

        $fileUri = (Resolve-Path $targetFile).Path -replace '\\', '/'
        Write-Host "[OK] Screenshot saved successfully!" -ForegroundColor Green
        Write-Host "     Path: file:///$fileUri" -ForegroundColor White
        Write-Host "     Size: ${capWidth}x${capHeight} (shadow L$($shadow.Left) T$($shadow.Top) R$($shadow.Right) B$($shadow.Bottom) cropped)" -ForegroundColor White

        if ($Open) {
            Invoke-Item $targetFile
        }

        $results.Add([PSCustomObject]@{
            HWnd         = $w.HWnd
            Title        = $w.Title
            FilePath     = $targetFile
            FileUri      = "file:///$fileUri"
            Width        = $capWidth
            Height       = $capHeight
            ShadowLeft   = $shadow.Left
            ShadowTop    = $shadow.Top
            ShadowRight  = $shadow.Right
            ShadowBottom = $shadow.Bottom
        })
    } else {
        Write-Error "Failed to capture window '$($w.Title)' (HWND $($w.HWnd))."
    }
}

return $results
