using System;
using System.Runtime.InteropServices;

namespace PreySense.Core.Hardware;

/// <summary>
/// Acer ERM4 profile table: "Predator" signature at base, mode DB/CT/CP at base+0xA4..C4.
/// Base differs by firmware (PHN16S-71: 0xFE800400, PHN16-71: 0xFE708400).
/// </summary>
public static class AcerErm4Table
{
    public const ulong Phn16s71Base = 0xFE800400UL;
    public const ulong Phn1671Base = 0xFE708400UL;
    public static readonly ulong[] CandidateBases = [Phn16s71Base, Phn1671Base];

    private static readonly object Gate = new();
    private static ulong? _resolvedBase;

    public static ulong? ResolvedBase
    {
        get { lock (Gate) return _resolvedBase; }
    }

    /// <summary>Resolved base, or PHN16S-71 default when discovery has not run yet (unit tests).</summary>
    public static ulong Base
    {
        get { lock (Gate) return _resolvedBase ?? Phn16s71Base; }
    }

    public static void Reset()
    {
        lock (Gate) _resolvedBase = null;
    }

    public static void SetBase(ulong baseAddress)
    {
        lock (Gate) _resolvedBase = baseAddress;
    }

    public static ulong ModeDbOffset(byte mode) => mode switch
    {
        0x00 => 0xA4, 0x01 => 0xAC, 0x04 => 0xB4,
        0x05 => 0xBC, 0x06 => 0xC4,
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    public static ulong ProfileAddress(byte mode) => Base + ModeDbOffset(mode);

    public static bool HasPredatorSignature(IPhysicalMemoryByteAccess memory, ulong baseAddress)
    {
        ReadOnlySpan<byte> signature = "Predator"u8;
        for (int i = 0; i < signature.Length; i++)
        {
            if (!memory.ReadPhysicalByte(baseAddress + (ulong)i, out byte value) || value != signature[i])
                return false;
        }
        return true;
    }

    /// <summary>Locate ERM4 via live DSDT, then candidate physical probes. Caches on success.</summary>
    public static bool TryResolve(IPhysicalMemoryByteAccess memory, out ulong baseAddress)
    {
        lock (Gate)
        {
            if (_resolvedBase is ulong cached)
            {
                baseAddress = cached;
                return true;
            }
        }

        if (TryReadErm4BaseFromDsdt(out ulong fromDsdt) && HasPredatorSignature(memory, fromDsdt))
        {
            SetBase(fromDsdt);
            baseAddress = fromDsdt;
            return true;
        }

        foreach (ulong candidate in CandidateBases)
        {
            if (!HasPredatorSignature(memory, candidate)) continue;
            SetBase(candidate);
            baseAddress = candidate;
            return true;
        }

        baseAddress = 0;
        return false;
    }

    public static bool EnsureResolved(IPhysicalMemoryByteAccess memory, out ulong baseAddress) =>
        TryResolve(memory, out baseAddress);

    internal static bool TryReadErm4BaseFromDsdt(out ulong baseAddress)
    {
        baseAddress = 0;
        byte[]? dsdt;
        try { dsdt = GetAcpiTable("DSDT"); }
        catch { return false; }
        if (dsdt is null || dsdt.Length < 16) return false;

        // DefOpRegion: ExtOp (5B) OpRegionOp (80) NameSeg "ERM4"
        ReadOnlySpan<byte> needle = [0x5B, 0x80, 0x45, 0x52, 0x4D, 0x34];
        for (int i = 0; i <= dsdt.Length - needle.Length - 2; i++)
        {
            if (!dsdt.AsSpan(i, needle.Length).SequenceEqual(needle)) continue;
            int p = i + needle.Length;
            if (p >= dsdt.Length) return false;
            byte regionSpace = dsdt[p++]; // 0 = SystemMemory
            if (regionSpace != 0) continue;
            if (!TryReadAmlInteger(dsdt, ref p, out ulong regionOffset)) continue;
            baseAddress = regionOffset;
            return true;
        }
        return false;
    }

    private static bool TryReadAmlInteger(byte[] data, ref int p, out ulong value)
    {
        value = 0;
        if (p >= data.Length) return false;
        byte op = data[p++];
        if (op <= 0x09) { value = op; return true; }
        return op switch
        {
            0x0A when p < data.Length => Read(data, ref p, 1, out value),
            0x0B when p + 1 < data.Length => Read(data, ref p, 2, out value),
            0x0C when p + 3 < data.Length => Read(data, ref p, 4, out value),
            0x0E when p + 7 < data.Length => Read(data, ref p, 8, out value),
            _ => false
        };

        static bool Read(byte[] buf, ref int index, int size, out ulong result)
        {
            result = 0;
            for (int i = 0; i < size; i++)
                result |= (ulong)buf[index++] << (8 * i);
            return true;
        }
    }

    private static byte[]? GetAcpiTable(string signature)
    {
        if (signature.Length != 4) return null;
        uint sig = (uint)(signature[0] | signature[1] << 8 | signature[2] << 16 | signature[3] << 24);
        uint size = GetSystemFirmwareTable(FirmwareAcpiTableProvider, sig, nint.Zero, 0);
        if (size == 0) return null;
        byte[] buffer = new byte[size];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            uint written = GetSystemFirmwareTable(FirmwareAcpiTableProvider, sig, handle.AddrOfPinnedObject(), size);
            return written == 0 ? null : buffer;
        }
        finally { handle.Free(); }
    }

    private const uint FirmwareAcpiTableProvider = 0x41435049; // 'ACPI'

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetSystemFirmwareTable(uint provider, uint tableId, nint buffer, uint bufferSize);
}
