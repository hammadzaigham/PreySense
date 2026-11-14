using System;
using System.Collections.Generic;

namespace PreySense.Rgb;

public sealed record AcerHidLightingBaseInfo(byte Target, byte Version, byte MaximumSpeed, int LedCount, uint EffectMask);
public sealed record AcerHidLightingMode(byte Value, string Name);

/// <summary>KYD100 serialization follows the OEM field order, with descriptor-derived widths.</summary>
public static class AcerHidLightingProtocol
{
    /// <summary>AcerECKeyboardController FUN_180008250 maps service direction values to KYD/EC values.</summary>
    public static uint DirectionFlags(byte mode, bool hasDirectionField) => !hasDirectionField ? 0u : mode switch
    {
        7 => 0x06u, // Color Wave: left/right and up/down.
        8 => 0x02u, // Shifting: left/right.
        _ => 0u
    };

    public static byte NativeDirection(int value) => value switch
    {
        0 => 2, 1 => 1, 2 => 3, 3 => 4, 4 => 5, 5 => 6,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    public static byte[] CreateUpdateReport(int reportLength, byte target, byte mode, byte speed, byte direction,
        byte brightness, byte red, byte green, byte blue, ushort zone, IReadOnlyDictionary<ushort, int> fieldWidths)
    {
        if (reportLength is < 2 or > 4096) throw new ArgumentOutOfRangeException(nameof(reportLength));
        var report = new byte[reportLength];
        report[0] = 0xA4;
        report[1] = target;
        int offset = 2;
        Write(0x41, mode); Write(0x43, brightness); Write(0x42, speed); Write(0x44, direction);
        Write(0x45, red); Write(0x46, green); Write(0x47, blue); Write(0x48, zone);
        return report;

        void Write(ushort usage, uint value)
        {
            if (!fieldWidths.TryGetValue(usage, out int width)) return;
            if (width is < 1 or > 4 || offset + width > report.Length)
                throw new ArgumentException("Invalid HID field widths or feature report length.", nameof(fieldWidths));
            if (width < 4 && value >= (1UL << (width * 8)))
                throw new ArgumentOutOfRangeException(nameof(value), "Value exceeds the HID descriptor field width.");
            for (int i = 0; i < width; i++) report[offset++] = (byte)(value >> (8 * i));
        }
    }

    public static AcerHidLightingBaseInfo? ParseBaseInfo(ReadOnlySpan<byte> report, byte target, IReadOnlyDictionary<ushort, int> fieldWidths)
    {
        if (report.Length < 2 || report[0] != 0xA3 || report[1] != target) return null;
        // Required fields: version, maximum speed, zone count, supported-effect mask.
        foreach (ushort usage in new ushort[] { 0x34, 0x32, 0x35, 0x31 })
            if (!fieldWidths.ContainsKey(usage)) return null;
        int offset = 2;
        uint version = 0, speed = 0, count = 0, effects = 0;
        foreach (ushort usage in new ushort[] { 0x34, 0x32, 0x35, 0x33, 0x31 })
        {
            if (!fieldWidths.TryGetValue(usage, out int width)) continue;
            if (width is < 1 or > 4 || offset + width > report.Length) return null;
            uint value = 0;
            for (int i = 0; i < width; i++) value |= (uint)report[offset++] << (8 * i);
            switch (usage) { case 0x34: version = value; break; case 0x32: speed = value; break; case 0x35: count = value; break; case 0x31: effects = value; break; }
        }
        if (version > 255 || speed > 255 || count is 0 or > 16) return null;
        return new(target, (byte)version, (byte)speed, (int)count, effects);
    }

    public static bool SupportsAnimationSpeed(byte target, int ledCount, byte maximumSpeed, byte mode) =>
        maximumSpeed > 0 && mode != 2
        && (ledCount > 1 || target is 0x64 or 0x65 || mode is not (4 or 5));

    public static IReadOnlyList<AcerHidLightingMode> GetModes(uint effectMask)
    {
        var modes = new List<AcerHidLightingMode>();
        foreach (var mode in new[] { new AcerHidLightingMode(2, "Static"), new(4, "Breathing"), new(5, "Neon"),
                     new(7, "Wave"), new(8, "Shifting"), new(9, "Zoom"), new(10, "Meteor"), new(11, "Twinkling") })
            if ((effectMask & (1u << (mode.Value - 1))) != 0) modes.Add(mode);
        return modes;
    }
}
