using System;
using System.Drawing;
using PreySense.Battery;
using PreySense.Core.Services;
using PreySense.Gpu;
using PreySense.Helpers;
using PreySense.Mode;
using PreySense.Rgb;

namespace PreySense.Frontend.Services
{
    public sealed class ProductionHardwareService : IHardwareService, IDisposable
    {
        private readonly WmiController _wmi;
        private readonly Lazy<CustomLightingEffectsEngine> _lightingEngine;
        private readonly Lazy<PdhCpuPowerMonitor> _powerMonitor;
        private readonly Lazy<PdhCpuFrequencyMonitor> _frequencyMonitor;
        private readonly object _telemetryLock = new();
        private int _kbdTimeoutSeconds;

        public event EventHandler<byte>? PowerModeChanged;
        public event EventHandler? PowerSourceChanged;

        public byte CurrentPowerMode
        {
            get
            {
                if (_wmi.TryGetPowerProfile(out byte mode))
                    return mode;
                return 0x01; // Balanced fallback
            }
        }

        private NvidiaGpuControl Gpu => NvidiaGpuControl.Shared;
        public bool TryGetPowerMode(out byte mode) => _wmi.TryGetPowerProfile(out mode);
        public CustomLightingEffectsEngine LightingEngine => _lightingEngine.Value;
        public WmiController Wmi => _wmi;

        public ProductionHardwareService()
        {
            _wmi = new WmiController();
            _lightingEngine = new Lazy<CustomLightingEffectsEngine>(() =>
            {
                var engine = new CustomLightingEffectsEngine(_wmi);
                engine.SetIdleTimeoutSeconds(_kbdTimeoutSeconds);
                return engine;
            }, true);
            _powerMonitor = new Lazy<PdhCpuPowerMonitor>(() => new PdhCpuPowerMonitor(), true);
            _frequencyMonitor = new Lazy<PdhCpuFrequencyMonitor>(() => new PdhCpuFrequencyMonitor(), true);
            _wmi.PowerSourceChanged += (_, _) =>
            {
                PowerSourceChanged?.Invoke(this, EventArgs.Empty);
            };
            // Power source events are unified and listened to via TurboKeyWatcher on APGeEvent
            // to prevent WMI quota violations from duplicate event subscriptions.
            TelemetryCoordinator.AutoPromptOnDemandResume = true;
            TelemetryCoordinator.ConfigureSampler(SampleFullTelemetrySnapshot, SampleFanOnlyTelemetrySnapshot);
        }

        public PowerSourceKind GetPowerSource() => _wmi.ReadPowerSource();

        public void NotePluggedSource(PowerSourceKind kind) => _wmi.NotePluggedSource(kind);

        public HardwareTelemetry GetCurrentTelemetry()
        {
            lock (_telemetryLock)
            {
                var thermal = _wmi.ReadThermalSensors(includeTemperatures: false);
                int cpuTemp = PreySense.Mode.PawnCpuPowerControl.ReadPackageTemperature() ?? -1;
                int gpuTemp = Gpu.ReadCurrentTemperature() ?? -1;
                int cpuRpm = thermal.CpuRpm;
                int gpuRpm = thermal.GpuRpm;
                _wmi.TryGetPowerStatus(out bool onAc, out float batteryPercent);
                PowerSourceKind source = _wmi.ReadPowerSource();
                float pkgPower = _powerMonitor.Value.GetPackagePowerWatts();

                return new HardwareTelemetry(
                    CpuTemp: cpuTemp,
                    GpuTemp: gpuTemp,
                    CpuRpm: cpuRpm,
                    GpuRpm: gpuRpm,
                    CpuFanMaxRpm: 5800,
                    GpuFanMaxRpm: 5600,
                    BatteryPercent: batteryPercent,
                    IsAcConnected: onAc,
                    BatteryDischargeRateMw: 0,
                    CpuPkgPowerWatts: pkgPower,
                    PowerSource: source
                );
            }
        }

        public HardwareTelemetry GetFanTemperatures()
        {
            // Custom curves need temperatures, not power, battery, RPM or utilization.
            int cpu = PreySense.Mode.PawnCpuPowerControl.ReadPackageTemperature() ?? -1;
            int gpu = Gpu.ReadCurrentTemperature() ?? 0;
            return new HardwareTelemetry(cpu, gpu, 0, 0, 5800, 5600, 0, false, 0);
        }

