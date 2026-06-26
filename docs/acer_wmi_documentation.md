# Acer Hardware Interface

This is the current PreySense reference for Acer/Predator hardware control. It describes the interfaces the app actively uses and the Acer services that must stay available.

## Code reference

All Acer WMI, AcerService packet IDs, service function names, sensor IDs, GPU mux values, and Windows power overlay GUIDs are defined in:

`app/Helpers/Hardware/AcerWmi.cs`

Use that file instead of scattering raw WMI class names or AcerService command strings through the codebase.

## Services

| Service/task | PreySense status | Why |
| --- | --- | --- |
| `AcerServiceSvc` | Keep enabled | Provides the AcerService TCP command/telemetry endpoints used for lighting, operating modes, fan control, sound mode, boot sound, LCD overdrive, GPU mux, and panel DFR. |
| `AcerLightingService` | Keep enabled | Routes RGB/lighting and several Acer device commands behind the AcerService stack. |
| `AcerQAAgentSvis` | Keep enabled | Backs Quick Access mode-key/current-mode state on `wss://localhost:5141`. |
| `AcerCCAgentSvis` | Disable | Not used by PreySense. |
| `AcerDIAgentSvis` | Disable | Not used by PreySense. |
| `ASMSvc` | Disable | Not used by PreySense. |
| `AcerDeviceEnablingServiceV2` | Disable | Not called by PreySense. GPU mode code uses local device/WMI paths. |
| `PredatorService` | Disable | Belongs to Predator Sense and can compete with PreySense for settings ownership. |
| `PredatorSenseLauncher` scheduled task | Disable | Prevents Predator Sense from relaunching while PreySense is running. |

The script `docs/scripts/Disable Acer Bloatware.bat` only disables the unused/conflicting items above. It intentionally leaves `AcerServiceSvc`, `AcerLightingService`, and `AcerQAAgentSvis` alone.

At app startup PreySense also closes known Predator Sense UI/launcher processes so the two apps do not fight over modes, fans, RGB, or GPU offsets during the same session.

## AcerService TCP

Host: `127.0.0.1`

| Port | Purpose |
| --- | --- |
| `46933` | Command socket |
| `46753` | Telemetry socket |

Packets are framed as:

```text
0..3   ASCII "ACER"
4..7   uint32 little-endian packet id
8..n   JSON payload, optionally AES-ECB encrypted when HKCU\Software\Acer\XSense\AESkey exists
```

Packet IDs:

| ID | Name | Use |
| --- | --- | --- |
| `0` | Initialization | Version/handshake commands |
| `20` | Get updated data | Query current state |
| `100` | Set device data | Apply device settings |

PreySense sends these set functions:

`LIGHTING`, `OPERATING_MODE`, `FAN_CONTROL`, `SOUND_MODE`, `WIN_KEY`, `STICKY_KEY`, `BOOT_SOUND`, `LCD_OVERDRIVE`, `GPU_MODE`, `PANEL_DFR_MODE`

PreySense queries these functions:

`AC_STATUS`, `ADAPTOR_STATUS`, `BATTERY_BOOST`, `OPERATING_MODE`, `FAN_CONTROL`, `LIGHTING`, `SOUND_MODE`

## WMI classes

Namespace: `root\WMI`

| Class | Use |
| --- | --- |
| `AcerGamingFunction` | Fallback power mode, fan behavior/speed, sensor reads, boot sound fallback, LCD overdrive fallback |
| `APGeAction` | LED timeout and USB charging controls |
| `BatteryControl` | Battery charge limit |
| `WmiMonitorBrightnessMethods` | Set screen brightness |
| `WmiMonitorBrightness` | Read screen brightness |

Primary WMI methods are defined in `AcerWmi.GamingMethods`, `AcerWmi.ApgeMethods`, and `AcerWmi.BatteryMethods`.

## Performance modes

| Name | Acer mode |
| --- | --- |
| Silent | `0x00` |
| Balanced | `0x01` |
| Performance | `0x04` |
| Turbo | `0x05` |
| Eco | `0x06` |

Mode changes are applied through AcerService `OPERATING_MODE` first, with `AcerGamingFunction.SetGamingMiscSetting` as fallback. On battery, PreySense switches to Eco (`0x06`) and tracks the AC mode separately.

Per-mode CPU limits, GPU offsets, Windows power overlay, and fan curve apply flags are stored under:

`HKCU\SOFTWARE\PreySense\Profiles\<ModeName>`

## GPU offsets

GPU overclocking no longer edits DriverStore `.ini` files. PreySense applies GPU offsets through NVAPI `SetPerformanceStates20`.

Observed editable NVAPI settings:

| PState | Setting | Range |
| --- | --- | --- |
| `P0_3DPerformance` | Graphics delta | `-1000..+1000 MHz` |
| `P0_3DPerformance` | Memory delta | `-1000..+3000 MHz` |

Turbo defaults remain `+100 MHz` core and `+200 MHz` memory. Other modes default to `0/0`, but Eco performance mode can now store and apply GPU offsets just like the other modes.

Applying GPU offsets requires the per-mode `ApplyGpuLimits` flag to be enabled. Slider edits clear the apply checkbox to show that the new value has not been applied yet.

## GPU mux modes

AcerService `GPU_MODE` values:

| UI mode | Acer value | Notes |
| --- | --- | --- |
| Endurance / iGPU only | `2` | Hybrid mux stays active; Windows dGPU device is disabled |
| Standard / hybrid | `2` | Optimus/hybrid path |
| Ultimate / discrete | `1` | Requires reboot |

This is separate from the Eco performance mode (`OPERATING_MODE = 0x06`).

NVAPI GPU offsets are skipped while GPU mode is Endurance/iGPU only, even if the active performance profile has `ApplyGpuLimits` enabled.

## Quick Access mode key

The physical mode key/current-mode state is read through Acer Quick Access over:

`wss://localhost:5141`

This is why `AcerQAAgentSvis` stays enabled. AcerService `GET_UPDATED_DATA OPERATING_MODE` can be stale after Predator Sense is killed, so Quick Access is preferred for startup/current mode state when available.
