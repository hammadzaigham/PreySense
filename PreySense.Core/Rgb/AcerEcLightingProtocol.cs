using System;
using System.Buffers.Binary;

namespace PreySense.Rgb;

/// <summary>Pure packet builders recovered from Acer EC lighting controllers. No hardware access.</summary>
public static class AcerEcLightingProtocol
{
    public const ulong LegacyLightbarLedCommand = 0xFFF5000000000010UL;
    public const uint LightbarCapabilityQuery = 0x10;

    public static byte[] CreateKeyboardMode(byte mode, byte speed, byte brightness, byte direction, byte red, byte green, byte blue)
        => CreateMode(mode, speed, brightness, direction, red, green, blue, 1);

    public static byte[] CreateLightbarMode(byte mode, byte speed, byte brightness, byte direction, byte red, byte green, byte blue)
        => CreateMode(mode, speed, brightness, direction, red, green, blue, 2);

    public static byte[] CreateKeyboardBrightness(byte brightness) => CreateKeyboardMode(0, 0, brightness, 0, 0, 0, 0);
    public static byte[] CreateLightbarBrightness(byte brightness) => CreateLightbarMode(0, 0, brightness, 0, 0, 0, 0);

    private static byte[] CreateMode(byte mode, byte speed, byte brightness, byte direction, byte red, byte green, byte blue, byte target)
    {
        // AcerECKeyboardController 0x63c0; AcerECLightbarController 0x61e0.
        return [mode, speed, brightness, 0, direction, red, green, blue, 3, target, 0, 0, 0, 0, 0, 0];
    }

    public static ulong CreateKeyboardZoneColor(int zeroBasedZone, byte red, byte green, byte blue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(zeroBasedZone);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(zeroBasedZone, 3);
        return (1UL << zeroBasedZone) | ((ulong)red << 8) | ((ulong)green << 16) | ((ulong)blue << 24);
    }

    /// <param name="sideSelector">Front=1, Left=2, Right=4, Rear=8.</param>
    public static ulong CreateLightbarColor(int sideSelector, int zeroBasedLed, byte red, byte green, byte blue)
    {
        SideIndex(sideSelector);
        ArgumentOutOfRangeException.ThrowIfNegative(zeroBasedLed);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(zeroBasedLed, 7);
        return ((ulong)sideSelector << 32) | (1UL << (40 + zeroBasedLed)) |
               ((ulong)red << 8) | ((ulong)green << 16) | ((ulong)blue << 24);
    }

    public static int GetLedCommandLength(int smbiosMajor, int smbiosMinor)
    {
        ValidateVersion(smbiosMajor, smbiosMinor);
        return Is291OrLater(smbiosMajor, smbiosMinor) ? 16 : smbiosMajor == 2 && smbiosMinor >= 86 ? 12 : 8;
    }

    /// <param name="rgbColors">Packed RGB values, 0x00BBGGRR; zero means off.</param>
    public static byte[] CreateKeyboardLedMask(int smbiosMajor, int smbiosMinor, ReadOnlySpan<uint> rgbColors)
    {
        if (rgbColors.Length > 4) throw new ArgumentOutOfRangeException(nameof(rgbColors));
        var packet = new byte[GetLedCommandLength(smbiosMajor, smbiosMinor)];
        packet[0] = 8;
        for (int i = 0; i < rgbColors.Length; i++)
            if (rgbColors[i] != 0) packet[5] |= (byte)(1 << i);
        return packet;
    }

    public static byte[] CreateLightbarLedMask(int smbiosMajor, int smbiosMinor, int sideSelector, ReadOnlySpan<uint> rgbColors)
    {
        int side = SideIndex(sideSelector);
        int length = GetLedCommandLength(smbiosMajor, smbiosMinor);
        int maxLeds = length == 16 ? 8 : 4;
        if (rgbColors.Length > maxLeds) throw new ArgumentOutOfRangeException(nameof(rgbColors));
        var packet = new byte[length];
        if (length == 8)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(packet, LegacyLightbarLedCommand);
            return packet;
        }
        packet[0] = 0x10;
        packet.AsSpan(6, length == 16 ? 8 : 4).Fill(0xFF);
        ushort mask = 0;
        for (int i = 0; i < rgbColors.Length; i++)
            if (rgbColors[i] != 0) mask |= (ushort)(1 << (2 * i));
        if (length == 16) BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6 + side * 2), mask);
        else packet[6 + side] = (byte)mask;
        return packet;
    }

    /// <summary>Counts present LEDs from GetGamingLED(gmInput=0x10). gmOutput is the raw WMI byte array, excluding gmReturn.</summary>
    public static int DecodeLightbarLedCount(int smbiosMajor, int smbiosMinor, int sideSelector, byte gmReturn, ReadOnlySpan<byte> gmOutput)
    {
        ValidateVersion(smbiosMajor, smbiosMinor);
        int side = SideIndex(sideSelector);
        if (gmReturn != 0) return 0;
        bool extended = Is291OrLater(smbiosMajor, smbiosMinor);
        int offset = 5 + side * (extended ? 2 : 1);
        int width = extended ? 2 : 1;
        if (gmOutput.Length < offset + width) throw new ArgumentException("Truncated GetGamingLED output.", nameof(gmOutput));
        int mask = extended ? BinaryPrimitives.ReadUInt16LittleEndian(gmOutput[offset..]) : gmOutput[offset];
        int count = 0;
        for (int i = 0; i < (extended ? 8 : 4); i++)
            if (((mask >> (2 * i)) & 3) != 3) count++;
        return count;
    }

    private static bool Is291OrLater(int major, int minor) => major > 2 || major == 2 && minor >= 91;
    private static void ValidateVersion(int major, int minor)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(major);
        ArgumentOutOfRangeException.ThrowIfNegative(minor);
    }
    private static int SideIndex(int selector) => selector switch
    {
        1 => 0, 2 => 1, 4 => 2, 8 => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(selector), "Use Front=1, Left=2, Right=4, or Rear=8.")
    };
}
