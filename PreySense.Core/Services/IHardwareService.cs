using System;
using System.Drawing;
using PreySense.Mode;

namespace PreySense.Core.Services
{
    public enum PowerSourceKind
    {
        Unknown = -1,
        Battery = 0,
        Ac = 1
    }

    public readonly record struct HardwareTelemetry(
        int CpuTemp,
        int GpuTemp,
        int CpuRpm,
        int GpuRpm,
        int CpuFanMaxRpm,
        int GpuFanMaxRpm,
        float BatteryPercent,
        bool IsAcConnected,
        int BatteryDischargeRateMw,
        float? CpuPkgPowerWatts = null,
        PowerSourceKind PowerSource = PowerSourceKind.Ac
    );

    /// <summary>
    /// Unified hardware control and telemetry interface for PreySense.
    /// Cleanly isolates the UI from Acer WMI, NVAPI, ENEK HID, and CPU driver MSR specifics.
    /// </summary>
    public interface IHardwareService
    {
        // Power source state (live AC/battery transitions use Acer APGeEvent via TurboKeyWatcher)
        PowerSourceKind GetPowerSource();

        // Power Profiles
        byte CurrentPowerMode { get; }
        bool TryGetPowerMode(out byte mode) { mode = CurrentPowerMode; return true; }
        bool SetPowerMode(byte mode);
        event EventHandler<byte>? PowerModeChanged;

        // Fan Control
        bool SetFanMode(byte cpuMode, byte gpuMode);
        bool SetFanSpeeds(byte cpuPercentage, byte gpuPercentage);
        void InvalidateFanTargets() { }

        // Battery Control
        int GetBatteryChargeLimit();
        bool SetBatteryChargeLimit(int limitMode);

        // GPU Limits (power limits are governed via NVPCF / driver)
        int? GetGpuMuxMode();
        bool SetGpuMuxMode(int muxMode);
        bool IsBiosDiscreteGpuActive { get; }

        // NVIDIA Advanced Optimus (Dynamic Display Switch / DDS - Reboot-Free)
        bool IsDdsAvailable { get; }
        bool EnableDiscreteGpu() => false;
        void RefreshGpuAdapter() { }
        bool? IsDiscreteGpuDisabled => null;
        bool IsDdsSupported();
        Gpu.DdsStatus? GetDdsStatus();
        bool HasDdsBlockers();
        (bool Success, string Message) SetDdsMode(Gpu.DdsDisplayMode mode);

        // Discrete GPU Power Cycle & Restart (Reboot-Free via Acer Service & PnP)
        (bool Success, string Message) RestartDiscreteGpu();

        // RGB Keyboard Lighting
        bool IsRgbAvailable => true;
        void SetRgbStatic(byte zone, Color color);
        void SetRgbAllZones(Color z1, Color z2, Color z3, Color z4);
        void SetRgbHardwareEffect(int mode, byte r, byte g, byte b, byte brightness, byte speed, byte direction);
        void SetRgbBrightness(byte brightness);
        int? GetKeyboardBrightness();

        // Cover / Top Logo Backlight
        void SetCoverLogoLighting(int mode, Color color, byte brightness, byte speed, bool enabled = true);

        // Native WMI / platform system controls
        bool GetLcdOverdrive();
        bool SetLcdOverdrive(bool enable);
        bool GetBootSound();
        void SetBootSound(bool enable);
        bool GetKbdBacklightTimeout();
        void SetKbdBacklightTimeout(bool enable);
        void SetKbdBacklightTimeoutSeconds(int seconds);
        int GetUsbChargingLevel();
        void SetUsbChargingLevel(int percent);

        // Display & Vision
        bool GetBlueLightShield();
        int GetBlueLightShieldMode();
        void SetBlueLightShield(bool enable);
        void SetBlueLightShield(int mode);
        int GetDisplayRefreshRate();
        bool SetDisplayRefreshRate(int hz);
        System.Collections.Generic.List<int> GetSupportedRefreshRates();

        // dGPU Battery Sentinel
        System.Collections.Generic.List<Gpu.GpuProcessInfo> GetActiveGpuProcesses();
        bool KillProcess(uint pid);
    }
}
