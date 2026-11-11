using System;

namespace PreySense.Core.Hardware;

/// <summary>
/// Shared settle window after Acer SetGamingMiscSetting(0x0B) mode changes.
/// Mode switching uses 2s; RGB restoration has its own 1.55s delay.
/// </summary>
public static class AcerOperatingModeTiming
{
    public const int TransitionMilliseconds = 2000;

    public static TimeSpan TransitionDelay { get; } = TimeSpan.FromMilliseconds(TransitionMilliseconds);
}
