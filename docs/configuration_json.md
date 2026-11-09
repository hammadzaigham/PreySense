# PreySense settings (`preysense.json`)

`ConfigurationService` reads and writes `preysense.json` in the current working directory. In a normal deployment this is alongside the executable. The file stores typed settings, custom tuning values and captured factory defaults. Current application preferences use this JSON store rather than PreySense registry keys. Windows-owned state remains separate: Run on Startup is a scheduled task, and display/driver/service integrations still access operating-system settings. This file is not an export of that Windows state.

## Format and compatibility

Files must explicitly contain `"SchemaVersion": 2`. Version 1 is not migrated. Older files, unsupported future versions and files without a version are rejected without rewriting or deleting them. A missing file starts with fresh version 2 defaults in memory; loading alone does not create the file. An older build may not understand version 2 names, so do not use the new file with an older executable.

Saved files use two-space indentation, stable section/property ordering and consistent line breaks. CPU settings appear together, followed by GPU settings; captured factory profiles are ordered Quiet, Eco, Balanced, Performance, Turbo. Null tuning values stay null and explicit zero values stay zero. Unknown properties at the root, Tuning and individual tuning-profile levels are preserved during supported-version saves.

Current serialized names are `CpuTurboTimeLimitSeconds`, `GpuPowerOffsetWatts`, `GpuNominalPowerWatts`, `BackgroundOpacityPercent` and `TextOpacityPercent`. These names describe time, offsets and opacity directly. Old spellings are not imported or renamed.

Writes go through a temporary file in the same directory and replace/move into place only after validation. Malformed JSON and invalid or unsupported settings report errors; the service does not search AppData for another settings file. There are no automatic `.bak` files. Back up a file yourself before manual edits. Editing the file does not directly invoke hardware writes; the app loads settings at startup or explicit reload.

## Main sections

| Section | Purpose |
| --- | --- |
| `General` | Language (`en-US`, `zh-CN`, `hi-IN`, `es-ES`, `fr-FR`), theme, Acer-service preference and tuning enable flag. |
| `Display` | Automatic refresh-rate preference. |
| `Battery` | Charge-limit, USB power-off charging and Windows Energy Saver preferences. |
| `Session` | Desired power modes for the session, AC and battery, plus GPU-mode preference. Mode codes: 0 Quiet, 1 Balanced, 4 Performance, 5 Turbo, 6 Eco. |
| `Tuning` | Optional global controls, per-mode user profiles and separately captured factory defaults. |
| `Fans` | Per-mode Auto/Max/Custom fan control, stage values and ramp-up delay. |
| `Lighting` | Keyboard and cover effects, colors, brightness, speed and timeout preferences. |
| `Overlay` | Visibility, FPS, scale, opacity, position and selected metric slots. |

A preference is a desired setting, not confirmation of a live hardware reading. The application verifies hardware operations separately.

## USB power-off charging

`Battery.UsbPowerOffChargingCutoffPercent` defaults to `0` (disabled). Choose `10`, `20` or `30` to keep the supported charging USB port powered while the laptop is shut down, until the laptop battery reaches that percentage. This can charge a phone or accessory without running the laptop, but uses laptop battery power. It does not change how the laptop battery itself charges. Firmware and a compatible USB charging port are required.

The Settings slider uses 0 for Disabled and 10/20/30% for the cutoff. It applies the setting through Acer APGE WMI and verifies firmware readback before saving it. The saved preference is reapplied on startup/resume; unsupported or unconfirmed operations report N/A rather than claiming success. The dropdown displays the saved preference, not a live hardware reading. Apply or readback failures also show an inline `USB charging: N/A.` message. A successful verified write clears that message. The disabled flag is accepted only within recognized firmware response payloads; unknown packets remain unavailable.

## Tuning values

The five user profiles are `Quiet`, `Eco`, `Balanced`, `Performance` and `Turbo`. Unset numeric values remain null. The editor may display captured factory values for an unset draft; only Save persists a custom value. Apply writes the selected controls to hardware, while Save also stores them. Incomplete enabled controls cannot be applied.

