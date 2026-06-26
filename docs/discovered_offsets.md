# Discovered BIOS WMI Offsets

These offsets are historical reverse-engineering notes for Acer BIOS option buffers. PreySense does not currently write this buffer during normal app operation.

Keep this file as a compact reference only; platform controls used by the app are documented in [`acer_wmi_documentation.md`](acer_wmi_documentation.md).

| Offset | Hex | Setting | Known values |
| ---: | ---: | --- | --- |
| `14` | `0x000E` | Intel VTX | `0` disabled, `1` enabled |
| `15` | `0x000F` | Intel VTD | `0` disabled, `1` enabled |
| `22` | `0x0016` | Active Efficient Cores | `0` disabled, `1` enabled |
| `23` | `0x0017` | GNA Device | `0` disabled, `1` enabled |
| `80` | `0x0050` | Display mode | `1` auto select, `2` Optimus, `3` dGPU |
| `161` | `0x00A1` | Network Boot | `0` disabled, `1` enabled |
| `162` | `0x00A2` | Wake on LAN | `0` disabled, `1` enabled |
| `170` | `0x00AA` | Wake on USB while lid closed | `0` disabled, `1` enabled |
| `177` | `0x00B1` | USB/TBT Wake from S4 support | `0` disabled, `1` enabled |
| `301` | `0x012D` | F12 Boot Menu | `0` disabled, `1` enabled |
| `302` | `0x012E` | Function key behavior | `0` media key, `1` function key |
| `303` | `0x012F` | D2D Recovery | `0` disabled, `1` enabled |
| `305` | `0x0131` | Keyboard backlight timeout | `0` disabled, `1` enabled |
| `307` | `0x0133` | Fast Boot | `0` disabled, `1` enabled |
| `328` | `0x0148` | Post animation and sound | `0` disabled, `1` enabled |
| `329` | `0x0149` | Post animation sound | `0` mute, `1` unmute |
| `1934` | `0x078E` | Secure Boot | `0` disabled, `1` enabled |
