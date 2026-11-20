using System;
using System.Threading.Tasks;
using PreySense.Core.Driver;
using PreySense.Core.Services;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    private static int? ReadOrCaptureGpuBaseline()
    {
        int? saved = GpuPowerBaseline.ReadWatts();
        if (saved.HasValue) return saved;
        try
        {
            int? detected = PreySense.Gpu.NvmlHelper.GetGpuDefaultPowerLimitWatts();
            return detected.HasValue ? GpuPowerBaseline.SaveOnce(detected.Value) : null;
        }
        finally { PreySense.Gpu.NvmlHelper.Shutdown(); }
    }

    private async Task CaptureGpuBaselineOnceAsync()
    {
        if (_disposed || GpuBasePowerWatts.HasValue) return;
        try
        {
            using var lease = await HardwareOperationGate.EnterGpuBaselineCaptureAsync();
            if (_disposed) return;
            int? baseline = await Task.Run(ReadOrCaptureGpuBaseline);
            if (_disposed) return;
            if (baseline.HasValue)
            {
                GpuBasePowerWatts = baseline.Value;
            }
            else AppLogger.Log("GPU baseline unavailable; no detected baseline was saved. Nominal GPU wattage remains —.");
        }
        catch (Exception ex) { AppLogger.Log($"GPU baseline capture failed: {ex.Message}"); }
    }
}