        private TelemetrySnapshot SampleFullTelemetrySnapshot()
        {
            lock (_telemetryLock)
            {
                long timestamp = System.Diagnostics.Stopwatch.GetTimestamp();

                // 1. CPU / RAM (throttled & non-blocking native syscalls)
                var cpu = CpuUsageMonitor.GetSnapshot();
                Interlocked.Increment(ref TelemetryInstrumentation.CpuTelemetryReadCount);

                var ram = SystemMemoryMonitor.GetMemoryInfo();
                Interlocked.Increment(ref TelemetryInstrumentation.RamTelemetryReadCount);

                // 2. EC Thermals & Fans & Battery
                var thermal = _wmi.ReadThermalSensors(includeTemperatures: false);
                Interlocked.Increment(ref TelemetryInstrumentation.EcThermalReadCount);

                int cpuTemp = PreySense.Mode.PawnCpuPowerControl.ReadPackageTemperature() ?? -1;
                int gpuTemp = Gpu.ReadCurrentTemperature() ?? -1;
                int cpuRpm = thermal.CpuRpm;
                int gpuRpm = thermal.GpuRpm;
                _wmi.TryGetPowerStatus(out bool onAc, out float batteryPercent);
                PowerSourceKind source = _wmi.ReadPowerSource();
                float pkgPower = _powerMonitor.Value.GetPackagePowerWatts();

                // 3. GPU metrics (only if dGPU is in use, avoiding waking dormant GPU)
                bool gpuInUse = Gpu.IsDiscreteGpuInUse();
                int? gpuUsage = null;
                int? gpuMhz = null;
                float? gpuVoltage = null;
                float? gpuPower = null;
                int? vramUsedMb = null;
                int? vramTotalMb = null;
                int? vramSpeedMhz = null;

                if (gpuInUse)
                {
                    try
                    {
                        gpuUsage = Gpu.GetGpuUse();
                        gpuMhz = Gpu.GetGpuClock();
                        gpuVoltage = Gpu.GetGpuVoltage();
                        gpuPower = (float?)Gpu.GetGpuPower();
                        if (Gpu.GetVramInfo() is { } v && v.totalMb > 0)
                        {
                            vramUsedMb = (int)v.usedMb;
                            vramTotalMb = (int)v.totalMb;
                        }
                        vramSpeedMhz = Gpu.GetGpuMemoryClock();
                        Interlocked.Increment(ref TelemetryInstrumentation.GpuTelemetryReadCount);
                    }
                    catch { }
                }

                float? cpuVid = PreySense.Mode.PawnCpuPowerControl.ReadCoreVid();

                int? cpuClockMhz = null;
                int? eCoreClockMhz = null;
                try
                {
                    if (_frequencyMonitor.Value.TrySampleFrequencies(out int maxMhz, out _, out _, out int eMaxMhz))
                    {
                        if (maxMhz > 0) cpuClockMhz = maxMhz;
                        if (eMaxMhz > 0) eCoreClockMhz = eMaxMhz;
                    }
                }
                catch { }

                return new TelemetrySnapshot(
                    TimestampTicks: timestamp,
                    CpuUsage: cpu?.TotalUsage,
                    CpuMaxCoreUsage: cpu?.MaxCoreUsage,
                    CpuPCoreUsage: cpu?.PCoreUsage,
                    CpuECoreUsage: cpu?.ECoreUsage,
                    RamUsage: ram?.UsedPercent,
                    RamUsedMb: ram?.UsedMb,
                    CpuTemp: cpuTemp,
                    GpuTemp: gpuTemp,
                    CpuRpm: cpuRpm,
                    GpuRpm: gpuRpm,
                    CpuFanMaxRpm: 5800,
                    GpuFanMaxRpm: 5600,
                    CpuPkgPowerWatts: pkgPower,
                    GpuPowerWatts: gpuPower,
                    CpuVoltage: cpuVid,
                    GpuVoltage: gpuVoltage,
                    CpuClockMhz: cpuClockMhz,
                    ECoreClockMhz: eCoreClockMhz,
                    GpuClockMhz: gpuMhz,
                    GpuUsage: gpuUsage,
                    VramUsedMb: vramUsedMb,
                    VramTotalMb: vramTotalMb,
                    VramSpeedMhz: vramSpeedMhz,
                    GpuInUse: gpuInUse,
                    BatteryPercent: batteryPercent,
                    IsAcConnected: onAc,
                    PowerSource: source,
                    IsValid: true,
                    IsFanOnly: false,
                    ThermalTimestampTicks: timestamp);
            }
        }

