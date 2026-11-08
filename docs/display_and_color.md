# Display refresh rate and color

---

## Refresh rate

Hybrid graphics expose the panel through a changing `\\.\DISPLAYn` name. PreySense finds the device with `DISPLAY_DEVICE_PRIMARY_DEVICE` and:

1. Enumerates modes with `EnumDisplaySettings`
2. Applies the matching width/height/frequency with `ChangeDisplaySettingsEx(..., CDS_UPDATEREGISTRY)`
3. Retries without `CDS_UPDATEREGISTRY` if the registry write is rejected

**Auto:** highest listed rate on AC, **60 Hz** on battery. Manual: any rate the panel reported. Preference persisted as `Display.AutoRefreshRate` in `preysense.json`.

---

## LCD overdrive

`AcerGamingFunction.SetGamingProfile`:

- On: `0x1000000000010`
- Off: `0x10`

This is firmware overdrive on the panel (short gray-to-gray pulses). Settings switch; preference stored as `Display.LcdOverdrive` in `preysense.json`.

---

## BlueLight Shield

Not Acer WMI. PreySense programs the adapter gamma LUT with `SetDeviceGammaRamp`.

Each of 256 entries starts as `i * 257`. Green and blue are scaled; red is left full.

| Mode | Name | Blue | Green |
| :---: | :--- | :---: | :---: |
| 0 | Off | 1.00 | 1.00 |
| 1 | Low | 0.82 | 0.96 |
| 2 | High | 0.64 | 0.92 |

---

## Brightness keys

Fn illumination keys adjust **keyboard RGB** by ±25%, not LCD backlight. HID reports `04 7D FF` / `04 7C FF` and related WMI events.
