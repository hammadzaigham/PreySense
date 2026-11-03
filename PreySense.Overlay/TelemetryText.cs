namespace PreySense.Overlay;

public static class TelemetryText
{
    public static string Format(double? value, string unit = "", int decimals = 0, bool allowZero = true)
    {
        if (!value.HasValue || !double.IsFinite(value.Value) || value < 0 || (!allowZero && value == 0)) return "N/A";
        return value.Value.ToString($"F{decimals}") + unit;
    }
}
