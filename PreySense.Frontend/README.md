# PreySense Frontend

WPF shell (`assembly name PreySense`). Hardware logic belongs in `PreySense.Core`.

## Where changes belong

- `Views/`: window composition and event forwarding. Main, Lighting (`KeyboardWindow`), Overlay (`MetricsWindow`), Mode limits (`ModeSettingsWindow`), Platform Settings (`SettingsWindow`).
- `Controls/`: reusable rows and cards. Add a control when the same interaction appears twice.
- `ViewModels/MainViewModel.*.cs`: state and commands, split by feature (`PowerModes`, `Fans`, `Gpu`, `Lighting`, `Display`, `Session`, `Services`, `Theme`, `Tuning`).
- `Services/`: UI-facing adapters (`ProductionHardwareService`, tray, overlay bridge, startup task, keyboard hook).
- `Styles/Colors.xaml`: semantic color tokens. Views must not introduce theme colors directly.
- `Styles/Typography.xaml`, `Cards.xaml`, `Controls.xaml`, `Spacing.xaml`: reusable presentation.

## UI conventions

1. Use `SettingSwitchRow`, `SettingDropdownRow`, `SettingSliderRow`, `SettingActionRow`, `SettingValueRow`, and `SettingCard` before writing equivalent grids by hand.
2. Reference semantic resources with `DynamicResource` for theme-sensitive brushes.
3. Use `UiDebouncer` for slider or text-input hardware writes. Do not add one-off `DispatcherTimer` debounce implementations.
4. Keep hardware calls out of rendering and property getters.
5. Any timer, hook, event subscription, or native-backed service must have an explicit shutdown path.
6. Mode and lighting commits must not re-apply unrelated subsystems (no RGB rewrite on mode switch).

## Validation

```powershell
dotnet test PreySense.Tests\PreySense.Tests.csproj -c Release
.\test_tools\Run.ps1 -SmokeTest
```

Publish the running binary from the repo root with `.github\publish.ps1`
