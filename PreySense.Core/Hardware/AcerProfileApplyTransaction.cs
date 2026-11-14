using System;
using System.Threading;

namespace PreySense.Core.Hardware;

/// <summary>Stages GPU tuning bytes while the selected profile is inactive, then activates once.</summary>
public sealed class AcerProfileApplyTransaction(IPhysicalMemoryByteAccess memory, IAcerPowerModeController modes, Action<int>? delay = null)
{
    public readonly record struct Result(bool Success, string Status);

    private void Pause(int milliseconds)
    {
        if (delay is { } wait) wait(milliseconds);
        else Thread.Sleep(milliseconds);
    }

    public static ulong ProfileAddress(byte mode) => AcerErm4Table.ProfileAddress(mode);

    /// <summary>
    /// Stage via Performance (or Balanced when Performance is selected). Quiet↔Balanced is avoided:
    /// Acer often accepts Quiet→Balanced but then refuses Quiet re-entry within the apply window.
    /// </summary>
    public static byte StagingMode(byte selected) => selected == 0x04 ? (byte)0x01 : (byte)0x04;

    private bool WaitForMode(byte expected)
    {
        for (int i = 0; i < 20; i++)
        {
            if (modes.TryGetAcerPowerMode(out byte actual) && actual == expected) return true;
            Pause(50);
        }
        return false;
    }

    /// <summary>
    /// AcerHardwareService uses Get/SetGamingMiscSetting(0x0B) for every model — there is no
    /// alternate GetCurrentPowerMode. Some firmwares accept Set but refuse Get / return unknown
    /// bytes, so a successful Set is enough when readback never confirms.
    /// </summary>
    private bool Switch(byte mode)
    {
        if (!modes.SetAcerPowerMode(mode)) return false;
        if (WaitForMode(mode)) return true;
        Pause(200);
        return !modes.TryGetAcerPowerMode(out byte actual) || actual == mode;
    }

    private bool Verify(ulong address, byte boost, byte delta, byte? platformOffset = null) =>
        memory.ReadPhysicalByte(address, out byte db) && db == boost &&
        memory.ReadPhysicalByte(address + 1, out byte ct) && ct == delta &&
        (!platformOffset.HasValue || memory.ReadPhysicalByte(address + 2, out byte cp) && cp == platformOffset.Value);

    public Result Apply(byte selected, int delta, int boost, byte? platformOffset = null)
    {
        if (delta is < 0 || delta > DriverFactoryCalibration.MaxGpuDeltaWatts || delta % 5 != 0
            || !DriverFactoryCalibration.IsLegalGpuDynamicBoost(boost))
            return new(false, "GPU offsets are out of range.");
        if (platformOffset is byte po && po % 5 != 0)
            return new(false, "Platform power offset is out of range.");
        if (!AcerErm4Table.EnsureResolved(memory, out _))
            return new(false, "GPU profile table was not found on this laptop.");
        ulong address = ProfileAddress(selected);
        bool modeKnown = modes.TryGetAcerPowerMode(out byte originalMode);
        if (!modeKnown)
            originalMode = selected; // restore target if firmware never reports the live mode
        if (!memory.ReadPhysicalByte(address, out byte originalBoost) ||
            !memory.ReadPhysicalByte(address + 1, out byte originalDelta))
            return new(false, "Could not read the current GPU profile.");

        byte originalPlatform = 0;
        if (platformOffset.HasValue && !memory.ReadPhysicalByte(address + 2, out originalPlatform))
            return new(false, "Could not read the platform power offset.");

        byte stagingMode = StagingMode(selected);
        bool wrote = false;
        try
        {
            // Dropdown mode must be inactive while ERM4 DB/CT/CP are written.
            // Same as hardware → leave to staging then return. Different → write then switch.
            // Unknown live mode → always leave to staging first (safe when GetGamingMiscSetting fails).
            bool needsStagingSwitch = !modeKnown || originalMode == selected;
            if (needsStagingSwitch)
            {
                if (!Switch(stagingMode))
                    throw new InvalidOperationException("Could not leave Acer mode to write GPU settings.");
            }

            // After a successful leave, a failed Get must not block the write — same firmwares
            // that accept Set while Get fails / returns unknown (see Switch / WaitForMode).
            if (modes.TryGetAcerPowerMode(out byte before) && before == selected)
                throw new InvalidOperationException("Acer mode is still active; GPU settings were not written.");

            wrote = true;
            if (!memory.WritePhysicalByte(address, (byte)boost) ||
                !memory.WritePhysicalByte(address + 1, (byte)delta) ||
                (platformOffset.HasValue && !memory.WritePhysicalByte(address + 2, platformOffset.Value)) ||
                !Verify(address, (byte)boost, (byte)delta, platformOffset))
                throw new InvalidOperationException("GPU settings did not stick.");

            // Only a real intermediate mode switch needs an EC settle interval.
            // An already-inactive target can be activated immediately after staging its bytes.
            if (needsStagingSwitch) Pause(AcerOperatingModeTiming.TransitionMilliseconds);
            if (!Switch(selected))
                throw new InvalidOperationException("Could not switch back to the selected Acer mode.");
            Pause(500);
            bool modeSettled = WaitForMode(selected)
                || !modes.TryGetAcerPowerMode(out byte after)
                || after == selected;
            if (!modeSettled || !Verify(address, (byte)boost, (byte)delta, platformOffset))
                throw new InvalidOperationException("GPU settings changed while switching mode.");
            return new(true, "GPU settings applied.");
        }
        catch (Exception ex)
        {
            bool restored = true;
            if (wrote)
            {
                if (modes.TryGetAcerPowerMode(out byte current) && current == selected)
                    restored = Switch(stagingMode);
                if (restored)
                {
                    bool db = memory.WritePhysicalByte(address, originalBoost);
                    bool ct = memory.WritePhysicalByte(address + 1, originalDelta);
                    bool cp = !platformOffset.HasValue || memory.WritePhysicalByte(address + 2, originalPlatform);
                    restored = db && ct && cp && Verify(address, originalBoost, originalDelta, platformOffset.HasValue ? originalPlatform : null);
                }
            }
            bool modeRestored = Switch(originalMode);
            return new(false, restored && modeRestored
                ? $"{ex.Message} Previous GPU settings were restored."
                : $"{ex.Message} Could not restore previous GPU settings.");
        }
    }
}
