namespace PreySense.Core.Hardware;

/// <summary>Intel TCC/PROCHOT offset control; preserves all other temperature-target fields.</summary>
public sealed class CpuProchotController(ICpuDriverBackend backend)
{
    public const uint TemperatureTargetMsr = 0x1A2;
    public const uint PlatformInfoMsr = 0xCE;
    public const ulong OffsetMask = 0x3FUL << 24;
    public const ulong LockBit = 1UL << 31;
    private static readonly object Gate = new();
    public readonly record struct Snapshot(int OffsetCelsius, int TjMaxCelsius, bool Locked)
    {
        public int ActivationTemperatureCelsius => TjMaxCelsius - OffsetCelsius;
    }

    public Snapshot? Read()
    {
        lock (Gate)
        {
            if (!backend.IsInitialized || !backend.ReadMsr(TemperatureTargetMsr, out ulong raw)) return null;
            int target = (int)((raw >> 16) & 0xFF);
            int offset = (int)((raw & OffsetMask) >> 24);
            return target is >= 40 and <= 125 && offset < target && (raw & (1UL << 30)) == 0
                ? new(offset, target, (raw & LockBit) != 0) : null;
        }
    }

    public bool CanSetOffset(int offset, out string status)
    {
        lock (Gate) return Prepare(offset, out _, out status);
    }

    private bool Prepare(int offset, out ulong raw, out string status)
    {
        raw = 0;
        status = string.Empty;
        if (offset is < 0 or > 63) { status = "PROCHOT offset must be between 0 and 63°C."; return false; }
        if (!backend.IsInitialized || !backend.ReadMsr(PlatformInfoMsr, out ulong platform) || (platform & (1UL << 30)) == 0)
        { status = "This CPU does not report programmable PROCHOT offset support."; return false; }
        if (!backend.ReadMsr(TemperatureTargetMsr, out raw))
        { status = "The CPU temperature target could not be read."; return false; }
        int target = (int)((raw >> 16) & 0xFF);
        if ((raw & (1UL << 30)) != 0)
        { status = "The CPU reports an offset outside the supported 0–63°C range."; return false; }
        if (target is < 40 or > 125 || offset >= target)
        { status = "The CPU temperature target is unavailable or incompatible with this offset."; return false; }
        if ((raw & LockBit) != 0 && (int)((raw & OffsetMask) >> 24) != offset)
        { status = "The BIOS has locked the PROCHOT offset."; return false; }
        return true;
    }

    public bool TrySetOffset(int offset, out string status)
    {
        lock (Gate)
        {
            if (!Prepare(offset, out ulong original, out status)) return false;
            ulong desired = (original & ~OffsetMask) | ((ulong)offset << 24);
            if (desired == original) { status = $"PROCHOT offset verified: {offset}°C."; return true; }
            bool written = backend.WriteMsr(TemperatureTargetMsr, desired);
            if (written && backend.ReadMsr(TemperatureTargetMsr, out ulong actual) && actual == desired)
            {
                status = $"PROCHOT offset verified: {offset}°C (throttling at {(original >> 16 & 0xFF) - (ulong)offset}°C).";
                return true;
            }
            // A rejected write may still have changed the register. Restore just the original offset.
            bool restored = backend.ReadMsr(TemperatureTargetMsr, out ulong current)
                && ((current & OffsetMask) == (original & OffsetMask)
                    || (current & LockBit) == 0 && backend.WriteMsr(TemperatureTargetMsr,
                        (current & ~OffsetMask) | (original & OffsetMask)));
            restored = restored && backend.ReadMsr(TemperatureTargetMsr, out ulong rollback)
                && (rollback & OffsetMask) == (original & OffsetMask);
            status = $"PROCHOT offset write/readback failed. Original offset {(restored ? "restored" : "could not be restored")}.";
            return false;
        }
    }
}
