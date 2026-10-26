using System;
using System.Threading.Tasks;
using PreySense.Core.Acpi;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    private async Task CaptureGpuBaselineOnceAsync()
    {
        try
        {
            int? baseline = await Task.Run(() =>
            {
                int? saved = GpuPowerBaseline.ReadWatts();
                if (saved.HasValue) return saved;
                int? detected = PreySense.Gpu.NvmlHelper.GetGpuDefaultPowerLimitWatts();
                return detected.HasValue ? GpuPowerBaseline.SaveOnce(detected.Value) : null;
            });
            if (baseline.HasValue)
            {
                GpuBasePowerWatts = baseline.Value;
                if (_factoryPowerProfile != null) _factoryPowerProfile.Gpu.BasePowerWatts = baseline.Value;
            }
            else AppLogger.Log("GPU baseline unavailable; no detected baseline was saved. Legacy arithmetic fallback remains in use.");
        }
        catch (Exception ex) { AppLogger.Log($"GPU baseline capture failed: {ex.Message}"); }
    }
}
