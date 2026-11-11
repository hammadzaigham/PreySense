using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;

namespace PreySense.Rgb;

/// <summary>Client for the installed Acer lighting service. Never starts the service.</summary>
public sealed class AcerLightingPipeClient
{
    public const string PipeName = "AcerLightingService_NamedPipe";
    internal const int MaximumMessageBytes = 16 * 1024 * 1024;
    private readonly TimeSpan _timeout;

    public AcerLightingPipeClient(TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(3);
        if (_timeout <= TimeSpan.Zero || _timeout.TotalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    public async Task<int> GetDeviceCountAsync(CancellationToken cancellationToken = default)
    {
        var response = await RequestAsync(0, [], cancellationToken).ConfigureAwait(false);
        if (response.Length != 1 || response[0].Length != 4)
            throw new InvalidDataException("Unexpected Acer device count response.");
        int count = BinaryPrimitives.ReadInt32LittleEndian(response[0]);
        if (count < 0) throw new IOException("Acer lighting controllers are not ready.");
        return count;
    }

    /// <summary>Returns OEM packed color values without assuming their channel order.</summary>
    public async Task<uint[]> GetDeviceLedsAsync(int deviceIndex, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(deviceIndex);
        byte[] index = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(index, deviceIndex);
        var response = await RequestAsync(6, [index], cancellationToken).ConfigureAwait(false);
        if (response.Length == 1 && ReadInt32(response[0]) < 0)
            throw new IOException("Acer lighting controller LEDs are unavailable.");
        if (response.Length != 2 || response[0].Length != 4)
            throw new InvalidDataException("Unexpected Acer LED response.");
        int count = BinaryPrimitives.ReadInt32LittleEndian(response[0]);
        if (count < 0 || count > MaximumMessageBytes / 4 || response[1].Length != (long)count * 4)
            throw new InvalidDataException("Invalid Acer LED count or payload length.");
        uint[] colors = new uint[count];
        for (int i = 0; i < count; i++)
            colors[i] = BinaryPrimitives.ReadUInt32LittleEndian(response[1].AsSpan(i * 4, 4));
        return colors;
    }

    public async Task<AcerLightingDeviceInfo> GetDeviceInfoAsync(int deviceIndex, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(deviceIndex);
        var response = await RequestAsync(1, [Int32Block(deviceIndex)], cancellationToken).ConfigureAwait(false);
        return DecodeDeviceInfo(deviceIndex, response);
    }

    /// <summary>Uses the returned mode Value, RGB packed as 0xBBGGRR, and OEM color type in the high byte.</summary>
    public async Task SetDeviceModeAsync(int deviceIndex, int modeValue, uint packedColor, int speed,
        int brightness, int direction, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(deviceIndex);
        var response = await RequestAsync(2, [Int32Block(deviceIndex), Int32Block(modeValue),
            Int32Block(unchecked((int)packedColor)), Int32Block(speed), Int32Block(brightness), Int32Block(direction)],
            cancellationToken).ConfigureAwait(false);
        RequireWriteSuccess(response);
    }

    /// <summary>Colors use 0x00BBGGRR, matching the OEM RGBX byte stream; the high byte is ignored.</summary>
    public async Task SetDeviceLedsAsync(int deviceIndex, IReadOnlyList<uint> packedColors,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(deviceIndex);
        ArgumentNullException.ThrowIfNull(packedColors);
        if (packedColors.Count > (MaximumMessageBytes - 23) / 4)
            throw new ArgumentOutOfRangeException(nameof(packedColors));
        byte[] colors = new byte[packedColors.Count * 4];
        for (int i = 0; i < packedColors.Count; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(colors.AsSpan(i * 4), packedColors[i] & 0xffffff);
        var response = await RequestAsync(3, [Int32Block(deviceIndex), Int32Block(packedColors.Count), colors],
            cancellationToken).ConfigureAwait(false);
        RequireWriteSuccess(response);
    }

    private static byte[] Int32Block(int value)
    {
        byte[] block = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(block, value);
        return block;
    }

    private static int ReadInt32(byte[] block)
    {
        if (block.Length != 4) throw new InvalidDataException("Expected an Acer 32-bit value.");
        return BinaryPrimitives.ReadInt32LittleEndian(block);
    }

    private static void RequireWriteSuccess(byte[][] response)
    {
        if (response.Length != 1) throw new InvalidDataException("Unexpected Acer write response.");
        if (ReadInt32(response[0]) != 0) throw new IOException("Acer lighting service rejected the update.");
    }

    internal static AcerLightingDeviceInfo DecodeDeviceInfo(int index, byte[][] blocks)
    {
        if (blocks.Length == 1 && ReadInt32(blocks[0]) < 0)
            throw new IOException("Acer lighting controller is unavailable.");
        if (blocks.Length is not (6 or 7) || blocks[1].Length != 50)
            throw new InvalidDataException("Unexpected Acer device info response.");
        int nameLength = ReadInt32(blocks[0]);
        if (nameLength is < 0 or > 50) throw new InvalidDataException("Invalid Acer device name length.");
        int count = ReadInt32(blocks[3]);
        if (count < 0 || count > MaximumMessageBytes / 108 ||
            (count == 0 ? blocks.Length != 6 : blocks.Length != 7 || blocks[6].Length != (long)count * 108))
            throw new InvalidDataException("Invalid Acer mode count or payload length.");
        var modes = new AcerLightingModeInfo[count];
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> mode = blocks[6].AsSpan(i * 108, 108);
            int modeNameLength = BinaryPrimitives.ReadInt32LittleEndian(mode.Slice(52));
            if (modeNameLength is < 0 or > 50) throw new InvalidDataException("Invalid Acer mode name length.");
            int[] fields = new int[13];
            for (int field = 0; field < fields.Length; field++)
                fields[field] = BinaryPrimitives.ReadInt32LittleEndian(mode.Slice(56 + field * 4));
            modes[i] = new(Encoding.UTF8.GetString(mode.Slice(0, modeNameLength)), fields[0], unchecked((uint)fields[1]),
                fields[2], fields[3], fields[4], fields[5], fields[6], fields[7], fields[8], fields[9], fields[10],
                fields[11], unchecked((uint)fields[12]));
        }
        return new(index, Encoding.UTF8.GetString(blocks[1], 0, nameLength), ReadInt32(blocks[2]),
            ReadInt32(blocks[4]), ReadInt32(blocks[5]), modes);
    }

    private async Task<byte[][]> RequestAsync(ushort command, byte[][] arguments, CancellationToken cancellationToken)
    {
        byte[] request = EncodeRequest(command, arguments);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(deadline.Token).ConfigureAwait(false);
        pipe.ReadMode = PipeTransmissionMode.Message;
        await pipe.WriteAsync(request, deadline.Token).ConfigureAwait(false);
        using var message = new MemoryStream();
        byte[] chunk = new byte[4096];
        do
        {
            int read = await pipe.ReadAsync(chunk, deadline.Token).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException("Acer lighting service closed the pipe.");
            if (message.Length + read > MaximumMessageBytes)
                throw new InvalidDataException("Acer lighting response exceeds the message limit.");
            message.Write(chunk, 0, read);
        } while (!pipe.IsMessageComplete);
        return DecodeResponse(message.ToArray());
    }

    internal static byte[] EncodeRequest(ushort command, byte[][] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Length > byte.MaxValue) throw new ArgumentOutOfRangeException(nameof(arguments));
        long length = 3;
        foreach (byte[] argument in arguments)
        {
            ArgumentNullException.ThrowIfNull(argument);
            length += 4L + argument.Length;
        }
        if (length > MaximumMessageBytes) throw new ArgumentOutOfRangeException(nameof(arguments));
        byte[] message = new byte[(int)length];
        BinaryPrimitives.WriteUInt16LittleEndian(message, command);
        message[2] = (byte)arguments.Length;
        int offset = 3;
        foreach (byte[] argument in arguments)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(offset), (uint)argument.Length);
            argument.CopyTo(message, offset + 4);
            offset += 4 + argument.Length;
        }
        return message;
    }

    internal static byte[][] DecodeResponse(ReadOnlySpan<byte> message)
    {
        if (message.IsEmpty || message.Length > MaximumMessageBytes)
            throw new InvalidDataException("Invalid Acer response length.");
        byte[][] arguments = new byte[message[0]][];
        int offset = 1;
        for (int i = 0; i < arguments.Length; i++)
        {
            if (message.Length - offset < 4) throw new InvalidDataException("Truncated Acer response length.");
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(message.Slice(offset, 4));
            offset += 4;
            if (length > message.Length - offset) throw new InvalidDataException("Truncated Acer response argument.");
            arguments[i] = message.Slice(offset, (int)length).ToArray();
            offset += (int)length;
        }
        if (offset != message.Length) throw new InvalidDataException("Trailing Acer response data.");
        return arguments;
    }
}

/// <summary>Capabilities returned by the OEM service, without inferred keyboard or zone geometry.</summary>
public sealed record AcerLightingDeviceInfo(int Index, string Name, int DeviceType, int ActiveMode,
    int Brightness, IReadOnlyList<AcerLightingModeInfo> Modes);

public sealed record AcerLightingModeInfo(string Name, int Value, uint Flags, int SpeedMinimum, int SpeedMaximum,
    int BrightnessMinimum, int BrightnessMaximum, int ColorsMinimum, int ColorsMaximum, int Speed,
    int Brightness, int Direction, int ColorMode, uint PackedColor);
