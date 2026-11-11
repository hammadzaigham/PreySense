using System;
using System.Buffers.Binary;

namespace PreySense.Rgb;

public sealed record AcerEcLightingCapabilities(int Major, int Minor, bool Keyboard, bool Lightbars, int KeyboardZoneCount = 0)
{
    /// <summary>Reads Acer type172 formatted bytes from a raw SMBIOS table stream.</summary>
    public static AcerEcLightingCapabilities? FromSmbios(ReadOnlySpan<byte> tables)
    {
        int position = 0;
        while (position + 4 <= tables.Length)
        {
            int length = tables[position + 1];
            if (length < 4 || position + length > tables.Length) return null;
            if (tables[position] == 172)
            {
                if (length < 6) return null;
                var entry = tables.Slice(position, length);
                bool ecKeyboard = false, rgbKeyboard = false, bars = false;
                int keyboardZoneCount = 0;
                for (int offset = 6; offset + 3 <= entry.Length; offset += 3)
                {
                    int value = BinaryPrimitives.ReadUInt16LittleEndian(entry[(offset + 1)..]);
                    switch (entry[offset])
                    {
                        case 8: ecKeyboard = value is 1 or 3 or 4; keyboardZoneCount = ecKeyboard ? value : 0; break;
                        case 10: rgbKeyboard = value == 2; break;
                        case 23: bars = value == 1; break;
                    }
                }
                return new(entry[4], entry[5], ecKeyboard && rgbKeyboard, bars, ecKeyboard && rgbKeyboard ? keyboardZoneCount : 0);
            }
            position += length;
            while (position + 1 < tables.Length && (tables[position] != 0 || tables[position + 1] != 0)) position++;
            if (position + 1 >= tables.Length) return null;
            position += 2;
        }
        return null;
    }
}
