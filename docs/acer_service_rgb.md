# AcerService RGB

PreySense controls Predator keyboard and logo lighting through AcerService on TCP `127.0.0.1:46933`.

Broader Acer platform control is documented in [`acer_wmi_documentation.md`](acer_wmi_documentation.md).

## Code path

```text
RgbForm / MainForm
  -> WmiController.ApplyLightingMode / ApplyZoneLighting / SetLogoLighting
  -> AcerServiceClient
  -> AcerService packet 100, Function="LIGHTING"
  -> AcerLightingService / Acer EC or HID lighting device
```

`AcerLightingService` must stay enabled. Disabling it breaks RGB and can break other AcerService-routed device commands.

## Protocol

| Item | Value |
| --- | --- |
| Host | `127.0.0.1` |
| Port | `46933` |
| Packet magic | `ACER` |
| Set packet | `100` |
| Query packet | `20` |
| Function | `LIGHTING` |
| Encryption | Optional AES-ECB when `HKCU\Software\Acer\XSense\AESkey` exists |

## Supported keyboard effects

| UI mode | Acer effect | `subindex."1"` | `subindex."2"` |
| --- | --- | --- | --- |
| Static | `STATIC` | `STATIC` | `STATIC` |
| Breathing | `BREATHING` | `BREATHING` | `BREATHING` |
| Neon | `NEON` | `NEON` | `NEON` |
| Wave | `WAVE` | `WAVE` | `NEON` |
| Ripple | `SHIFTING` | `SHIFTING` | `STATIC` |
| Zoom | `ZOOM` | `ZOOM` | `STATIC` |
| Snake | `METEOR` | `METEOR` | `STATIC` |
| Disco | `TWINKLING` | `TWINKLING` | `BREATHING` |

The Predator Sense UI may display renamed labels for some effects, but the wire protocol uses the effect names above.

## Payload notes

Dynamic keyboard effects use `device: 0`, `duration: 3`, `colortype: 1`, brightness `1..5`, speed `1..5`, and an optional wave direction field. PreySense now exposes and passes through raw direction values `1..4` for hardware probing, though only `1` and `2` are currently documented by existing notes.

Per-zone static uses `device: 1` and an `LEDs` array with four zone colors.

Do not send `UserDynamicEffect` for normal keyboard modes. It can override the selected effect and make unrelated modes render the same.

## State query

To inspect current committed lighting state, use packet `20`:

```json
{"Function":"LIGHTING"}
```

PreySense uses this only as a service-state query; saved user RGB settings live in `HKCU\SOFTWARE\PreySense`.
