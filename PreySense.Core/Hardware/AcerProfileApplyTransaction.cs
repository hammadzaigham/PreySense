using System;
using System.Threading;

namespace PreySense.Core.Hardware;

/// <summary>Stages GPU tuning bytes while the selected profile is inactive, then activates once.</summary>
public sealed class AcerProfileApplyTransaction(IPhysicalMemoryByteAccess memory, IAcerPowerModeController modes)
{
    public readonly record struct Result(bool Success, string Status);

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
            Thread.Sleep(50);
        }
        return false;
    }

    private bool Switch(byte mode) => modes.SetAcerPowerMode(mode) && WaitForMode(mode);

    private bool Verify(ulong address, byte boost, byte delta, byte? platformOffset = null) =>
        memory.ReadPhysicalByte(address, out byte db) && db == boost &&
        memory.ReadPhysicalByte(address + 1, out byte ct) && ct == delta &&
        (!platformOffset.HasValue || memory.ReadPhysicalByte(address + 2, out byte cp) && cp == platformOffset.Value);

    public Result Apply(byte selected, int delta, int boost, byte? platformOffset = null)
    {
        if (delta is < 0 || delta > DriverFactoryCalibration.MaxGpuDeltaWatts || delta % 5 != 0
            || !DriverFactoryCalibration.IsLegalGpuDynamicBoost(boost))
            return new(false, "GPU offsets are out of range.");
        if (platformOffset is byte po && (po > 100 || po % 5 != 0))
            return new(false, "Platform power offset is out of range.");
        if (!AcerErm4Table.EnsureResolved(memory, out _))
            return new(false, "GPU profile table was not found on this laptop.");
        ulong address = ProfileAddress(selected);
        if (!modes.TryGetAcerPowerMode(out byte originalMode))
            return new(false, "Could not read the current Acer mode.");
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
            if (originalMode == selected)
            {
                if (!Switch(stagingMode))
                    throw new InvalidOperationException("Could not leave Acer mode to write GPU settings.");
            }

            if (!modes.TryGetAcerPowerMode(out byte before) || before == selected)
                throw new InvalidOperationException("Acer mode is still active; GPU settings were not written.");

            wrote = true;
            if (!memory.WritePhysicalByte(address, (byte)boost) ||
                !memory.WritePhysicalByte(address + 1, (byte)delta) ||
                (platformOffset.HasValue && !memory.WritePhysicalByte(address + 2, platformOffset.Value)) ||
                !Verify(address, (byte)boost, (byte)delta, platformOffset))
                throw new InvalidOperationException("GPU settings did not stick.");

            Thread.Sleep(2000);
            if (!Switch(selected))
                throw new InvalidOperationException("Could not switch back to the selected Acer mode.");
            Thread.Sleep(500);
            if (!WaitForMode(selected) || !Verify(address, (byte)boost, (byte)delta, platformOffset))
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
