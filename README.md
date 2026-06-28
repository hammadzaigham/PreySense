# <img src="app/appicon.svg" alt="PreySense logo" width="42" height="42" align="left">&nbsp;PreySense

PreySense is a fork of G-Helper adapted for Acer Predator laptops. It is a lightweight Windows utility for controlling Acer laptop performance modes, fans, GPU behavior, RGB lighting, display options, battery limits, and a compact hardware overlay without running the full Predator Sense app.

This project is experimental and hardware-specific. It has been developed and tested on limited Acer Predator hardware, so compatibility with other Acer models is not guaranteed.

## Features

- Performance modes: Eco, Silent, Balanced, Performance, and Turbo.
- Profile-based settings saved per performance mode.
- CPU power limit controls.
- NVIDIA GPU overclock.
- Custom Fan speed control.
- Per-mode fan curves and saved fan settings.
- GPU mode controls for Endurance, Standard, and Ultimate. Auto GPU behavior on battery.
- Battery charge limit control.
- Auto display refresh-rate switching + LCD overdrive.
- Color profile handling for display refresh modes.
- Keyboard RGB and logo lighting control.
- Predator key support, including Predator key shortcuts for mode switching.
- Compact on-screen hardware overlay with CPU/GPU temperatures, fan RPM, power, RAM/VRAM, FPS, and power graphs.

## Requirements

- Windows 10 or Windows 11 x64.
- Acer Predator laptop with compatible Acer WMI and AcerService interfaces.
- Microsoft .NET 10 Windows Desktop Runtime x64.
- [PawnIO](https://pawnio.eu/) driver installed for CPU power limit access.
- Predator Sense installed for Acer Services.

## Download

Download the latest release build and run `PreySense.exe` as administrator.

## Building From Source

Install the .NET 10 SDK, then run:

```powershell
dotnet build app\PreySense.csproj
```

## Acer Documentation

- `docs/acer_wmi_documentation.md`
- `docs/acer_service_rgb.md`

## Registry State

User settings are stored under:

```text
HKCU\SOFTWARE\PreySense
```

## Contributing

Contributions are welcome, especially:

- Hardware reports for additional Acer Predator models.
- WMI/AcerService packet documentation.
- Safer fallbacks for unsupported hardware.
- UI polish and accessibility improvements.
- Bug fixes with clear reproduction steps.
- Documentation updates.

When contributing, keep changes focused and include the laptop model, BIOS version, Windows version, GPU mode, and Acer service state when reporting hardware behavior.

## Disclaimer

PreySense controls low-level laptop hardware behavior. Use it at your own risk. Incorrect power, fan, GPU, display, or firmware-adjacent settings may cause instability or unexpected behavior.
