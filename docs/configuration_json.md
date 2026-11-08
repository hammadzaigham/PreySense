# PreySense Configuration (`preysense.json`)

PreySense persists user settings and custom hardware tuning profiles to an editable, human-readable JSON configuration file named `preysense.json` stored strictly in the current working directory (alongside the executable in deployment).

All PreySense application settings and state — including session preferences, fan curves, lighting, language/theme, battery care, Acer service debloat preference, CPU/GPU tuning profiles, calibrated factory defaults, and overlay layout — persist exclusively in this JSON store. Windows registry is used only for OS/hardware control (EDID/DISPLAY, SCM service Start/ImagePath), not PreySense app preferences.

---

## Storage & Reliability Characteristics

1. **Storage Location**:
   - `preysense.json` in the active working directory / executable directory.
   - AppData or hidden directory fallbacks are explicitly disabled to guarantee portability and discoverability.
2. **Atomic Writes**:
   - Updates are written to a temporary file (`preysense.json.<guid>.tmp`) in the same directory and committed via atomic replace/move.
   - Prevents partial or corrupt files on sudden system power-off or crash.
3. **No Automatic Backups (`.bak`)**:
   - No `.bak` files are created or maintained.
4. **Clean Initialization**:
   - When `preysense.json` does not exist, documented defaults are used directly without legacy registry imports.
5. **Partial Updates**:
   - Unrelated sections are preserved when modifying a specific setting (e.g., toggling dark mode will not alter custom fan curves or power limits).
6. **Validation & Errors**:
   - Out-of-range values or malformed JSON produce explicit error notifications without silently corrupting settings.
7. **Read-at-Startup / Explicit Reload**:
   - Settings are loaded when PreySense starts or when the user explicitly triggers a profile load/reload. Editing the file does not silently issue uncommanded hardware writes.
   - **Apply** writes to hardware; **Save** persists to `preysense.json`; **Reset** restores documented factory defaults.

---

## Schema Overview

The schema is formally defined in [`preysense.schema.json`](../preysense.schema.json).

### Units & Value Conventions
- **Power Limits (PL1, PL2, GPU Target, Offsets)**: Watts (`W`)
- **Time Windows (`TauSeconds`)**: Seconds (`s`) [Steps: `0, 1, 2, 4, 8, 16, 32, 56, 64, 128, 256, 449 (infinite)`]
- **Percentages (`ScalePercent`, `BackgroundAlphaPercent`, Fan duty cycle)**: Integer Percent (`0-100%`)
- **Colors**: Hex color strings (e.g., `"#00E5FF"`)

---

## Top-Level Sections

### `General`
Controls application-level behavior:
- `Language` (string): UI culture code (`"en-US"`, `"zh-CN"`, `"hi-IN"`, `"es-ES"`, `"fr-FR"`). Default: `"en-US"`.
- `LightTheme` (boolean): `true` for light UI theme, `false` for dark theme. Default: `false`.
- `AcerServicesDebloated` (boolean): `true` if Acer OEM background services are stopped and disabled. Default: `false`.
- `HardwareTuningEnabled` (boolean): enables hardware tuning controls. Default: `false`.

### `Display`
Controls panel response and eye-care modes:
- `LcdOverdrive` (boolean): LCD 3ms panel overdrive toggle. Default: `false`.
- `BlueLightShieldMode` (integer): `0` = Off, `1` = Low Reduction, `2` = Medium Reduction. Default: `0`.
- `AutoRefreshRate` (boolean): Automatically switch display to 60Hz when running on battery. Default: `false`.

### `Battery`
Manages battery care and charging policies:
- `ChargeLimitEnabled` (boolean): Acer 80% maximum charge care limit toggle. Default: `false`.
- `UsbChargingPercent` (integer): Cutoff battery percentage for USB charging while laptop is sleeping/off (`0`, `10`, `20`, `30`). Default: `0`.
- `BatteryEnergySaver` (boolean): Automatically engage Windows Energy Saver when on battery. Default: `false`.
- `GpuBatteryAuto` (boolean): Automatically set GPU mode to Eco when disconnecting AC. Default: `false`.

### `Session`
Power mode selection and automated switching rules:
- `ActivePowerMode` (integer): `0` = Quiet, `1` = Balanced, `4` = Performance, `5` = Turbo, `6` = Eco. Default: `1`.
- `AcPowerMode` (integer): Preferred mode when connected to AC power. Default: `1`.
- `BatteryPowerMode` (integer): Preferred mode when running on battery. Default: `6` (Eco).
- `ExtendedCpuBoost` (boolean): Extended CPU boost state toggle. Default: `false`.
- `GpuModePreference` (integer): `0` = Integrated, `1` = Dynamic/Optimus, `2` = Discrete. Default: `1`.

