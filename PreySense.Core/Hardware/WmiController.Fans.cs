using PreySense.Core.Services;
using PreySense.Mode;

namespace PreySense.Helpers;

public partial class WmiController
{
    public bool SetFanBehavior(byte mode)
    {
        ulong value = mode switch
        {
            0 => 0x410009ul,
            1 => 0x820009ul,
            2 => 0xC30009ul,
            _ => 0x410009ul
        };
        return SendCommand(AcerWmi.GamingMethods.SetFanBehavior, value).success;
    }

    public static int ScalePercentToWmi(int percent) => EcFanPolicy.ClampDuty(percent);

    public void InvalidateFanTargets()
    {
        lock (_lock) { _lastSetFanMode = -1; }
    }

    public bool SetCpuFanSpeed(int percent) => SetFanDuty(1, percent);
    public bool SetGpuFanSpeed(int percent) => SetFanDuty(4, percent);

    private bool SetFanDuty(int fan, int percent)
    {
        int duty = EcFanPolicy.ClampDuty(percent);
        ulong payload = (ulong)(((duty * 25600) / 100) & 0xFF00) + (uint)fan;
        return SendCommand(AcerWmi.GamingMethods.SetFanSpeed, payload).success;
    }

    public bool SetFanControlWmi(int mode, int cpuSpeed = 50, int gpuSpeed = 50)
    {
        lock (_lock)
        {
            cpuSpeed = EcFanPolicy.ClampDuty(cpuSpeed);
            gpuSpeed = EcFanPolicy.ClampDuty(gpuSpeed);
            bool force = mode != _lastSetFanMode;
            if (!force && (mode != 2 || cpuSpeed == _lastSetCpuSpeed && gpuSpeed == _lastSetGpuSpeed))
                return true;
            if (force && !SetFanBehavior((byte)mode)) return false;
            if (mode == 2)
            {
                if ((force || cpuSpeed != _lastSetCpuSpeed) && !SetCpuFanSpeed(cpuSpeed)) return false;
                if ((force || gpuSpeed != _lastSetGpuSpeed) && !SetGpuFanSpeed(gpuSpeed)) return false;
            }
            _lastSetFanMode = mode;
            _lastSetCpuSpeed = cpuSpeed;
            _lastSetGpuSpeed = gpuSpeed;
            return true;
        }
    }
}
