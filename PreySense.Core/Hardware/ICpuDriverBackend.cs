using System;

namespace PreySense.Core.Hardware;

/// <summary>
/// Hardware abstraction interface for low-level CPU MSR and MMIO access.
/// Decouples CPU power and telemetry operations from specific driver implementations.
/// </summary>
public interface ICpuDriverBackend : IDisposable
{
    /// <summary>
    /// Gets a value indicating whether the driver backend is initialized and ready for commands.
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// Reads a 64-bit Model-Specific Register (MSR).
    /// </summary>
    /// <param name="msr">32-bit MSR address (e.g. 0x610, 0x606, 0x611).</param>
    /// <param name="value">64-bit value read from the register.</param>
    /// <returns>True if the read succeeded; otherwise false.</returns>
    bool ReadMsr(uint msr, out ulong value);

    /// <summary>
    /// Writes a 64-bit Model-Specific Register (MSR).
    /// </summary>
    /// <param name="msr">32-bit MSR address (e.g. 0x610).</param>
    /// <param name="value">64-bit value to write.</param>
    /// <returns>True if the write succeeded; otherwise false.</returns>
    bool WriteMsr(uint msr, ulong value);

    /// <summary>
    /// Reads a 32-bit doubleword from physical memory (MMIO).
    /// </summary>
    /// <param name="physicalAddress">64-bit physical address.</param>
    /// <param name="value">32-bit value read.</param>
    /// <returns>True if the read succeeded; otherwise false.</returns>
    bool ReadPhysicalDword(ulong physicalAddress, out uint value);

    /// <summary>
    /// Writes a 32-bit doubleword to physical memory (MMIO).
    /// </summary>
    /// <param name="physicalAddress">64-bit physical address.</param>
    /// <param name="value">32-bit value to write.</param>
    /// <returns>True if the write succeeded; otherwise false.</returns>
    bool WritePhysicalDword(ulong physicalAddress, uint value);
}
