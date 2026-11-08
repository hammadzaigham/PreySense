# PreySense technical documentation

Start with the [root README](../README.md) for product behavior. The current tuning implementation uses the bundled driver. OpenCore is not used in v3.

| Document | What it explains |
|---|---|
| [v3.0.0 release notes](releases/v3.0.0.md) | Simple upgrade guide and feature summary (uninstall previous version; no OpenCore). |
| [Driver tuning](driver_tuning.md) | First-enable calibration, nullable user profiles, JSON factory defaults, Apply/Save/Reset, and startup/mode restoration. |
| [CPU power limits](cpu_power_limits_driver.md) | Combined driver MSR/MMIO transactions, readback, and rollback. |
| [GPU power limits](gpu_power_limits_nvpcf.md) | Independent GPU offsets and inactive-profile staging. |
| [Configuration](configuration_json.md) | JSON storage, atomic persistence, schema, and remaining registry consumers. |
| [Telemetry](telemetry.md) | Selected sampling, CPU/GPU clock sources, graphs, and missing values. |
| [Fans](fan_control.md) | Per-mode fan curves and runtime control. |
| [GPU switching](gpu_switching_and_dds.md) | NVIDIA Advanced Optimus and Acer BIOS mux. |
| [WMI and services](hardware_wmi_and_services.md) | OEM mode, fan, battery, charging, and service controls. |
| [Keyboard and logo RGB](keyboard_rgb_ec.md) | HID lighting controls and software effects. |
| [Display and color](display_and_color.md) | Refresh rate, overdrive, and BlueLight Shield. |
| [BIOS offsets](bios_setup_var_offsets.md) | Hardware research reference. |
