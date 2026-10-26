using System;
using System.Collections.Generic;
using System.Linq;

namespace PreySense.Core.Acpi;

/// <summary>Moves mode-independent values into shared rows without hiding real differences.</summary>
public static class FactoryCalibrationSummary
{
    private sealed record Row(int Index, int Bit, string Name, int Pl1, int Pl2, int Tau, int Gpu, int Trip);

    public static string Build(FactoryPowerProfileSnapshot snapshot, int mask, int? baseline,
        IReadOnlyDictionary<int, GpuPowerOffsets>? offsets = null, Func<string, string>? translate = null)
    {
        string T(string value) => translate?.Invoke(value) ?? value;
        var cpu = snapshot.Cpu;
        var gpu = snapshot.Gpu;
        Row[] all =
        {
            new(0, 1, "Eco", cpu.EcoPl1, cpu.EcoPl2, cpu.EcoTauSeconds, gpu.EcoGpuWatts, gpu.EcoGpuTripPointCelsius),
            new(1, 2, "Quiet", cpu.QuietPl1, cpu.QuietPl2, cpu.QuietTauSeconds, gpu.QuietGpuWatts, gpu.QuietGpuTripPointCelsius),
            new(2, 4, "Balanced", cpu.BalancedPl1, cpu.BalancedPl2, cpu.BalancedTauSeconds, gpu.BalancedGpuWatts, gpu.BalancedGpuTripPointCelsius),
            new(3, 8, "Performance", cpu.PerformancePl1, cpu.PerformancePl2, cpu.PerformanceTauSeconds, gpu.PerformanceGpuWatts, gpu.PerformanceGpuTripPointCelsius),
            new(4, 16, "Turbo", cpu.TurboPl1, cpu.TurboPl2, cpu.TurboTauSeconds, gpu.TurboGpuWatts, gpu.TurboGpuTripPointCelsius)
        };
        var rows = all.Where(r => (mask & r.Bit) != 0).ToArray();
        var shared = new List<string>
        {
            baseline.HasValue ? $"{T("GPU Baseline")}: {baseline.Value}W"
                : $"{T("GPU Baseline")}: {T("not detected (legacy fallback)")}"
        };
        var perMode = rows.ToDictionary(r => r.Index, _ => new List<string>());
        void Add(string name, Func<Row, string> value)
        {
            if (rows.Length == 0) return;
            var values = rows.Select(value).ToArray();
            if (values.Distinct().Count() == 1) shared.Add($"{T(name)}: {values[0]}");
            else for (int i = 0; i < rows.Length; i++) perMode[rows[i].Index].Add($"{T(name)} {values[i]}");
        }
        Add("GPU Thermal Trip Point", r => $"{r.Trip}°C");
        if (snapshot.TauReadFromFirmware) Add("Tau", r => CpuPowerLimitConfiguration.FormatTauSelection(r.Tau).Replace(" s", "s"));
        else shared.Add($"{T("Tau")}: {T("unavailable")}");
        Add("CPU PL1", r => $"{r.Pl1}W");
        Add("CPU PL2", r => $"{r.Pl2}W");
        foreach (var row in rows)
        {
            int watts = row.Gpu;
            string details = string.Empty;
            if (offsets != null && offsets.TryGetValue(row.Index, out var raw))
            {
                if (baseline.HasValue) watts = raw.NominalReferenceWatts(baseline.Value);
                details = $" [{T("Delta")} {raw.DeltaWatts}W · {T("Dynamic Boost")} {raw.DynamicBoostWatts}W]";
            }
            perMode[row.Index].Add($"{T("GPU")} {watts}W{details}");
        }
        var result = new List<string> { T("Profiles") };
        foreach (var row in rows)
        {
            string name = T(row.Name);
            result.Add(perMode[row.Index].Count > 0 ? $"{name}: {string.Join("  •  ", perMode[row.Index])}" : name);
        }
        result.Add(string.Empty);
        result.AddRange(shared);
        return string.Join(Environment.NewLine, result);
    }
}
