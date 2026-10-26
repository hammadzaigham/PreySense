# <img src="PreySense.Frontend/appicon.svg" alt="PreySense logo" width="42" height="42" align="left"> PreySense

PreySense is a Windows utility for Acer Predator laptops. It replaces the official Predator Sense suite.

**v2.4.5** adds interactive custom CPU & GPU fan curves, factory fan table presets, fan ramp-up delay, emergency cooling protection, and serialized power/game mode policy fixes. See the [release notes](docs/releases/v2.4.5.md).

Developed and verified on the **Acer Predator Helios Neo 16S (`PHN16S-71-98RF`)**. Other Predator models may work; that is not guaranteed.

<p align="center">
  <img src="pics/Dashboard-v2.png" alt="PreySense v2 dashboard" width="440">
  <br>
  <em>Main dashboard — performance, GPU, display, fan, and quick-access controls</em>
</p>

## Screenshots

| Keyboard and logo lighting | Per-mode power limits |
| :---: | :---: |
| <img src="pics/Lighting.png" alt="Keyboard and logo lighting controls" width="420"> | <img src="pics/Power-Limits.png" alt="CPU and GPU power-limit controls" width="420"> |

| Platform and OpenCore settings | Metrics overlay configuration |
| :---: | :---: |
| <img src="pics/Platform-Settings.png" alt="Platform and OpenCore settings" width="420"> | <img src="pics/Metrics.png" alt="Metrics overlay configuration" width="420"> |

<p align="center">
  <img src="pics/Overlay.png" alt="PreySense hardware telemetry overlay" width="550">
  <br>
  <em>Real-time hardware telemetry overlay</em>
</p>

## Contents

