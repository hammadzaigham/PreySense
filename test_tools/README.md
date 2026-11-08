# PreySense UI test tools

PowerShell helpers that launch PreySense on the interactive desktop (`WinSta0\Default`) so WPF windows are visible and capturable.

## Runner

```powershell
.\test_tools\Run.ps1 -Restart
.\test_tools\Run.ps1 -Monitor
.\test_tools\Run.ps1 -Capture
.\test_tools\Run.ps1 -Inspect
.\test_tools\Run.ps1 -Open Lighting
.\test_tools\Run.ps1 -Open Metrics
.\test_tools\Run.ps1 -Open Advanced
.\test_tools\Run.ps1 -Open Settings
.\test_tools\Run.ps1 -SmokeTest
```

`-SmokeTest` closes running instances, rebuilds, launches, and captures the main window plus the scripted subwindows. Use `-NoBuild` only against an already compiled binary.

## Window map (`-Open`)

| Name | Automation id | Window |
| :--- | :--- | :--- |
| Lighting / Keyboard | `BtnNavLighting` | Keyboard / cover lighting |
| Metrics | `BtnNavMetrics` | Overlay column editor |
| Advanced | `BtnNavAdvanced` | Mode CPU/GPU limits |
| Settings | `BtnSettings` | Platform settings |
| Fans | `BtnFanCustom` | Selects Custom fans on the main window |

## Scripts

- `Restart-PreySense.ps1` — close, build, spawn on the interactive desktop
- `Monitor-Window.ps1` — process health and HWNDs
- `Capture-Window.ps1` — GDI/`PrintWindow` PNG under `test_tools\captures`
- `Inspect-UiTree.ps1` — UI Automation dump (`-Sliders`, `-Switches`, `-Buttons`)
- `Open-Subwindow.ps1` — click a footer/title-bar button, capture, optional inspect
- `Test-UiWorkflow.ps1` — smoke suite → `smoke_test_report.md`