        private TelemetrySnapshot SampleFanOnlyTelemetrySnapshot()
        {
            long timestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            int cpu = PreySense.Mode.PawnCpuPowerControl.ReadPackageTemperature() ?? -1;
            int gpu = Gpu.ReadCurrentTemperature() ?? 0;
            Interlocked.Increment(ref TelemetryInstrumentation.EcThermalReadCount);

            var curr = TelemetryCoordinator.CurrentSnapshot;
            if (curr.IsValid)
            {
                return curr.WithFanTemperatures(cpu, gpu, timestamp) with { IsFanOnly = true };
            }

            return new TelemetrySnapshot(
                TimestampTicks: 0,
                CpuUsage: null,
                CpuMaxCoreUsage: null,
                CpuPCoreUsage: null,
                CpuECoreUsage: null,
                RamUsage: null,
                RamUsedMb: null,
                CpuTemp: cpu,
                GpuTemp: gpu,
                CpuRpm: null,
                GpuRpm: null,
                CpuFanMaxRpm: 5800,
                GpuFanMaxRpm: 5600,
                CpuPkgPowerWatts: null,
                GpuPowerWatts: null,
                CpuVoltage: null,
                GpuVoltage: null,
                CpuClockMhz: null,
                ECoreClockMhz: null,
                GpuClockMhz: null,
                GpuUsage: null,
                VramUsedMb: null,
                VramTotalMb: null,
                VramSpeedMhz: null,
                GpuInUse: false,
                BatteryPercent: 0,
                IsAcConnected: false,
                PowerSource: PowerSourceKind.Ac,
                IsValid: cpu > 0 || gpu > 0,
                IsFanOnly: true,
                ThermalTimestampTicks: timestamp);
        }

        public bool EnableDiscreteGpu()
        {
            if (!DeviceHelper.SetDeviceState("VEN_10DE", true)) return false;
            NvidiaGpuControl.RestartNVService();
            NvidiaDdsService.Instance.Dispose();
            NvidiaGpuControl.Shared.RefreshDevice();
            return true;
        }

        public bool? IsDiscreteGpuDisabled => DeviceHelper.IsDisplayDeviceDisabled("VEN_10DE");
        public void RefreshGpuAdapter() => NvidiaGpuControl.Shared.RefreshDevice();

        public bool SetPowerMode(byte mode)
        {
            bool ok = _wmi.SetPowerMode(mode);
            if (ok)
            {
                PowerModeChanged?.Invoke(this, mode);
            }
            return ok;
        }

        public bool SetFanMode(byte cpuMode, byte gpuMode)
        {
            return _wmi.SetFanControl(cpuMode);
        }

        public bool SetFanSpeeds(byte cpuPercentage, byte gpuPercentage)
        {
            return _wmi.SetFanControl(2, cpuPercentage, gpuPercentage);
        }

        public void InvalidateFanTargets() => _wmi.InvalidateFanTargets();

        public int GetBatteryChargeLimit()
        {
            return BatteryControl.GetBatteryLimit();
        }

        public bool SetBatteryChargeLimit(int limitMode)
        {
            return BatteryControl.SetBatteryLimit(_wmi, limitMode);
        }

        public int? GetGpuMuxMode()
        {
            return _wmi.GetGpuMuxModeWmi();
        }

        public bool SetGpuMuxMode(int muxMode)
        {
            return _wmi.SetGpuMuxMode(muxMode);
        }

        public bool IsBiosDiscreteGpuActive
        {
            get
            {
                // BIOS dGPU only keeps the iGPU device node, but Windows reports it Off Line.
                return DeviceHelper.IsDiscreteGpuOnlyDisplay();
            }
        }

        public bool IsDdsAvailable => NvidiaDdsService.Instance.IsAvailable;
        public bool IsDdsSupported() => NvidiaDdsService.Instance.IsDdsSupported();
        public DdsStatus? GetDdsStatus() => NvidiaDdsService.Instance.GetStatus();
        public bool HasDdsBlockers() => NvidiaDdsService.Instance.HasBlockingApps();
        public (bool Success, string Message) SetDdsMode(DdsDisplayMode mode) => NvidiaDdsService.Instance.SetMode(mode);

        public (bool Success, string Message) RestartDiscreteGpu()
        {
            try
            {
                AppLogger.Log("RestartDiscreteGpu: Initiating reboot-free dGPU power cycle via Windows PnP...");

                var control = NvidiaGpuControl.Shared;
                if (control.IsValid)
                {
                    control.KillGPUApps();
                }

                if (!DeviceHelper.RestartDevice("VEN_10DE"))
                    return (false, "Windows could not restart the NVIDIA adapter.");
                NvidiaGpuControl.RestartNVService();
                System.Threading.Thread.Sleep(1500);
                NvidiaDdsService.Instance.Dispose();
                control.RefreshDevice();
                if (NvidiaDdsService.Instance.IsAvailable && NvidiaDdsService.Instance.IsDdsSupported())
                {
                    var (changed, detail) = NvidiaDdsService.Instance.SetMode(DdsDisplayMode.Automatic);
                    if (!changed) return (false, $"Adapter restarted; automatic display mode failed: {detail}");
                }

                return (true, "Discrete GPU successfully power-cycled and reinitialized without system reboot.");
            }
            catch (Exception ex)
            {
                AppLogger.Log($"RestartDiscreteGpu error: {ex.Message}");
                return (false, $"dGPU restart failed: {ex.Message}");
            }
        }

        public bool IsRgbAvailable => _wmi.IsEnekRgbAvailable;

