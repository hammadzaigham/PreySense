# Display refresh rate and color

---

## Refresh rate

Hybrid graphics expose the panel through a changing `\\.\DISPLAYn` name. PreySense finds the device with `DISPLAY_DEVICE_PRIMARY_DEVICE` and:

1. Enumerates modes with `EnumDisplaySettings`
2. Applies the matching width/height/frequency with `ChangeDisplaySettingsEx(..., CDS_UPDATEREGISTRY)`
3. Retries without `CDS_UPDATEREGISTRY` if the registry write is rejected

**Auto:** highest listed rate on AC, **60 Hz** on battery. Manual: any rate the panel reported. Preference persisted as `Display.AutoRefreshRate` in `preysense.json`.

---

## Brightness keys

Fn illumination keys adjust **keyboard RGB** by ±25%, not LCD backlight. HID reports `04 7D FF` / `04 7C FF` and related WMI events.
