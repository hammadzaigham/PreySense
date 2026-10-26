using System;
using System.ServiceProcess;

namespace PreySense.Mode;

/// <summary>v1.4.0 PawnIO transport, shared and serialized for static package limits.</summary>
public static class PawnCpuPowerControl
{
    private static readonly object Gate = new();
    private static PawnIO.IntelMsr? _msr;
    private static long _nextVoltageAttempt;
    private static readonly bool VoltageSupported = IntelCpuVoltage.IsSupportedCpu();

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
            if (Environment.TickCount64 < _nextVoltageAttempt) return null;
            try
            {
                if (!EnsureMsr(out _) || !_msr!.ReadMsr(0x198, out ulong raw))
                {
                    _nextVoltageAttempt = Environment.TickCount64 + 30000;
                    return null;
                }
                return IntelCpuVoltage.DecodePerfStatus(raw);
            }
            catch
            {
                Reset();
                _nextVoltageAttempt = Environment.TickCount64 + 30000;
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