        public void SetRgbStatic(byte zone, Color color)
        {
            LightingEngine.Stop();
            int idx = Math.Clamp(zone - 1, 0, 3);
            Color[] colors = new Color[4];
            Array.Copy(_wmi.ZoneColors, colors, 4);
            colors[idx] = color;
            _wmi.SetZoneColors(colors, _wmi.Brightness);
        }

        public void SetRgbAllZones(Color z1, Color z2, Color z3, Color z4)
        {
            LightingEngine.Stop();
            _wmi.SetZoneColors([z1, z2, z3, z4], _wmi.Brightness);
        }

        public void SetRgbHardwareEffect(int mode, byte r, byte g, byte b, byte brightness, byte speed, byte direction)
        {
            LightingEngine.Stop();
            _wmi.SetRgbMode(mode, r, g, b, brightness, speed, direction);
        }

        public void SetRgbSpeed(byte speed)
        {
            LightingEngine.SetSpeed(speed);
            _wmi.SetSpeed(speed);
        }

        public void SetRgbBrightness(byte brightness)
        {
            byte scaled = (byte)Math.Clamp((int)brightness, 0, 100);
            LightingEngine.SetBrightness(scaled);
            if (LightingEngine.IsRunning)
                _wmi.NoteBrightness(scaled);
            else
                _wmi.SetBrightness(scaled);
        }

        public int? GetKeyboardBrightness() => _wmi.GetKeyboardBrightness();

        public void SetCoverLogoLighting(int mode, Color color, byte brightness, byte speed, bool enabled = true)
        {
            _wmi.SetLogoLighting(mode, color.R, color.G, color.B, brightness, speed, enabled);
        }

        // ACPI / WMI Native System Controls
        public bool GetLcdOverdrive()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PreySense\AdvancedSettings");
                return key?.GetValue("LcdOverdrive") is int value && value == 1;
            }
            catch
            {
                return false;
            }
        }

        public bool SetLcdOverdrive(bool enable) => _wmi.SetLcdOverdrive(enable);

        public bool GetBootSound() => _wmi.GetBootSound();
        public void SetBootSound(bool enable) => _wmi.SetBootSound(enable);

        public bool GetKbdBacklightTimeout() => _wmi.GetLedTimeout();
        public void SetKbdBacklightTimeout(bool enable) => SetKbdBacklightTimeoutSeconds(enable ? 30 : 0);
        public void SetKbdBacklightTimeoutSeconds(int seconds)
        {
            _kbdTimeoutSeconds = Math.Max(0, seconds);
            bool softwareOwnsTimeout = _lightingEngine.IsValueCreated && LightingEngine.IsRunning && _kbdTimeoutSeconds > 0;
            // Animation frames reset the EC idle timer. While a software effect is running, keep the hardware timeout off.
            _wmi.SetLedTimeoutSeconds(softwareOwnsTimeout ? 0 : _kbdTimeoutSeconds);
            if (_lightingEngine.IsValueCreated)
                LightingEngine.SetIdleTimeoutSeconds(_kbdTimeoutSeconds);
        }

        public int GetUsbChargingLevel() => _wmi.GetUsbChargingLevel();
        public void SetUsbChargingLevel(int percent) => _wmi.SetUsbChargingLevel(percent);

        private int _blueLightShieldMode = 0;
        public bool GetBlueLightShield() => _blueLightShieldMode > 0;
        public int GetBlueLightShieldMode() => _blueLightShieldMode;
        public void SetBlueLightShield(bool enable) => SetBlueLightShield(enable ? 1 : 0);
        public void SetBlueLightShield(int mode)
        {
            _blueLightShieldMode = mode;
            DisplayManager.SetBlueLightShield(mode);
        }

        public int GetDisplayRefreshRate() => DisplayManager.GetCurrentRefreshRate();
        public bool SetDisplayRefreshRate(int hz) => DisplayManager.SetRefreshRate(hz);
        public System.Collections.Generic.List<int> GetSupportedRefreshRates() => DisplayManager.GetSupportedRefreshRates();

        public System.Collections.Generic.List<Gpu.GpuProcessInfo> GetActiveGpuProcesses() => Gpu.GetGpuProcesses();

        public bool KillProcess(uint pid)
        {
            try
            {
                using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
                proc.Kill();
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"ProductionHardwareService: Failed to kill PID {pid}: {ex.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            if (_lightingEngine.IsValueCreated)
                _lightingEngine.Value.Dispose();
            if (_powerMonitor.IsValueCreated)
                _powerMonitor.Value.Dispose();
            if (_frequencyMonitor.IsValueCreated)
                _frequencyMonitor.Value.Dispose();
            _wmi.Dispose();
            TelemetryCoordinator.Reset();
            GC.SuppressFinalize(this);
        }
    }
}
