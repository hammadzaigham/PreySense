# Detected lighting devices

The Lighting window discovers controllers automatically when opened and when Windows reports HID connection or removal. There is no Refresh or Apply button. Edits are coalesced for 80 ms, sent on a background worker, and saved only after the transport accepts them. Different devices retain separate pending updates. Loading an editor or discovering a device never applies default colors.

## Editing

Supported per-key keyboards show a keyboard diagram built from their recovered OEM key names and wire indices. Click selects one key, Ctrl-click toggles keys, Shift-click selects a range in displayed keyboard order, and dragging selects a rectangle. Shift-drag or Ctrl-drag adds to the existing selection. Shift-arrow keys extend a selection. The centered color wheel changes all selected keys. Selection is indicated by outlines, with no selected-key label.

The diagram is a readable PC keyboard arrangement, not a chassis photograph or an assertion of exact physical geometry. Vendor-specific keys stay visible separately. Only named keys whose slots are actually transmitted by the recovered protocol are editable; unknown or untransmitted slots are not invented.

Single-color, individual key/LED colors, brightness, hardware effects, speed, and random colors appear according to the transport's supported modes. Four-zone-only controls are hidden on detected per-key keyboards. Existing four-zone presets/software animations and cover synchronization remain available on their original transport. Independently editing a cover logo disables keyboard-to-cover synchronization so it cannot overwrite the new color.

## Transports

| Transport | Confirmed capability boundary |
| --- | --- |
| Acer KYD100 HID / ENEK | A1 advertises targets; descriptor-sized A2/A3 queries confirm count and effect mask. Only validated A4 layouts and targets are writable. Supports advertised keyboard, general/front/left/right/rear bars, cover/base/general logo, and mode/turbo-key targets. |
| Acer USB | Recovered product/layout `04F2:0117`, usage page FF02, validated report lengths; per-key colors and ordinary hardware effects. |
| Darfon USB | Recovered product/layout `0D62:BA51`, usage page FF01, validated report lengths; per-key colors and ordinary hardware effects. |
| Sunrex USB | Explicit product/layout tables embedded in `OemUsbKeyboardLayouts.json`; validated FF02 reports, named keys, per-key colors, and ordinary hardware effects. Slots outside the OEM transmitted region are excluded. |
| Acer EC / WMI | SMBIOS type 172 declares RGB keyboard and zone count; method signatures must match. Optional bars require firmware declarations and successful `GetGamingLED` counts. There is no PHN16S-71/BIOS-1.28 restriction. |
| Acer Lighting Service | Uses `AcerLightingService_NamedPipe` only when the service is already running. Device information, modes and exact LED count are queried before writes. Unknown physical keys retain LED numbers. The app does not change service startup settings. |

Static EC bar colors are addressed per side. The OEM hardware-animation command affects the entire EC bar subsystem, so independent per-bar animations are not advertised. Native HID modes are limited by each target's effect mask. Native USB brightness percentages map to the OEM 0–50 range; speed bounds and packet checksums follow the recovered controller family.

ENEK and descriptor-driven HID transactions share one gate so capability target selection cannot interleave with existing lighting writes. No periodic hardware retries were added. A failed generic update reports an error and does not save a successful profile.

## Settings and startup

Explicit device edits are stored under `Lighting.Devices` in the existing `preysense.json`, keyed by detected stable device ID. Each entry stores its mode, brightness/speed, base color, and optional individual color map. Unused individual colors are omitted from the snapshot. Device discovery does not populate default profiles. Saved entries are restored only when their device is detected; failures are written to the existing `debug.log`.

## Evidence and limits

Static analysis covered the installed `AcerLightingService.exe`, `AcerECKeyboardController.dll`, `AcerECLightbarController.dll`, `AcerUSBController.dll`, `DarfonUSBController.dll`, and `SunrexUSBKeyboard.dll`. Ghidra decompilation, Capstone disassembly, extracted mappings, hashes, and reproducible scripts are retained locally under `outputs/acer-lighting/`. Decompiled text is not the original source, and unidentified code is not claimed recovered.

Read-only live discovery on the PHN16S-71 confirmed a four-zone keyboard, a one-LED cover logo, and a one-LED mode key; no bar was advertised. These probes sent no lighting-color updates. Per-key USB behavior and other chassis surfaces are verified against recovered code and packet/layout tests, not physically bench-tested on every model. OEM operating-mode/MAG paths and older EC-only logo presence rules that were not fully verified are intentionally not exposed as direct controls. No OEM binaries are bundled with PreySense.