### `Tuning`
Per-mode hardware tuning profiles (`Quiet`, `Eco`, `Balanced`, `Performance`, `Turbo`). Numeric tuning values and CPU boost are nullable: unset controls stay blank and are not replaced with preset values. Only explicit Save creates a user profile. Incomplete profiles leave Acer stock mode selection active.

`FactoryDefaults` is a separate map of the five driver-read mode profiles. Missing defaults are captured at startup; calibration does not populate user profiles.

Root-level Tuning fields:
- `GpuBasePowerWatts` (int or null): Hardware default GPU power limit in Watts from NVML or factory calibration (`1` - `999W`). Null until captured once via `GpuPowerBaseline`; subsequent saves keep the first valid value.
- `CpuBoostMode` (int): Global/extended CPU boost mode (`0` - `6`). Default: `1`.
- `ApplyProchotOffset` / `ProchotOffsetCelsius` / factory PROCHOT fields: optional PROCHOT offset controls.

Per-mode profile fields:
- `CpuPl1Watts` (int): Sustained CPU power limit (`5` - `300W`).
- `CpuPl2Watts` (int): Short-term burst CPU power limit (`5` - `300W`).
- `TauSeconds` (int): Burst duration window (`0` - `449s`).
- `CpuBoostMode` (int): Per-mode Windows CPU boost (`0`–`6`). Preferred over legacy `CpuBoost`.
- `CpuBoost` (boolean): Legacy on/off boost flag; normalized to `CpuBoostMode` on load.
- `GpuWatts` (int): Total target GPU power limit (`1` - `999W`).
- `GpuDeltaWatts` (int): NVPCF base power offset in Watts (`0` - `255W`).
- `GpuDynamicBoostWatts` (int): NVPCF dynamic boost offset in Watts (`0` - `255W`).
- `ApplyCpuLimits` (boolean): Apply CPU limits when switching to this mode. Default: `false`.
- `ApplyGpuLimits` (boolean): Apply GPU limits when switching to this mode. Default: `false`.
- `ApplyFanCurve` (boolean): Apply custom fan curve when switching to this mode. Default: `false`.
- `FanRampUp` (int): Fan speed ramp-up delay in seconds (`0` - `10s`).

### `Fans`
Per-mode fan curve definitions (`Quiet`, `Eco`, `Balanced`, `Performance`, `Turbo`):
- `ControlMode` (int): `0` = Auto, `1` = Max, `2` = Custom.
- `RampUpSeconds` (int): Fan ramp-up smoothing window (`0` - `10s`).
- `CpuCurve` (array of 10 integers): Fan duty cycle percentages (`0` - `100%`) for 10 temperature steps.
- `GpuCurve` (array of 10 integers): Fan duty cycle percentages (`0` - `100%`) for 10 temperature steps.

### `Lighting`
Keyboard and cover RGB lighting:
- `Keyboard.Brightness` (int, `0` - `100`)
- `Keyboard.Speed` (int, `0` - `100`)
- `Keyboard.BacklightTimeoutSeconds` (int, `0` - `120`)
- `Keyboard.SelectedColorHex` (string, e.g. `"#00E5FF"`)
- `Keyboard.SelectedZone` (int, `1` - `4`)
- `Keyboard.SyncZones` (boolean)
- `Keyboard.Zone1ColorHex` ... `Zone4ColorHex` (strings)
- `Keyboard.EffectTag` (string, e.g. `"HW:0"`)
- `Keyboard.PresetTheme` (string, e.g. `"Custom"`)
- `Cover.Enabled` (boolean)
- `Cover.Mode` (int)
- `Cover.ColorHex` (string)
- `Cover.Brightness` (int, `0` - `100`)
- `Cover.Speed` (int, `0` - `100`)
- `Cover.SyncWithKeyboard` (boolean)

### `Overlay`
On-Screen Display layout and metrics:
- `Enabled` (boolean): Master overlay toggle.
- `ShowFps` (boolean): Toggle FPS counter.
- `ScalePercent` (int, `50` - `200`): Display HUD scale.
- `BackgroundAlphaPercent` (int, `0` - `100`): HUD background opacity.
- `TextAlphaPercent` (int, `0` - `100`): Text opacity.
- `PositionHorizontal` (`"Left"`, `"Center"`, `"Right"`)
- `PositionVertical` (`"Top"`, `"Center"`, `"Bottom"`)
- `Anchor` (int): Coordinate anchor (`-1` for preset).
- `OffsetX` / `OffsetY` (int): Pixel distance from display edge.
- `Slots` (array of objects): Dynamic list of overlay metric slots:
  - `CpuMetric` (string): Metric key displayed for CPU row (or `"None"`).
  - `GpuMetric` (string): Metric key displayed for GPU row (or `"None"`).
  - `ShowGraph` (boolean): Whether graph visualization is enabled for this slot.
