# Acer WMI and OEM services

Modes, fans, sensors, battery, USB charging, and overdrive use `root\WMI`. Verified EC lighting also uses Acer gaming WMI methods when firmware and method schemas confirm support.

There is no AcerService TCP client in PreySense. Additional lighting controllers can use the local named pipe of an already-running Acer Lighting Service. Native HID/USB and EC transports work independently; discovery does not start or re-enable OEM services. See [lighting devices](lighting_devices.md).

---

## Classes

| Class | ACPI | Used for |
| :--- | :--- | :--- |
| `AcerGamingFunction` | `\_SB.WMID` / `WMBH` | Mode, fans, sensors, overdrive, BIOS GPU mux (reboot), boot sound |
| `BatteryControl` | `\_SB.AMW0` | 80% charge limit |
| `APGeAction` | `WMAA` | USB power-off charging, some LED timeout |

Namespace is always `root\WMI`.

---

## Power mode

Acer `AcerHardwareService` / `AcerSysHardwareService` use the same WMI getters and setters on every model (`kSvcCmdGetOperationMode` / `kSvcCmdSetOperationMode`). There is no alternate `GetCurrentPowerMode` path: both call `GetGamingMiscSetting` / `SetGamingMiscSetting` with index `0x0B`. `GetGamingProfile` is a different configuration API and does not report the live operating mode.

```text
SetGamingMiscSetting( 0x0B | (mode << 8) )
GetGamingMiscSetting( 0x0B )  →  status in bits 0..7, mode in bits 8..15
```

| Mode | Byte |
| :--- | :---: |
| Quiet | `0x00` |
| Balanced | `0x01` |
| Performance | `0x04` |
| Turbo | `0x05` |
| Eco | `0x06` |

Some firmwares accept Set but refuse Get (or return an unknown mode byte). PreySense caches the last known Quiet/Balanced/Performance/Turbo/Eco value after a successful set or read, and GPU profile apply tolerates a missing readback so wattage / ERM4 apply is not blocked.

After a successful set, PreySense applies a Windows overlay scheme:

| Modes | GUID |
| :--- | :--- |
| Quiet, Eco | `961cc777-2547-4f9d-8174-7d86181b8a7a` (Efficiency) |
| Balanced | all-zero (no overlay) |
| Performance, Turbo | `ded574b5-45a0-4f42-8737-46345c09c238` (Performance) |

---

## Fans

`SetGamingFanBehavior`:

| UI | Payload |
| :--- | :--- |
| Auto | `0x410009` |
| Max | `0x820009` |
| Custom | `0xC30009` |

Custom duty: `SetGamingFanSpeed` with

```text
gmInput = ((percent * 25600 / 100) & 0xFF00) + fanId
```

`fanId` 1 = CPU, 4 = GPU. Get speed: same method, result / 256.

Identical custom PWM is not rewritten on a timer. Repeating the same duty made the EC restart the cycle and hiss.

---

## Sensors (`GetGamingSysInfo`)

| Id | Reading |
| :---: | :--- |
| `0x01` | CPU °C |
| `0x02` | CPU fan RPM |
| `0x0A` | GPU °C |
| `0x06` | GPU fan RPM |

---

## Battery charge limit

`BatteryControl.SetBatteryLimit`: status **1** = 80% cap, **0** = 100%. Preference stored as `Battery.ChargeLimitEnabled` in `preysense.json`.

---

## GPU mux

Optimus / Automatic / Advanced Optimus use NVIDIA DDS only. Acer `SetGamingMiscSetting(((mode + 1) << 8) | 0x02)` stages the BIOS mux for **dGPU Only** and does not switch the live panel. See [GPU switching](gpu_switching_and_dds.md).

---

## Mode key

Chassis Mode/Turbo is not a WMI *set*. PreySense listens on:

1. `APGeEvent` / `AcerGenericEvent` (category `0x07`)
2. HID `ACPI\1025174B` / `HID\1025174B` report `04 85 FF` (non-keyboard collections)
3. Low-level keyboard hook scan `0x85` (and related chassis scans)

---

## OEM services PreySense can stop

Settings **Acer Services** toggles:

- `AcerSysHardwareService`
- `AcerHardwareService`
- `AcerLightingService`
- `AcerServiceSvc`
- `ASMSvc`

PreySense does not need them. Disable also kills leftover Acer/Predator Sense processes so they cannot respawn the services.

PredatorSense itself, if left running, will fight over mode and fans. Turn it off.

---
