# PreySense technical documentation

Product overview: [root README](../README.md).

| Document | Contents |
|---|---|
| [Driver tuning](driver_tuning.md) | PreySenseDriver enablement, calibration, Apply/Save/Reset, CPU/GPU write order |
| [Configuration](configuration_json.md) | `preysense.json` layout and persistence |
| [Telemetry](telemetry.md) | Overlay sampling, clocks, graphs, missing values |
| [Fans](fan_control.md) | Per-mode Auto / Max / Custom curves |
| [GPU switching](gpu_switching_and_dds.md) | NVIDIA Advanced Optimus and Acer BIOS mux |
| [WMI and services](hardware_wmi_and_services.md) | OEM mode, fan, battery, charging, services |
| [Keyboard and logo RGB](keyboard_rgb_ec.md) | HID lighting and software effects |
| [Display and color](display_and_color.md) | Refresh rate and auto-on-battery |
| [v3.0.2](releases/v3.0.2.md) · [v3.0.1](releases/v3.0.1.md) · [v3.0.0](releases/v3.0.0.md) | Release notes |

Helper scripts used by Settings live in [`scripts/`](scripts/).