| Profile field | Meaning |
| --- | --- |
| `ApplyCpuLimits` | Whether this profile's CPU limits should be applied. |
| `CpuPl1Watts` | Sustained CPU power selection (5–300 W). |
| `CpuPl2Watts` | Burst CPU power selection (5–300 W, at least PL1). |
| `CpuTurboTimeLimitSeconds` | Turbo time-window selection; **449 means Infinite**, not literal seconds. |
| `CpuBoostMode` | Windows PERFBOOSTMODE, 0–6. |
| `CpuBoost` | Retained legacy on/off field within supported-version profiles; `CpuBoostMode` takes priority. |
| `ApplyGpuLimits` | Whether this profile's GPU offsets should be applied. |
| `GpuPowerOffsetWatts` | Acer GPU base-power offset (CT), 0–100 W in steps of 5. |
| `GpuDynamicBoostWatts` | Separate Dynamic Boost offset, 0–25 W in steps of 5. |
| `PlatformPowerOffsetWatts` | Separate Acer platform-power offset (CP), 0–100 W in steps of 5. |
| `GpuNominalPowerWatts` | Arithmetic reference from the verified GPU baseline plus GPU offsets. **Not measured draw or an enforced power-limit reading.** Null when the baseline is unavailable. |

`FactoryDefaults` is a separate map of driver-read mode records. Missing records are captured by the app's calibration path; they do not populate saved user profiles. CPU-only capture can be complete without GPU ERM4 fields; GPU tuning requires the GPU fields. `FactoryStaticCpuPl1Watts` and `FactoryStaticCpuPl2Watts` store global MSR limits used by the existing tuning policy. `GpuBasePowerWatts` is verified baseline metadata, not a live board-power sample.

Global PROCHOT fields are `ApplyProchotOffset`, `ProchotOffsetCelsius`, `FactoryProchotOffsetCelsius`, `FactoryProchotOffsetLocked` and `FactoryTjMaxCelsius`. Offsets are 0–63 °C below Tj-Max. They are not per-mode values. An unreadable value is null; a locked or unavailable control is disabled. CPU boost is per-mode only (`CpuBoostMode` on each profile).

Example of an unset user profile (an excerpt, not a complete configuration):

```json
{
  "ApplyCpuLimits": false,
  "CpuPl1Watts": null,
  "CpuPl2Watts": null,
  "CpuTurboTimeLimitSeconds": null,
  "CpuBoostMode": null,
  "CpuBoost": null,
  "ApplyGpuLimits": false,
  "GpuPowerOffsetWatts": null,
  "GpuDynamicBoostWatts": null,
  "PlatformPowerOffsetWatts": null,
  "GpuNominalPowerWatts": null
}
```

## Overlay layout

`ScalePercent` is 50–200. `BackgroundOpacityPercent` and `TextOpacityPercent` are 0–100, where 0 is transparent. Position presets use Left/Center/Right and Top/Center/Bottom. Each entry in `Slots` chooses `CpuMetric`, `GpuMetric` and `ShowGraph`. `None` requests no sensor for that column. Turning a graph off does not disable its numeric metric's collection. Missing sensor readings display N/A.


Advanced appearance settings live only in `Overlay` in JSON; there are no corresponding UI controls. Close PreySense before editing the file, then restart it. Colors use `#RRGGBB` and opacity stays controlled by the existing opacity fields.

| Field | Default | Meaning |
|---|---|---|
| `BackgroundColorHex` | `#000000` | Background color. |
| `CpuTextColorHex` / `GpuTextColorHex` | `#FFFFFF` / `#FFFFFF` | CPU and GPU row colors. |
| `CpuGraphColorHex` / `GpuGraphColorHex` | `#2C7CB4` / `#3AAA54` | CPU and GPU graph colors, including their translucent fill. |
| `CornerRadiusPixels` | `6` | Rounded corners, 0–16 base overlay pixels; scales with the overlay. Set 0 for square corners. |
| `GraphLineWidthPixels` | `1.5` | Graph stroke width, 0.5–4 screen pixels. |
| `ShowBackgroundShading` | `false` | Adds a subtle top highlight and bottom shading. |

The overlay uses per-pixel transparency; pixels outside the rounded boundary remain transparent, including when dragging. Invalid colors or ranges are rejected rather than silently replaced.

The accompanying `preysense.schema.json` lists supported fields, descriptive titles, units, nullability and validation ranges. It describes the canonical version 2 file format.