- [Features](#features)
- [Screenshots](#screenshots)
- [Requirements](#requirements)
- [Install and run](#install-and-run)
- [OpenCore](#opencore)
- [Building](#building)
- [Disclaimer](#disclaimer)

## Features

- **Performance modes** — Quiet (AC) / Eco (battery), Balanced, Performance, and Turbo. The Predator key next to Num Lock opens PreySense from the tray, or hides it when it is already open. Hold that key and press 1–5 to change mode: 1 Eco, 2 Quiet, 3 Balanced, 4 Performance, 5 Turbo. The chassis Mode / Turbo key still cycles modes.
- **Per-mode CPU and GPU power** — CPU PL1, PL2, and Tau; separate GPU Delta and Dynamic Boost controls; GPU thermal trip point (requires OpenCore and factory calibration). PawnIO writes CPU static MSR limits alongside the dynamic MMIO limits, with readback checks.
- **GPU display switching** — Optimus, Automatic, Advanced Optimus (no reboot), and dGPU Only (reboot)
- **Fans** — The footer Fans button opens per-mode Auto, Max, or Custom choices for Eco, Quiet, Balanced, Performance, and Turbo. Max is independent of Custom; Custom offers separate CPU/GPU percentage curves that follow the hotter sensor, with 5°C editing points and one 0–5-second Fan Control Delay for speed increases and decreases. Curve speeds cannot decrease at higher temperatures, and dragging locks the selected temperature. Curves keep running in the tray with one-second checks and writes only when targets change. See [fan control](docs/fan_control.md).
- **Lighting** — four-zone keyboard and cover logo, hardware effects, custom animations (Rainbow Cycle, Police Strobe, Fire Ember, Matrix Cascade, Cylon Scanner, Aurora Drift, Ocean Tide, Heartbeat, Lightning Storm, Starlight, Ice Crystal), optional logo sync
- **Game profiles** — apply a mode and CPU/GPU limits when a watched game starts; restore when it exits. Games uses the same separate Delta and Dynamic Boost controls as Tuning.
- **Hardware overlay** — always-on-top CPU/GPU stats, optional graphs, and FPS. CPU voltage (VID) through PawnIO replaces E-core frequency in the default layout. Unavailable voltage displays as 0.000 V.
- **Display** — refresh rate, LCD overdrive, BlueLight Shield
- **Battery** — charge limit and USB power-off charging cutoff. The embedded controller only accepts 80% or 100%. PreySense cannot set any other charge limit.
- **Startup and tray** — run on Windows startup, hide to the tray, Predator key (next to Num Lock) to show or hide; tray menu follows the app's light/dark theme
- **Languages** — English, Simplified Chinese, Hindi, Spanish, and French
- **OpenCore installer** — optional boot-drive or USB install from Advanced for live power limits, plus an in-place **Re-install** button with file verification, backups, and a Y/N restart prompt
- **Factory defaults** — required first-time calibration when OpenCore is active, immutable saved defaults, and a compact profile summary with common metrics shown once

## Requirements

- Windows 10 or 11 x64
- Acer Predator laptop
- [Microsoft .NET 10 Windows Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/10.0)
- Run **as administrator**

## Install and run

1. Download the latest `PreySense.exe`.
2. Run it as administrator. A second launch brings the existing instance to the front.
3. Close official Predator Sense if it is running.
4. For CPU/GPU watt limits, install OpenCore from **Advanced** and set PreySense first in the BIOS boot order.
5. If PawnIO is missing, PreySense offers to download and install it. PawnIO is required for static CPU limit control and supported CPU VID reads; LibreHardwareMonitor is not used.

Closing the window hides to the tray. **Quit** exits.

## OpenCore

OpenCore is optional. Without it, modes, fans, lighting, GPU switching, and the overlay still work. CPU/GPU watt limits and game auto-apply stay locked until OpenCore is loaded, factory calibration has finished, and ACPI Override is turned on in Advanced.

When OpenCore is active and factory profiles are missing, startup requires **Calibrate** or **Uninstall OpenCore**. Calibration captures the missing modes before applying custom limits. Completed calibration cannot overwrite saved defaults. **Factory Defaults → View** shows profiles first, followed by the GPU baseline, GPU thermal trip point, Tau, and other common values.

**Install to Boot Drive** (from Advanced):

1. PreySense deletes any existing `PREYSENSE` partition, then creates a new small hidden EFI partition with that label.
2. The partition has no Windows drive letter. BIOS can still browse it.
3. Enroll all three files. Repeat the trusted-file step for each one:
   1. Open BIOS → Security. Set a supervisor password if asked, then reboot.
   2. Open BIOS → Security → **Select an UEFI file as trusted for executing**.
      `PREYSENSE` → `EFI` → `BOOT` → `BOOTX64.EFI`
   3. Repeat: `PREYSENSE` → `EFI` → `OC` → `OpenCore.efi`
   4. Repeat: `PREYSENSE` → `EFI` → `OC` → `Drivers` → `OpenRuntime.efi`
   5. Save and exit from BIOS. After restart, open BIOS again.
   6. Boot → put BOOTX64 first in the boot order. Save and exit.
   7. After booting, OpenCore status should show Active. Complete the startup calibration, then enable ACPI Override in Advanced.

USB install is the same files on a FAT32 stick. Restart, press F12, and select the USB drive. Unplug it to boot stock Windows.

### Updating from an older version

v2.4.0 uses ACPI interface **0x0201**. If PreySense reports outdated ACPI files, choose **Re-install** in Advanced. This updates the existing PREYSENSE partition in place, preserves unrelated EFI files, backs up replaced files, and verifies copied files with SHA256. The PowerShell window shows the completed update and asks whether to restart now (Y/N). Restart through the PreySense/OpenCore boot entry to load the new table.

If the current ACPI table is already loaded but Windows still has an older PreySense WMI definition, the elevated app detects the full method-signature mismatch and rebuilds that definition automatically.

The GPU baseline comes from NVIDIA's default power-management limit, not its minimum constraint or instantaneous power draw. On the verified laptop it is **85 W**. GPU totals are displayed as `GPU 130W [Delta 30W · Dynamic Boost 15W]`. These are arithmetic references; actual power remains subject to the driver and firmware. The platform power offset is fixed at **255 W inside ACPI**, with no app control.

CPU voltage is the requested VID on supported Intel CPUs, not a measured motherboard Vcore reading. A displayed zero is the fallback when voltage cannot be read.

## Building

Prerequisite: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .github\publish.ps1
```

The output is `dist\PreySense.exe`.

## Disclaimer

PreySense changes fans, power limits, and firmware-visible system state. Wrong settings can cause throttling, shutdowns, or instability. Use at your own risk.
