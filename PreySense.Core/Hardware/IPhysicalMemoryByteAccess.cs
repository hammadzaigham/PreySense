namespace PreySense.Core.Hardware;

/// <summary>Byte access shared by the bundled driver and verified GPU profile transactions.</summary>
public interface IPhysicalMemoryByteAccess : IDisposable
{
    bool ReadPhysicalByte(ulong physicalAddress, out byte value);
    bool WritePhysicalByte(ulong physicalAddress, byte value);
}

/// <summary>Acer mode control used by the active profile transaction.</summary>
public interface IAcerPowerModeController
{
    bool TryGetAcerPowerMode(out byte mode);
    bool SetAcerPowerMode(byte mode);
}
