<#
.SYNOPSIS
    Programmatically opens and inspects subwindows in PreySense (Advanced, Lighting, Metrics, Fans, Settings).
.PARAMETER Window
    Subwindow to open: 'Settings', 'Advanced', 'Lighting', 'Metrics', 'Fans'.
.PARAMETER Capture
    Automatically capture a screenshot of the opened subwindow.
.PARAMETER Inspect
    Dump UI controls, sliders, and switches of the opened subwindow.
.PARAMETER CloseAfter
    Close the dialog after capturing/inspecting.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('Settings', 'Advanced', 'Tuning', 'Lighting', 'Keyboard', 'Metrics', 'Fans', 'FanCurve')]
    [string]$Window,

    [switch]$Capture,
    [switch]$Inspect,
    [switch]$CloseAfter
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\Init-Tools.ps1"

$autoIdMap = @{
    'Settings' = 'BtnSettings'
    'Advanced' = 'BtnNavMode'
    'Tuning'   = 'BtnNavMode'
    'Lighting' = 'BtnNavLighting'
    'Keyboard' = 'BtnNavLighting'
    'Metrics'  = 'BtnNavMetrics'
    'Fans'     = 'BtnNavFans'
    'FanCurve' = 'BtnNavFans'
}

$targetAutoId = $autoIdMap[$Window]

# Find running PreySense MainWindow
$wins = @([PreySense.TestTools.WindowHelper]::FindPreySenseWindows("PreySense") | Where-Object { $_.Title -eq 'PreySense' })
if ($wins.Count -eq 0) {
    Write-Warning "PreySense is not running. Launching with Restart-PreySense.ps1..."
    & "$PSScriptRoot\Restart-PreySense.ps1" -NoBuild | Out-Null
    Start-Sleep -Seconds 1
    $wins = @([PreySense.TestTools.WindowHelper]::FindPreySenseWindows("PreySense") | Where-Object { $_.Title -eq 'PreySense' })
}

if ($wins.Count -eq 0) {
    Write-Error "Could not find PreySense MainWindow."
    return
}

$mainWin = $wins[0]
$aeMain = [System.Windows.Automation.AutomationElement]::FromHandle($mainWin.HWnd)
if (-not $aeMain) {
    Write-Error "Could not acquire AutomationElement for HWND $($mainWin.HWnd)."
    return
}

# Find target button
$btnProp = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $targetAutoId)
$btn = $aeMain.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $btnProp)

if (-not $btn) {
    # Capturing or inspecting a window can deactivate PreySense, which correctly
    # hides it to the tray. Restore it through the application's existing named
    # event and wait for the automation tree before treating this as a failure.
    try {
        $showEvent = [System.Threading.EventWaitHandle]::OpenExisting('PreySense_Show_App_Event')
        $showEvent.Set() | Out-Null
        $showEvent.Dispose()
    } catch { }

    $restoreWatch = [System.Diagnostics.Stopwatch]::StartNew()
    while (-not $btn -and $restoreWatch.Elapsed.TotalSeconds -lt 3) {
        Start-Sleep -Milliseconds 100
        $restoredWindows = @([PreySense.TestTools.WindowHelper]::FindPreySenseWindows('PreySense') | Where-Object { $_.Title -eq 'PreySense' })
        if ($restoredWindows.Count -eq 0) {
            continue
        }
        $mainWin = $restoredWindows[0]
        $aeMain = [System.Windows.Automation.AutomationElement]::FromHandle($mainWin.HWnd)
        if ($aeMain) {
            $btn = $aeMain.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $btnProp)
        }
    }

    if (-not $btn) {
        Write-Error "Could not find button with AutomationId '$targetAutoId' on MainWindow after restoring it from the tray."
        return
    }
}

Write-Host "[*] Triggering '$Window' (Button: $targetAutoId)..." -ForegroundColor Cyan
$invPattern = $null
if ($btn.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$invPattern)) {
    ($invPattern -as [System.Windows.Automation.InvokePattern]).Invoke()
} else {
    Write-Error "Button does not support InvokePattern."
    return
}

