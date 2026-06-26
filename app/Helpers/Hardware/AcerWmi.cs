using System;

namespace PreySense.Helpers
{
    public static class AcerWmi
    {
        public const string Namespace = @"root\WMI";

        public static class Classes
        {
            public const string GamingFunction = "AcerGamingFunction";
            public const string ApgeAction = "APGeAction";
            public const string BatteryControl = "BatteryControl";
            public const string MonitorBrightness = "WmiMonitorBrightness";
            public const string MonitorBrightnessMethods = "WmiMonitorBrightnessMethods";
        }

        public static class GamingMethods
        {
            public const string GetMiscSetting = "GetGamingMiscSetting";
            public const string SetMiscSetting = "SetGamingMiscSetting";
            public const string GetSystemInfo = "GetGamingSysInfo";
            public const string GetFanSpeed = "GetGamingFanSpeed";
            public const string SetFanSpeed = "SetGamingFanSpeed";
            public const string SetFanBehavior = "SetGamingFanBehavior";
            public const string SetProfile = "SetGamingProfile";
        }

        public static class ApgeMethods
        {
            public const string GetFunction = "GetFunction";
            public const string SetFunction = "SetFunction";
        }

        public static class BatteryMethods
        {
            public const string GetHealthControlStatus = "GetBatteryHealthControlStatus";
            public const string SetHealthControl = "SetBatteryHealthControl";
        }

        public static class Service
        {
            public const int CommandPort = 46933;
            public const int TelemetryPort = 46753;
            public const uint InitializationPacket = 0;
            public const uint GetUpdatedDataPacket = 20;
            public const uint SetDeviceDataPacket = 100;

            public const string Lighting = "LIGHTING";
            public const string OperatingMode = "OPERATING_MODE";
            public const string FanControl = "FAN_CONTROL";
            public const string SoundMode = "SOUND_MODE";
            public const string WinKey = "WIN_KEY";
            public const string StickyKey = "STICKY_KEY";
            public const string BootSound = "BOOT_SOUND";
            public const string LcdOverdrive = "LCD_OVERDRIVE";
            public const string GpuMode = "GPU_MODE";
            public const string PanelDfrMode = "PANEL_DFR_MODE";
            public const string AcStatus = "AC_STATUS";
            public const string AdaptorStatus = "ADAPTOR_STATUS";
            public const string BatteryBoost = "BATTERY_BOOST";
        }

        public static class OperatingModes
        {
            public const byte Silent = 0x00;
            public const byte Balanced = 0x01;
            public const byte Performance = 0x04;
            public const byte Turbo = 0x05;
            public const byte Eco = 0x06;
        }

        public static class GpuMux
        {
            public const int Discrete = 1;
            public const int Hybrid = 2;
        }

        public static class Sensors
        {
            public const ulong CpuTemperature = 0x01;
            public const ulong CpuFanRpm = 0x02;
            public const ulong GpuFanRpm = 0x06;
            public const ulong GpuTemperature = 0x0A;
        }

        public static readonly Guid WindowsPowerOverlayEfficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
        public static readonly Guid WindowsPowerOverlayBalanced = new("00000000-0000-0000-0000-000000000000");
        public static readonly Guid WindowsPowerOverlayPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");
    }
}
