using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PreySense.Battery;

namespace PreySense
{
    public partial class MainForm
    {
        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern uint CallNtPowerInformation(
            int InformationLevel,
            IntPtr InputBuffer,
            uint InputBufferLength,
            IntPtr OutputBuffer,
            uint OutputBufferLength);

        private const int SystemBatteryStateInformation = 5;

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_BATTERY_STATE
        {
            [MarshalAs(UnmanagedType.U1)] public bool AcOnLine;
            [MarshalAs(UnmanagedType.U1)] public bool BatteryPresent;
            [MarshalAs(UnmanagedType.U1)] public bool Charging;
            [MarshalAs(UnmanagedType.U1)] public bool Discharging;
            public byte Spare1;
            public byte Spare2;
            public byte Spare3;
            public byte Spare4;
            public uint MaxCapacity;
            public uint RemainingCapacity;
            public int Rate;
            public uint EstimatedTime;
            public uint DefaultAlert1;
            public uint DefaultAlert2;
        }

        private string GetBatteryRateString()
        {
            try
            {
                int size = Marshal.SizeOf<SYSTEM_BATTERY_STATE>();
                IntPtr buffer = Marshal.AllocHGlobal(size);
                try
                {
                    uint status = CallNtPowerInformation(SystemBatteryStateInformation, IntPtr.Zero, 0, buffer, (uint)size);
                    if (status != 0)
                        return "Charge: --%";

                    var state = Marshal.PtrToStructure<SYSTEM_BATTERY_STATE>(buffer);
                    if (!state.BatteryPresent)
                        return "Charge: --%";

                    int rate = state.Rate;
                    if (rate > 0)
                        return $"Charging: {rate / 1000.0:F1}W";

                    if (!state.AcOnLine && rate < 0)
                        return $"Discharging: {Math.Abs(rate) / 1000.0:F1}W";

                    if (state.AcOnLine && rate == 0)
                        return "Plugged in";

                    return "Charge: --%";
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch
            {
                return "Charge: --%";
            }
        }

        public void ApplyBatteryMode(int mode)
        {
            BatteryControl.SetBatteryLimit(_wmi, mode);
            UpdateBatteryLimitUi(mode);
            RefreshBatteryRateLabel();
            _ = RefreshBatteryRateLabelSoonAsync();
        }

        public void UpdateBatteryHighlight(int mode)
        {
            if (sliderBatteryChargeLimit == null) return;

            sliderBatteryChargeLimit.Value = mode == 1 ? 80 : 100;
            labelBatteryStatusLimitTitle.Text = $"Battery Charge Limit: {sliderBatteryChargeLimit.Value}%";
            UpdateBatteryLimitButtonFromValue(sliderBatteryChargeLimit.Value);
        }

        private void UpdateBatteryLimitButtonFromValue(int value)
        {
            bool batteryLimitEnabled = value == 80;
            buttonBatteryFull.Activated = batteryLimitEnabled;
            buttonBatteryFull.BackColor = batteryLimitEnabled ? colorStandard : buttonSecond;
            buttonBatteryFull.ForeColor = batteryLimitEnabled ? SystemColors.ControlLightLight : SystemColors.ControlDark;
            buttonBatteryFull.Text = batteryLimitEnabled ? "80%" : "100%";
        }

        public void UpdateCurrentCharge()
        {
            RefreshBatteryRateLabel();
        }

        private void RefreshBatteryRateLabel()
        {
            if (!_showBatteryTelemetry) return;
            if (labelBatteryStatus == null || labelBatteryStatus.IsDisposed) return;
            labelBatteryStatus.Text = GetBatteryRateString();
        }

        private async Task RefreshBatteryRateLabelSoonAsync()
        {
            await Task.Delay(250);
            if (IsDisposed) return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action(RefreshBatteryRateLabel));
                return;
            }

            RefreshBatteryRateLabel();
        }

        private void SetBatteryTelemetryVisible(bool visible)
        {
            _showBatteryTelemetry = visible;
            if (visible)
            {
                RefreshBatteryRateLabel();
            }
        }

        private void UpdateBatteryLimitUi(int mode)
        {
            Program.settingsForm?.UpdateBatteryHighlight(mode);
            UpdateBatteryLimitButtonFromValue(mode == 1 ? 80 : 100);
        }

        private void ApplySavedBatteryLimitAsync()
        {
            Task.Run(() =>
            {
                int mode = BatteryControl.GetBatteryLimit();
                int sliderValue = mode == 1 ? 80 : 100;

                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (IsDisposed) return;

                        _isApplyingSavedBatteryLimit = true;
                        try
                        {
                            if (sliderBatteryChargeLimit.Value != sliderValue)
                            {
                                sliderBatteryChargeLimit.Value = sliderValue;
                            }

                            labelBatteryStatusLimitTitle.Text = $"Battery Charge Limit: {sliderBatteryChargeLimit.Value}%";
                            UpdateBatteryLimitButtonFromValue(sliderBatteryChargeLimit.Value);
                        }
                        finally
                        {
                            _isApplyingSavedBatteryLimit = false;
                        }
                    }));
                }
                catch
                {
                    return;
                }

                BatteryControl.ApplyBatteryLimit(_wmi, mode);

                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (!IsDisposed) UpdateBatteryLimitUi(mode);
                    }));
                }
                catch { }
            });
        }
    }
}
