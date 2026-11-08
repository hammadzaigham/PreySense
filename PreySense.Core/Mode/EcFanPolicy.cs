namespace PreySense.Mode
{
    /// <summary>
    /// Custom-mode duties the PHN16 EC keeps on <c>EFN1</c>/<c>EFN2</c>.
    /// WMI <c>SetGamingFanSpeed</c> is 0–100 and 0 RPM is a valid stopped state.
    /// SSD2 <c>ART0</c> SEN4/SEN5 trips are DPTF sensor maxima on ETP1/ETP2, not a
    /// custom-curve floor on junction temp — applying them made 0% until 60°C
    /// impossible.
    /// </summary>
    public static class EcFanPolicy
    {
        public const int MinDutyPercent = 0;
        public const int MaxDutyPercent = 100;

        public static int FloorPercent(float temperatureCelsius)
        {
            _ = temperatureCelsius;
            return MinDutyPercent;
        }

        public static int ClampDuty(int percent) =>
            Math.Clamp(percent, MinDutyPercent, MaxDutyPercent);

        public static int ClampDuty(int percent, float temperatureCelsius) =>
            Math.Clamp(percent, FloorPercent(temperatureCelsius), MaxDutyPercent);

        public static byte ClampDuty(byte percent) =>
            (byte)ClampDuty((int)percent);

        public static byte ClampDuty(byte percent, float temperatureCelsius) =>
            (byte)ClampDuty((int)percent, temperatureCelsius);

        public static float ClampDuty(float percent, float temperatureCelsius) =>
            Math.Clamp(percent, FloorPercent(temperatureCelsius), MaxDutyPercent);
    }
}
