# AGENTS.md

Repository guidance for AI coding agents working in `PreySense`.

## Hard Rules

### No iGPU-Only Mode
- **Do NOT add, re-enable, or restore an iGPU-only GPU mode** (such as disabling or powering off the discrete NVIDIA GPU).
- PreySense intentionally supports only:
  - **NVIDIA Advanced Optimus (DDS)** via `PreySense.Core/Gpu/NvidiaDdsService.cs` (`DdsDisplayMode`, reboot-free):
    - `Optimus` (`0`): iGPU drives the display while the dGPU remains available on demand.
    - `Automatic` (`1`): Automatic Dynamic Display Switch.
    - `NvidiaGpuOnly` (`2`): dGPU drives the display directly.
  - **BIOS MUX switching** via `AcerWmi.GpuMux` (requires restart, managed in `PreySense.Frontend/ViewModels/MainViewModel.Gpu.cs`): `Automatic` (Optimus) and Discrete/dGPU.

### Build & Test Cadence
- **Do NOT run `dotnet build` or `dotnet test` after every single edit.**
- Batch edits first, use targeted `--filter` test runs only when needed, and reserve a full solution build/test for the end of a multi-file task or when explicitly requested.

## Project Layout

- `PreySense.Core/` — Hardware abstraction, ACPI/WMI, telemetry, power modes, fans, lighting, and GPU control (`PreySense.Core/Gpu/`).
- `PreySense.Frontend/` — WPF application shell (assembly `PreySense`): views, view models, reusable controls, styles, and UI services.
- `PreySense.Overlay/` — Metrics overlay window and rendering.
- `PreySense.Tests/` — Automated test suite.
- `test_tools/` — Smoke test and verification scripts.

## Build, Test & Publish

```powershell
# Run full solution tests
dotnet test PreySense.slnx -c Release

# Run test project directly
dotnet test PreySense.Tests\PreySense.Tests.csproj -c Release

# Run smoke test
.\test_tools\Run.ps1 -SmokeTest

# Publish running binary from repo root
.github\publish.ps1
```

## UI Conventions

Follow `PreySense.Frontend/README.md` for all frontend changes:
- Keep hardware logic in `PreySense.Core`; keep hardware calls out of rendering and property getters.
- Reuse `SettingSwitchRow`, `SettingDropdownRow`, `SettingSliderRow`, `SettingActionRow`, `SettingValueRow`, and `SettingCard`.
- Use `DynamicResource` with `Styles/Colors.xaml` semantic tokens instead of hardcoding theme colors.
- Use `UiDebouncer` for slider or text-input hardware writes (no one-off `DispatcherTimer` debouncers).
- Ensure timers, hooks, event subscriptions, and native-backed services have explicit shutdown paths.
- Do not re-apply unrelated subsystems during mode or lighting commits.
