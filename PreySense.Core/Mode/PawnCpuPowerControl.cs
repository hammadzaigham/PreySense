using System;
using System.ServiceProcess;

namespace PreySense.Mode;

/// <summary>v1.4.0 PawnIO transport, shared and serialized for static package limits.</summary>
public static class PawnCpuPowerControl
{
    private static readonly object Gate = new();
    private static PawnIO.IntelMsr? _msr;
    private static readonly bool VoltageSupported = IntelCpuVoltage.IsSupportedCpu();
    private static readonly bool TemperatureSupported = IntelCpuTemperature.IsSupportedCpu();

    public static int? ReadPackageTemperature()
    {
        if (!TemperatureSupported) return null;
        lock (Gate)
        {
            try
            {
                if (!EnsureMsr(out _) || !_msr!.ReadMsr(0x1A2, out ulong target) ||
                    !_msr.ReadMsr(0x1B1, out ulong thermal)) return null;
                return IntelCpuTemperature.DecodePackageTemperature(target, thermal);
            }
            catch { Reset(); return null; }
        }
    }

    private static bool EnsureMsr(out string status)
    {
        status = string.Empty;
        if (_msr != null) return true;
        // Use the installed PawnIO service; never install a driver here.
        using var service = new ServiceController("PawnIO");
        if (service.Status == ServiceControllerStatus.Stopped)
        {
            service.Start();
            service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(5));
        }
        var candidate = new PawnIO.IntelMsr();
        try
        {
            if (!candidate.Initialize(typeof(PawnCpuPowerControl).Assembly))
            { candidate.Dispose(); status = "PawnIO could not connect/load IntelMSR.bin."; return false; }
            _msr = candidate;
            return true;
        }
        catch { candidate.Dispose(); throw; }
    }

    public static bool TrySetLimits(int pl1, int pl2, bool enablePl2, int tauSeconds, out string status)
    {
        lock (Gate)
        {
            try
            {
                if (!EnsureMsr(out status)) return false;
                if (!_msr!.ReadMsr(0x606, out ulong units) || !_msr.ReadMsr(0x610, out ulong current))
                { status = "PawnIO could not read MSR 0x606/0x610."; Reset(); return false; }
                if (!IntelRaplPowerLimits.TryBuild(current, units, pl1, pl2, enablePl2, tauSeconds, out ulong target, out status))
                    return false;
                if (!_msr.WriteMsr(0x610, target) || !_msr.ReadMsr(0x610, out ulong readback))
                { status = "PawnIO MSR write/readback failed."; Reset(); return false; }
                bool matched = (readback & IntelRaplPowerLimits.WritableMask) == (target & IntelRaplPowerLimits.WritableMask);
                status = matched ? $"Static/MSR verified: PL1 {pl1}W, PL2 {pl2}W" : "Static/MSR readback differed; firmware may be overriding the request.";
                return matched;
            }
            catch (Exception ex) { Reset(); status = $"Static/MSR unavailable: {ex.Message}"; return false; }
        }
    }

    public static float? ReadCoreVid()
    {
        if (!VoltageSupported) return null;
        lock (Gate)
        {
            try
            {
                if (!EnsureMsr(out _) || !_msr!.ReadMsr(0x198, out ulong raw))
                {
                    Reset();
                    return null;
                }
                return IntelCpuVoltage.DecodePerfStatus(raw);
            }
            catch
            {
                Reset();
                return null;
            }
        }
    }

    private static void Reset()
    {
        try { _msr?.Dispose(); } catch { }
        _msr = null;
    }

    public static void Dispose() { lock (Gate) Reset(); }
}
