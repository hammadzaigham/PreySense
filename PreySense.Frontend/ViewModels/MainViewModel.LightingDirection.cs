using PreySense.Rgb;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    private int? _keyboardLightingDirection;
    // Wave exposes left/right and up/down; EC metadata can also expose horizontal/vertical.
    public bool HasKeyboardLightingDirection => LightingEffectTag is "HW:3" or "HW:4" && KeyboardLightingDirections.Count > 0;
    public IReadOnlyList<LightingDirectionInfo> KeyboardLightingDirections
    {
        get
        {
            if (LightingEffectTag is not ("HW:3" or "HW:4")) return [];
            var keyboard = LightingDevices.FirstOrDefault(d => d.Kind == "Keyboard");
            if (keyboard is null) return LightingModeInfo.DirectionsForFlags(LightingEffectTag == "HW:3" ? 0x06u : 0x02u);
            int mode = keyboard.Id.StartsWith("acer-ec-", StringComparison.Ordinal) ?
                LightingEffectTag == "HW:3" ? 3 : 4 : LightingEffectTag == "HW:3" ? 7 : 8;
            return keyboard.Modes.FirstOrDefault(m => m.Value == mode)?.Directions ?? [];
        }
    }
    public int KeyboardLightingDirection
    {
        get => KeyboardLightingDirections.Any(d => d.Value == _keyboardLightingDirection) ? _keyboardLightingDirection!.Value : 1;
        set
        {
            if (!KeyboardLightingDirections.Any(d => d.Value == value)) return;
            if (SetProperty(ref _keyboardLightingDirection, (int?)value)) ScheduleKeyboardLightingCommit();
        }
    }

    private void NotifyKeyboardLightingDirection()
    {
        OnPropertyChanged(nameof(HasKeyboardLightingDirection));
        OnPropertyChanged(nameof(KeyboardLightingDirections));
        OnPropertyChanged(nameof(KeyboardLightingDirection));
    }
}
