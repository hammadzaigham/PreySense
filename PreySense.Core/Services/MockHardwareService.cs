using System;
using System.Drawing;

namespace PreySense.Core.Services
{
    /// <summary>
    /// Mock hardware service for unit testing, rapid UI development, and XAML designer previews
    /// without sending any real commands to Acer WMI or NVAPI.
    /// </summary>
    public class MockHardwareService : IHardwareService
    {
        private byte _powerMode = 0x01; // Balanced
        private int _batteryLimit = 1;  // 80%

        public byte CurrentPowerMode => _powerMode;

        public event EventHandler<byte>? PowerModeChanged;

        public PowerSourceKind GetPowerSource() => PowerSourceKind.Ac;

        public HardwareTelemetry GetCurrentTelemetry()
        {
            var telemetry = new HardwareTelemetry(
                CpuTemp: 54,
                GpuTemp: 48,
                CpuRpm: 2400,
                GpuRpm: 2100,
                CpuFanMaxRpm: 5800,
                GpuFanMaxRpm: 5600,
                BatteryPercent: 82.5f,
                IsAcConnected: true,
                BatteryDischargeRateMw: 0
            );
            return telemetry;
        }

        public bool SetPowerMode(byte mode)
        {
            _powerMode = mode;
            PowerModeChanged?.Invoke(this, mode);
            return true;
        }

        public bool SetFanMode(byte cpuMode, byte gpuMode) => true;

        public bool SetFanSpeeds(byte cpuPercentage, byte gpuPercentage) => true;

        public int GetBatteryChargeLimit() => _batteryLimit;

        public bool SetBatteryChargeLimit(int limitMode)
        {
            _batteryLimit = limitMode;
            return true;
        }


        public int SetGpuMuxModeCallCount { get; set; }
        public int LastMuxModeSet { get; set; } = -1;
        public int? GetGpuMuxMode() => LastMuxModeSet >= 0 ? LastMuxModeSet : 1;
        public bool SetGpuMuxMode(int muxMode)
        {
            SetGpuMuxModeCallCount++;
            LastMuxModeSet = muxMode;
            return true;
        }

        public bool IsBiosDiscreteGpuActive { get; set; } = false;

        private Gpu.DdsDisplayMode _mockDdsMode = Gpu.DdsDisplayMode.Automatic;
        public bool IsDdsAvailable => true;
        public bool EnableDiscreteGpu() => true;
        public bool? IsDiscreteGpuDisabled { get; set; } = false;
        public bool IsDdsSupported() => true;
        public Gpu.DdsStatus? GetDdsStatus() => new Gpu.DdsStatus(
            IsSupported: true,
            IsAutomatic: _mockDdsMode == Gpu.DdsDisplayMode.Automatic,
            ActiveMux: _mockDdsMode == Gpu.DdsDisplayMode.NvidiaGpuOnly ? Gpu.DdsActiveMux.dGPU : Gpu.DdsActiveMux.Optimus,
            KmdResponse: 0,
            SrType: 0
        );
        public bool HasDdsBlockers() => false;
        public (bool Success, string Message) SetDdsMode(Gpu.DdsDisplayMode mode)
        {
            _mockDdsMode = mode;
            return (true, $"GPU Display Mode set to {mode}.");
        }

        public (bool Success, string Message) RestartDiscreteGpu()
        {
            return (true, "Discrete GPU successfully power-cycled and reinitialized without system reboot.");
        }

        public bool IsRgbAvailable { get; set; } = true;
        public int SetRgbStaticCount { get; private set; }
        public int SetRgbAllZonesCount { get; private set; }
        public int SetRgbHardwareEffectCount { get; private set; }
        public int SetRgbBrightnessCount { get; private set; }
        public int SetCoverLogoLightingCount { get; private set; }

        public void SetRgbStatic(byte zone, Color color) => SetRgbStaticCount++;

        public void SetRgbAllZones(Color z1, Color z2, Color z3, Color z4) => SetRgbAllZonesCount++;
        public void SetRgbAllZones(Color z1, Color z2, Color z3, Color z4, byte brightness) => SetRgbAllZonesCount++;

        public void SetRgbHardwareEffect(int mode, byte r, byte g, byte b, byte brightness, byte speed, byte direction) => SetRgbHardwareEffectCount++;

        public void SetRgbBrightness(byte brightness) => SetRgbBrightnessCount++;
        public int? GetKeyboardBrightness() => 100;

        public void SetCoverLogoLighting(int mode, Color color, byte brightness, byte speed, bool enabled = true) => SetCoverLogoLightingCount++;

        // Mock WMI / platform system controls
        private bool _mockLcdOverdrive = true;
        private bool _mockBootSound = false;
        private bool _mockKbdTimeout = false;
        private int _mockUsbCharging = 0;

        public bool GetLcdOverdrive() => _mockLcdOverdrive;
        public bool SetLcdOverdrive(bool enable)
        {
            _mockLcdOverdrive = enable;
            return true;
        }
        public bool GetBootSound() => _mockBootSound;
        public void SetBootSound(bool enable) => _mockBootSound = enable;
        public bool GetKbdBacklightTimeout() => _mockKbdTimeout;
        public void SetKbdBacklightTimeout(bool enable) => _mockKbdTimeout = enable;
        public void SetKbdBacklightTimeoutSeconds(int seconds) => _mockKbdTimeout = seconds > 0;
        public int GetUsbChargingLevel() => _mockUsbCharging;
        public void SetUsbChargingLevel(int percent) => _mockUsbCharging = percent;

        private int _mockBlueLightMode = 0;
        private int _mockRefreshRate = 240;
        public bool GetBlueLightShield() => _mockBlueLightMode > 0;
        public int GetBlueLightShieldMode() => _mockBlueLightMode;
        public void SetBlueLightShield(bool enable) => _mockBlueLightMode = enable ? 1 : 0;
        public void SetBlueLightShield(int mode) => _mockBlueLightMode = mode;
        public int GetDisplayRefreshRate() => _mockRefreshRate;
        public bool SetDisplayRefreshRate(int hz) { _mockRefreshRate = hz; return true; }
        public System.Collections.Generic.List<int> GetSupportedRefreshRates() => new() { 60, 120, 240 };

        public System.Collections.Generic.List<Gpu.GpuProcessInfo> GetActiveGpuProcesses() => new();
        public bool KillProcess(uint pid) => true;
    }
}
