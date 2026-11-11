using System;
using System.Threading.Tasks;
using PreySense.Core.Driver;
using PreySense.Core.Services;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    private int? ReadOrCaptureGpuBaseline()
    {
        int? saved = GpuPowerBaseline.ReadWatts();
        if (saved.HasValue) return saved;
        using var lease = HardwareOperationGate.TryEnterTelemetry();
        if (lease == null || PreySense.Helpers.DeviceHelper.GetDisplayDevicePowerState("VEN_10DE") != 1) return null;
        try
        {
            int? detected = PreySense.Gpu.NvmlHelper.GetGpuDefaultPowerLimitWatts();
            return detected.HasValue ? GpuPowerBaseline.SaveOnce(detected.Value) : null;
        }
        finally { PreySense.Gpu.NvmlHelper.Shutdown(); }
    }

    private async Task CaptureGpuBaselineOnceAsync()
    {
        try
        {
            int? baseline = await Task.Run(ReadOrCaptureGpuBaseline);
            if (baseline.HasValue)
            {
                GpuBasePowerWatts = baseline.Value;
            }
            else AppLogger.Log("GPU baseline unavailable; no detected baseline was saved. Nominal GPU wattage remains N/A.");
        }
        catch (Exception ex) { AppLogger.Log($"GPU baseline capture failed: {ex.Message}"); }
    }
}
