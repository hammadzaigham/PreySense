using System.Text.RegularExpressions;

namespace PreySense.Helpers
{
    /// <summary>
    /// Parsed telemetry from AcerService port 46753 (GET_MONITOR_DATA).
    /// </summary>
    public class TelemetryData
    {
        public int CpuTemp { get; set; }
        public double CpuUsage { get; set; }
        public double CpuFrequency { get; set; }
        public int CpuFanSpeed { get; set; }

        public int GpuTemp { get; set; }
        public double GpuUsage { get; set; }
        public int GpuFanSpeed { get; set; }

        public int SysTemp { get; set; }
        public int RamTotal { get; set; }
        public double RamUsage { get; set; }

        public static TelemetryData? Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;

            return new TelemetryData
            {
                CpuTemp = ExtractInt(json, "CPU_TEMPERATURE"),
                CpuUsage = ExtractDouble(json, "CPU_USAGE"),
                CpuFrequency = ExtractDouble(json, "CPU_FREQUENCY"),
                CpuFanSpeed = ExtractInt(json, "CPU_FANSPEED"),
                GpuTemp = ExtractInt(json, "GPU1_TEMPERATURE"),
                GpuUsage = ExtractDouble(json, "GPU1_USAGE"),
                GpuFanSpeed = ExtractInt(json, "GPU1_FANSPEED"),
                SysTemp = ExtractInt(json, "SYS1_TEMPERATURE"),
                RamTotal = ExtractInt(json, "RAM_TOTAL"),
                RamUsage = ExtractDouble(json, "RAM_USAGE"),
            };
        }

        private static int ExtractInt(string json, string key)
        {
            var match = Regex.Match(json, $@"""{key}""\s*:\s*(-?\d+)");
            return match.Success && int.TryParse(match.Groups[1].Value, out int v) ? v : 0;
        }

        private static double ExtractDouble(string json, string key)
        {
            var match = Regex.Match(json, $@"""{key}""\s*:\s*(-?[\d.]+)");
            return match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;
        }
    }
}
