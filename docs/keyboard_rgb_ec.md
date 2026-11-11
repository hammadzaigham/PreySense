# Keyboard and cover lighting

The existing four-zone keyboard and cover editor uses **ENEK5130** HID. Additional detected devices use verified Acer HID targets, Acer/Darfon/Sunrex USB protocols, firmware-declared EC lighting, or the named pipe of an already-running Acer Lighting Service. PreySense does not launch OpenRGB or send AcerService TCP commands. See [device support and the per-key editor](lighting_devices.md).

---

## Applying changes

The editor captures lighting values on the UI dispatcher. HID writes, effect transitions and JSON saves run on one background worker. Rapid edits replace the pending snapshot while preserving whether keyboard or cover changes still need applying; they do not queue a long series of obsolete writes. Software-effect cover frames use the same worker and do not save the JSON on every frame. Hardware errors go to `debug.log`.

Static colors and their final brightness are applied together. Equal RGB values in all four zones use one all-zones HID report; mixed colors retain individual zone reports. A software animation stops after an unexpected frame error rather than retrying it every 100 ms.

Shutdown drains the final lighting save and waits for active writes before closing the device. This wait is limited to shutdown; interactive handlers do not wait synchronously for the hardware.

## Device

| | |
| :--- | :--- |
| Hardware | `HID\ENEK5130` (also seen as `VID_048D&PID_C985`) |
| Report | Feature report **`0xA4`**, **11 bytes** |
| Keyboard target | `0x21` |
| Cover logo target | `0x83` |
| Zone masks | `0x01`, `0x02`, `0x04`, `0x08` (all zones `0x0F`) |

`USB\VID_1025&PID_174B` report `0xA0` is the generic Acer EC channel. PreySense does **not** send RGB on `0xA0`.

Client: `PreySense.Core/Hardware/EnekRgbClient.cs`. `AcerCoverLogoHidClient` is a thin wrapper around `SetLogoLighting`.

### Keyboard packet

| Byte | Field |
| :---: | :--- |
| 0 | Report ID `0xA4` |
| 1 | Target `0x21` |
| 2 | Effect opcode |
| 3 | Brightness 0–100 |
| 4 | Speed |
| 5 | Direction |
| 6–8 | R, G, B |
| 9 | Zone mask |
| 10 | `0x00` |

Identical zone colors are sent once with mask `0x0F`. Mixed colors are four reports.

### Cover logo packet

Same layout with target `0x83`. Logo effect opcodes: static `0x02`, breathing `0x04`, neon `0x05`. Off is brightness 0 / mode 0.

---

## Hardware keyboard effects

Run on the controller. PreySense sends one report and stops.

| UI index | Name | Opcode |
| :---: | :--- | :---: |
| 0 | Static (4-zone) | `0x02` |
| 1 | Breathing | `0x04` |
| 2 | Neon | `0x05` |
| 3 | Wave | `0x07` |
| 4 | Ripple | `0x07` + direction |
| 5 | Zoom | `0x09` |
| 6 | Snake / Meteor | `0x0A` |
| 7 | Disco / Twinkling | `0x0B` |

The EC does not expose live per-zone color while Wave, Ripple, Zoom, Snake, or Disco are running.

---

## Software animations

`CustomLightingEffectsEngine` (~35 fps) paints four `Color`s and calls `SetZoneColors`. Live effects: Rainbow Cycle, Police Strobe, Fire Ember, Matrix Cascade, Cylon Scanner, Aurora Drift, Heartbeat, Lightning Storm, Starlight, Neon Pulse, Plasma Orb, Glitch Storm, Bio Hazard, Solar Flare. Retired enum slots (CPU Heatmap, Battery Fuel Gauge, Ocean Tide, Ice Crystal) remain for stable indexes but are not offered in the UI.

Idle timeout: if keyboard backlight timeout is > 0, the engine stops HID writes after that many seconds without input so the EC idle timer can actually expire (continuous HID frames would keep the backlight awake).

---

## Cover logo modes

The lid LED only implements Off, Static, Breathing, Neon. It cannot play Wave or the software animations.

---

## Sync With Keyboard

`IsCoverSyncWithKeyboard`:

| Keyboard effect | Logo / Mode Key |
| :--- | :--- |
| Static | Average of the four zone RGB, **static** |
| Software | Average of the four zone RGB (live frame), **static** |
| Breathing | Selected keyboard color, **breathing** |
| Neon | Selected keyboard color, **neon** |
| Other hardware (Wave, Ripple, …) | Selected keyboard color, **static** |

Average source (Static / software only):

- Software engine running → last 4-zone frame
- Otherwise pending zone preset or stored zone hex colors

Logo HID is throttled to 250 ms during software animations.

Brightness and speed follow the keyboard sliders while sync is on.

---

## Timeout and power

Keyboard backlight timeout is written through the same lighting stack (`SetKbdBacklightTimeoutSeconds`). Cover power is logo mode 0.

If `EnekRgbClient.IsAvailable` is false, lighting calls are no-ops.