# Fans is an in-place dashboard state, not an owned secondary window.
if ($Window -in @('Fans', 'FanCurve')) {
    Start-Sleep -Milliseconds 500
    $screenPath = $null
    if ($Capture) {
        $cap = & "$PSScriptRoot\Capture-Window.ps1" -Hwnd $mainWin.HWnd
        if ($cap -and $cap.Count -gt 0) {
            $screenPath = $cap[0].FilePath
        }
    }
    return [PSCustomObject]@{
        HWnd           = $mainWin.HWnd
        ProcessId      = $mainWin.ProcessId
        Title          = $mainWin.Title
        ClassName      = $mainWin.ClassName
        Left           = $mainWin.Left
        Top            = $mainWin.Top
        Width          = $mainWin.Width
        Height         = $mainWin.Height
        IsVisible      = $mainWin.IsVisible
        ScreenshotPath = $screenPath
    }
}

# Wait for subwindow to appear
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$subWin = $null

while ($sw.Elapsed.TotalSeconds -lt 4) {
    Start-Sleep -Milliseconds 250
    $allWins = [PreySense.TestTools.WindowHelper]::FindPreySenseWindows("*")
    foreach ($w in $allWins) {
        if ($w.HWnd -ne $mainWin.HWnd -and $w.Width -gt 100 -and $w.Height -gt 100) {
            $subWin = $w
            break
        }
    }
    if ($subWin) { break }
}

if (-not $subWin) {
    Write-Warning "Subwindow was triggered, but no separate window handle was detected within 4 seconds."
    return
}

Write-Host "[OK] Subwindow opened successfully!" -ForegroundColor Green
Write-Host "     Title:   '$($subWin.Title)'" -ForegroundColor White
Write-Host "     HWND:    $($subWin.HWnd)" -ForegroundColor White
Write-Host "     Bounds:  $($subWin.Width)x$($subWin.Height) at ($($subWin.Left), $($subWin.Top))" -ForegroundColor White

$screenPath = $null
if ($Capture) {
    Write-Host "`n[*] Capturing subwindow screenshot..." -ForegroundColor Gray
    $cap = & "$PSScriptRoot\Capture-Window.ps1" -Hwnd $subWin.HWnd
    if ($cap -and $cap.Count -gt 0) {
        $screenPath = $cap[0].FilePath
    }
}

if ($Inspect) {
    Write-Host "`n[*] Inspecting subwindow UI tree..." -ForegroundColor Gray
    & "$PSScriptRoot\Inspect-UiTree.ps1" -WindowTitle $subWin.Title
}

if ($CloseAfter) {
    Write-Host "`n[*] Closing subwindow..." -ForegroundColor Gray
    $aeSub = [System.Windows.Automation.AutomationElement]::FromHandle($subWin.HWnd)
    if ($aeSub) {
        $winPattern = $null
        if ($aeSub.TryGetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern, [ref]$winPattern)) {
            ($winPattern -as [System.Windows.Automation.WindowPattern]).Close()
            Write-Host "[OK] Subwindow closed." -ForegroundColor Green
        } else {
            # Try finding close button
            $closeBtnProp = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "✕")
            $closeBtn = $aeSub.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $closeBtnProp)
            if ($closeBtn) {
                $closeInv = $null
                if ($closeBtn.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$closeInv)) {
                    ($closeInv -as [System.Windows.Automation.InvokePattern]).Invoke()
                    Write-Host "[OK] Closed via ✕ button." -ForegroundColor Green
                }
            }
        }
    }
}

return [PSCustomObject]@{
    HWnd           = $subWin.HWnd
    ProcessId      = $subWin.ProcessId
    Title          = $subWin.Title
    ClassName      = $subWin.ClassName
    Left           = $subWin.Left
    Top            = $subWin.Top
    Width          = $subWin.Width
    Height         = $subWin.Height
    IsVisible      = $subWin.IsVisible
    ScreenshotPath = $screenPath
}
