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
    public sealed class ProductionHardwareService : IHardwareService, ITelemetrySources, IDisposable
    {
        private readonly WmiController _wmi;
        private readonly Lazy<CustomLightingEffectsEngine> _lightingEngine;
        private readonly Lazy<int?> _memorySpeed = new(ReadMemorySpeed);
        private int _kbdTimeoutSeconds;

        public event EventHandler<byte>? PowerModeChanged;

        public byte CurrentPowerMode
        {
            get
            {
                if (_wmi.TryGetPowerProfile(out byte mode))
                    return mode;
                return byte.MaxValue; // Unavailable, never claim a hardware mode.
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
            TelemetryCoordinator.ConfigureSampler(SampleTelemetrySnapshot, SampleFanOnlyTelemetrySnapshot);
        }

        public PowerSourceKind GetPowerSource() => _wmi.ReadPowerSource();

        private TelemetrySnapshot SampleTelemetrySnapshot() =>
            TelemetryCollector.Read(TelemetryCoordinator.RequestedMetrics, this, !TelemetryCoordinator.HasUiDemand);

        private TelemetrySnapshot SampleFanOnlyTelemetrySnapshot() =>
            TelemetryCollector.Read(TelemetryMetrics.Temperatures, this, fanOnly: true);

        CpuTelemetrySnapshot? ITelemetrySources.ReadCpuUsage() => CpuUsageMonitor.GetSnapshot(aggregateOnly: true);
        int? ITelemetrySources.ReadCpuTemperature() => PreySense.Core.Hardware.CpuDriverRuntime.Telemetry?.ReadPackageTemperature();
        float? ITelemetrySources.ReadCpuVoltage() => PreySense.Core.Hardware.CpuDriverRuntime.Telemetry?.ReadCoreVoltageVid();
        float? ITelemetrySources.ReadCpuPower() => PreySense.Core.Hardware.CpuDriverRuntime.Telemetry?.ReadPackagePowerWatts();
        int? ITelemetrySources.ReadRamSpeed() => _memorySpeed.Value;
        NvidiaGpuControl.Metrics ITelemetrySources.ReadGpu(TelemetryMetrics metrics)
        {
            // Read Windows' cached power data before touching NVAPI/NVML or even
            // constructing the GPU client. Missing/asleep adapters remain asleep.
            if (DeviceHelper.GetDisplayDevicePowerState("VEN_10DE") != 1)
            {
                NvmlHelper.Shutdown();
                return default;
            }
            return Gpu.ReadMetrics(metrics);
        }

        CpuClockSnapshot ITelemetrySources.ReadCpuClockSnapshot(TelemetryMetrics requested) => ReadDriverClocks();

        private static CpuClockSnapshot ReadDriverClocks()
        {
            var sample = PreySense.Core.Hardware.CpuDriverRuntime.Telemetry?.SampleTopologyClocks();
            if (sample is not { } clocks) return default;
            static int? Max(int? a, int? b) => a.HasValue && b.HasValue ? Math.Max(a.Value, b.Value) : a ?? b;
            return new(clocks.PCoreActiveMhz, clocks.ECoreActiveMhz,
                Max(clocks.PCoreActiveMhz, clocks.ECoreActiveMhz),
                Max(clocks.PCoreEffectiveMhz, clocks.ECoreEffectiveMhz));
        }

        (int? Performance, int? Efficiency) ITelemetrySources.ReadCpuClocks()
        {
            var clocks = ReadDriverClocks();
            return (clocks.PerformanceMhz, clocks.EfficiencyMhz);
        }

        (int? ActiveMhz, int? EffectiveMhz) ITelemetrySources.ReadCpuActiveEffectiveClocks()
        {
            var clocks = ReadDriverClocks();
            return (clocks.ActiveMhz, clocks.EffectiveMhz);
        }

        (int? Pl1Watts, int? Pl2Watts) ITelemetrySources.ReadCpuPowerLimits()
        {
            var snap = PreySense.Core.Hardware.CpuDriverRuntime.PowerLimits?.ReadMsrPowerLimits();
            if (snap is { Success: true } s)
            {
                return (s.Pl1Watts > 0 ? s.Pl1Watts : null, s.Pl2Watts > 0 ? s.Pl2Watts : null);
            }

            return default;
        }

        (int? Percent, int? UsedMb) ITelemetrySources.ReadRamUsage()
        {
            var memory = SystemMemoryMonitor.GetMemoryInfo();
            return (memory?.UsedPercent, memory?.UsedMb);
        }

        (int? Cpu, int? Gpu) ITelemetrySources.ReadFanRpms(bool cpu, bool gpu)
        {
            var fans = _wmi.ReadThermalSensors(includeTemperatures: false, readCpuRpm: cpu, readGpuRpm: gpu);
            return (fans.CpuRpm >= 0 ? fans.CpuRpm : null, fans.GpuRpm >= 0 ? fans.GpuRpm : null);
        }

        private static int? ReadMemorySpeed()
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher("SELECT ConfiguredClockSpeed FROM Win32_PhysicalMemory");
                using var results = searcher.Get();
                foreach (System.Management.ManagementObject module in results)
                {
                    using (module)
                        if (module["ConfiguredClockSpeed"] is uint speed && speed > 0) return (int)speed;
                }
            }
            catch (System.Management.ManagementException) { }
            return null;
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

        // WMI / platform system controls
        private bool _lcdOverdrive;
        public bool GetLcdOverdrive() => _lcdOverdrive;
        public bool SetLcdOverdrive(bool enable)
        {
            bool ok = _wmi.SetLcdOverdrive(enable);
            if (ok) _lcdOverdrive = enable;
            return ok;
        }

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
            _wmi.Dispose();
            TelemetryCoordinator.Reset();
            GC.SuppressFinalize(this);
        }
    }
}
