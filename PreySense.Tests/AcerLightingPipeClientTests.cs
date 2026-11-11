using PreySense.Rgb;

namespace PreySense.Tests;

[TestClass]
public class AcerLightingPipeClientTests
{
    [TestMethod]
    public void RequestsUseLittleEndianCommandAndLengthPrefixedArguments()
    {
        CollectionAssert.AreEqual(new byte[] { 0, 0, 0 }, AcerLightingPipeClient.EncodeRequest(0, []));
        CollectionAssert.AreEqual(new byte[] { 6, 0, 1, 4, 0, 0, 0, 2, 0, 0, 0 },
            AcerLightingPipeClient.EncodeRequest(6, [[2, 0, 0, 0]]));
        CollectionAssert.AreEqual(new byte[] { 0x34, 0x12, 2, 0, 0, 0, 0, 1, 0, 0, 0, 0xab },
            AcerLightingPipeClient.EncodeRequest(0x1234, [[], [0xab]]));
    }

    [TestMethod]
    public void ResponseHasNoEchoedCommandAndPreservesRawColorBytes()
    {
        var blocks = AcerLightingPipeClient.DecodeResponse([2, 4, 0, 0, 0, 1, 0, 0, 0, 4, 0, 0, 0, 0x11, 0x22, 0x33, 0]);
        Assert.HasCount(2, blocks);
        CollectionAssert.AreEqual(new byte[] { 1, 0, 0, 0 }, blocks[0]);
        CollectionAssert.AreEqual(new byte[] { 0x11, 0x22, 0x33, 0 }, blocks[1]);
    }

    [TestMethod]
    public void RejectsMalformedAndTruncatedResponses()
    {
        byte[][] malformed = [[], [1], [1, 4, 0, 0], [1, 4, 0, 0, 0, 1], [1, 0xff, 0xff, 0xff, 0xff], [0, 9]];
        foreach (var bytes in malformed)
            Assert.Throws<InvalidDataException>(() => AcerLightingPipeClient.DecodeResponse(bytes));
    }

    [TestMethod]
    public void RejectsArgumentCountsThatCannotFitTheWireFormat()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AcerLightingPipeClient.EncodeRequest(0, new byte[256][]));
    }
    [TestMethod]
    public void DeviceInfoDecodesFixedWidthModeRecordsAndRejectsWrongRecordLength()
    {
        byte[] mode = new byte[108];
        System.Text.Encoding.UTF8.GetBytes("Static").CopyTo(mode, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(mode.AsSpan(52), 6);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(mode.AsSpan(56), 17);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(mode.AsSpan(76), 100);
        byte[] name = new byte[50];
        System.Text.Encoding.UTF8.GetBytes("Keyboard").CopyTo(name, 0);
        byte[][] blocks = [[8, 0, 0, 0], name, [5, 0, 0, 0], [1, 0, 0, 0], [0, 0, 0, 0], [75, 0, 0, 0], mode];
        var info = AcerLightingPipeClient.DecodeDeviceInfo(2, blocks);
        Assert.AreEqual("Keyboard", info.Name);
        Assert.AreEqual(2, info.Index);
        Assert.AreEqual("Static", info.Modes[0].Name);
        Assert.AreEqual(17, info.Modes[0].Value);
        Assert.AreEqual(100, info.Modes[0].BrightnessMaximum);
        blocks[6] = new byte[107];
        Assert.Throws<InvalidDataException>(() => AcerLightingPipeClient.DecodeDeviceInfo(2, blocks));
    }

}
