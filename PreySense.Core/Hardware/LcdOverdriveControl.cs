namespace PreySense.Helpers;

/// <summary>Acer's internal-panel setting; this protocol does not address external monitors.</summary>
internal sealed class LcdOverdriveControl(Func<string, ulong, (bool success, ulong output)> send)
{
    internal const string GetProfileMethod = "GetGamingProfile";
    internal const ulong Selection = 0x10;
    internal const ulong EnabledBit = 1ul << 48;

    public bool TryGet(out bool enabled)
    {
        enabled = false;
        var (success, output) = send(GetProfileMethod, 0);
        // Profile byte six is selector 6. Firmware can return FF (unsupported).
        // Accept only the two defined states, never infer support from method existence.
        ulong state = (output >> 48) & 0xFF;
        if (!success || state > 1) return false;
        enabled = state == 1;
        return true;
    }

    public bool Set(bool enabled)
    {
        if (!TryGet(out bool current)) return false;
        if (current == enabled) return true;
        var (success, _) = send(AcerWmi.GamingMethods.SetProfile,
            Selection | (enabled ? EnabledBit : 0));
        return success && TryGet(out bool actual) && actual == enabled;
    }
}
