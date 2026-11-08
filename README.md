# <img src="PreySense.Frontend/appicon.svg" alt="PreySense logo" width="42" height="42" align="left"> PreySense

PreySense is a Windows utility for Acer Predator laptops. It replaces the official Predator Sense suite.

**v3.0.0** uses a bundled kernel driver for per-mode CPU/GPU tuning — no OpenCore. See [What’s new in v3.0.0](docs/releases/v3.0.0.md).

Developed and verified on the **Acer Predator Helios Neo 16S (`PHN16S-71-98RF`)**. Other Predator models may work; that is not guaranteed.

<p align="center">
  <img src="pics/Dashboard-v2.png" alt="PreySense dashboard" width="440">
  <br>
  <em>Main dashboard — performance, GPU, display, fan, and quick-access controls</em>
</p>

## Screenshots

| Keyboard and logo lighting | Per-mode power limits |
| :---: | :---: |
| <img src="pics/Lighting.png" alt="Keyboard and logo lighting controls" width="420"> | <img src="pics/Power-Limits.png" alt="CPU and GPU power-limit controls" width="420"> |

| Platform settings | Metrics overlay configuration |
| :---: | :---: |
| <img src="pics/Platform-Settings.png" alt="Platform settings" width="420"> | <img src="pics/Metrics.png" alt="Metrics overlay configuration" width="420"> |

<p align="center">
  <img src="pics/Overlay.png" alt="PreySense hardware telemetry overlay" width="550">
  <br>
  <em>Real-time hardware telemetry overlay</em>
</p>

## Contents

- [Upgrade from v2](#upgrade-from-v2)
- [Features](#features)
- [Screenshots](#screenshots)
- [Requirements](#requirements)
- [Install and run](#install-and-run)
- [Hardware tuning](#hardware-tuning)
- [Building](#building)
- [Disclaimer](#disclaimer)

## Upgrade from v2

1. **Uninstall the previous PreySense version** before installing v3. Do not overwrite an old copy in place.
2. **OpenCore is not used anymore.** If an older PreySense install left OpenCore / EFI PreySense patches on the ESP, remove that setup before running v3.
3. Download the new `PreySense.exe` and run it as administrator.

Full notes: [docs/releases/v3.0.0.md](docs/releases/v3.0.0.md).

## Features

- **Performance modes** — Quiet (AC) / Eco (battery), Balanced, Performance, and Turbo. The Predator key next to Num Lock opens PreySense from the tray, or hides it when it is already open. Hold that key and press 1–5 to change mode: 1 Eco, 2 Quiet, 3 Balanced, 4 Performance, 5 Turbo. The chassis Mode / Turbo key still cycles modes.
- **Per-mode CPU and GPU power** — CPU PL1, PL2, and Tau, plus GPU Delta, Dynamic Boost, and platform offset. Manual Apply and automatic mode/startup/resume use the bundled driver with readback checks. Enable Tuning and save each mode to use its custom profile automatically.
- **Factory calibration** — first-enable capture of factory defaults into `preysense.json` for Reset.
- **GPU display switching** — Optimus, Automatic, Advanced Optimus (no reboot), and dGPU Only (reboot)
- **Fans** — The footer Fans button opens per-mode Auto, Max, or Custom choices for Eco, Quiet, Balanced, Performance, and Turbo. Max is independent of Custom; Custom offers separate CPU/GPU percentage curves that follow the hotter sensor, with 5°C editing points and one 0–5-second Fan Control Delay for speed increases and decreases. Curve speeds cannot decrease at higher temperatures, and dragging locks the selected temperature. Curves keep running in the tray with one-second checks and writes only when targets change. See [fan control](docs/fan_control.md).
- **Lighting** — four-zone keyboard and cover logo, hardware effects, custom animations (Rainbow Cycle, Police Strobe, Fire Ember, Matrix Cascade, Cylon Scanner, Aurora Drift, Ocean Tide, Heartbeat, Lightning Storm, Starlight, Ice Crystal), optional logo sync
- **Hardware overlay** — always-on-top CPU/GPU stats, selectable metrics, optional graphs, and FPS. Separate GPU Active and Effective clock choices. CPU requested voltage (VID) through the bundled CPU driver. Unavailable voltage is not reported as a measured value.
- **Display** — refresh rate, LCD overdrive, BlueLight Shield
- **Battery** — charge limit and USB power-off charging cutoff. The embedded controller only accepts 80% or 100%. PreySense cannot set any other charge limit.
- **Startup and tray** — run on Windows startup, hide to the tray, Predator key (next to Num Lock) to show or hide; tray menu follows the app's light/dark theme
- **Languages** — English, Simplified Chinese, Hindi, Spanish, and French

## Requirements

- Windows 10 or 11 x64
- Acer Predator laptop
- [Microsoft .NET 10 Windows Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/10.0)
- Run **as administrator**

## Install and run

1. Uninstall any previous PreySense version (required when coming from v2).
2. Download the latest `PreySense.exe`.
3. Run it as administrator. A second launch brings the existing instance to the front.
4. Close official Predator Sense if it is running.
5. For custom CPU/GPU limits, open Tuning, enable tuning, edit a mode, and save it. Saved profiles are applied when that mode is selected and restored at startup or after wake.
6. The bundled CPU driver provides static CPU limit control and supported CPU VID reads. Driver initialization failures are reported; no OpenCore or alternate ACPI path is used.

Closing the window hides to the tray. **Quit** exits.

## Hardware tuning

The bundled CPU driver stages the saved GPU profile while it is inactive, activates the selected Acer mode, and writes CPU MSR/MMIO limits last. Mode changes, startup, and resume use the saved per-mode values in `preysense.json` when **Enable Tuning** is on. Manual **Apply** uses the current editor values; **Save** applies and persists them.

There is no laptop-model or BIOS-version name check. Driver initialization, supported host-bridge discovery, register access, and readback checks still determine whether an operation can complete. Removing the name check does not establish compatibility with every firmware layout.

GPU thermal-trip editing and its injected ACPI calls have been removed. GPU temperature telemetry remains available. Actual GPU power remains subject to NVIDIA driver and firmware policy.

See [driver tuning](docs/driver_tuning.md) for application order, persistence, and failure behavior. Product overview: [v3.0.0 notes](docs/releases/v3.0.0.md).

## Building

Prerequisite: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .github\publish.ps1
```

The output is `dist\PreySense.exe`.

## Disclaimer

PreySense changes fans, power limits, and firmware-visible system state. Wrong settings can cause throttling, shutdowns, or instability. Use at your own risk.
