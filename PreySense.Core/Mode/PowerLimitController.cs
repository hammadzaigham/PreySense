using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PreySense.Mode
{
    /// <summary>
    /// Governs CPU Boost and Windows power policy settings via powrprof.dll.
    /// </summary>
    public class PowerLimitController
    {
        private static readonly object PowerCfgGate = new();
        private static readonly Guid SubgroupProcessor = new("54533251-82be-4824-96c1-47b60b740d00");
        private static readonly Guid SettingPerfBoostMode = new("be337238-0d82-4146-a960-4f3749d470c7");
        private static readonly Guid SubgroupEnergySaver = new("de830923-a562-41af-a086-e3a2c6bad2da");
        private static readonly Guid SettingEnergySaverBatteryThreshold = new("e69653ca-cf7f-4f05-aa73-cb833fa90ad4");

        [DllImport("powrprof.dll")]
        private static extern uint PowerGetActiveScheme(IntPtr UserRootPowerKey, out IntPtr ActivePolicyGuid);

        [DllImport("powrprof.dll")]
        private static extern uint PowerWriteACValueIndex(
            IntPtr RootPowerKey,
            in Guid SchemeGuid,
            in Guid SubGroupOfPowerSettingsGuid,
            in Guid PowerSettingGuid,
            uint AcValueIndex);

        [DllImport("powrprof.dll")]
        private static extern uint PowerWriteDCValueIndex(
            IntPtr RootPowerKey,
            in Guid SchemeGuid,
            in Guid SubGroupOfPowerSettingsGuid,
            in Guid PowerSettingGuid,
            uint DcValueIndex);

        [DllImport("powrprof.dll")]
        private static extern uint PowerSetActiveScheme(IntPtr UserRootPowerKey, in Guid SchemeGuid);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);

        /// <summary>
        /// Sets Windows PERFBOOSTMODE on the active scheme (AC + DC).
        /// 0 Disabled, 1 Enabled, 2 Aggressive, 3 Efficient Enabled, 4 Efficient Aggressive,
        /// 5 Aggressive At Guaranteed, 6 Efficient Aggressive At Guaranteed.
        /// </summary>
        public static int EnergySaverBatteryThreshold(bool enabled) => enabled ? 100 : 0;

        /// <summary>
        /// Windows applies this DC threshold only while the laptop is on battery.
        /// 100 turns Energy Saver on for the whole battery session. 0 leaves it off.
        /// USB-C and the AC adapter both count as plugged in, so this stays off there.
        /// </summary>
        public static void SetEnergySaverOnBattery(bool enabled)
        {
            lock (PowerCfgGate)
            {
                int threshold = EnergySaverBatteryThreshold(enabled);
                if (!TryGetActiveScheme(out Guid scheme))
                    return;
                if (PowerWriteDCValueIndex(IntPtr.Zero, in scheme, in SubgroupEnergySaver, in SettingEnergySaverBatteryThreshold, (uint)threshold) != 0 ||
                    PowerSetActiveScheme(IntPtr.Zero, in scheme) != 0)
                {
                    return;
                }
                AppLogger.Log($"Windows Energy Saver on battery: {(enabled ? "on" : "off")} (threshold {threshold}).");
            }
        }

        public static bool SetCpuBoost(int value)
        {
            lock (PowerCfgGate)
            {
                value = Math.Clamp(value, 0, 6);
                if (!TryGetActiveScheme(out Guid scheme))
                    return false;
                uint boost = (uint)value;
                return PowerWriteACValueIndex(IntPtr.Zero, in scheme, in SubgroupProcessor, in SettingPerfBoostMode, boost) == 0 &&
                    PowerWriteDCValueIndex(IntPtr.Zero, in scheme, in SubgroupProcessor, in SettingPerfBoostMode, boost) == 0 &&
                    PowerSetActiveScheme(IntPtr.Zero, in scheme) == 0;
            }
        }

        private static bool TryGetActiveScheme(out Guid scheme)
        {
            scheme = Guid.Empty;
            if (PowerGetActiveScheme(IntPtr.Zero, out IntPtr activeGuidPtr) != 0 || activeGuidPtr == IntPtr.Zero)
                return false;
            try
            {
                scheme = Marshal.PtrToStructure<Guid>(activeGuidPtr);
                return true;
            }
            finally
            {
                LocalFree(activeGuidPtr);
            }
        }

        private static bool RunCommand(string executable, string args, int timeoutMs, out string error)
        {
            error = string.Empty;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                if (process == null) { error = "The process could not be started."; return false; }
                if (!process.WaitForExit(timeoutMs))
                {
                    error = $"Command timed out after {timeoutMs} ms.";
                    try { process.Kill(entireProcessTree: true); } catch { }
                    return false;
                }
                if (process.ExitCode != 0) { error = $"Command exited with code {process.ExitCode}."; return false; }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
