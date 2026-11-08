namespace PreySense.Mode
{
    /// <summary>
    /// Maps Acer performance modes to the Windows overlay scheme PreySense applies
    /// on every mode switch. This is not a user-editable per-mode setting.
    /// </summary>
    public static class WindowsPowerPolicy
    {
        public const int OverlayEfficiency = 0;
        public const int OverlayBalanced = 1;
        public const int OverlayPerformance = 2;

        public static int OverlaySchemeForMode(byte acerMode)
        {
            return acerMode switch
            {
                0x00 or 0x06 => OverlayEfficiency,
                0x04 or 0x05 => OverlayPerformance,
                0x01 => OverlayBalanced,
                _ => throw new ArgumentOutOfRangeException(nameof(acerMode))
            };
        }
    }
}
