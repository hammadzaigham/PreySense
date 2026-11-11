using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using PreySense.Core.Hardware;

namespace PreySense.Frontend.Services;

/// <summary>
/// Registers for HID interface arrival/removal via WM_DEVICECHANGE and wakes waiters
/// instead of SetupDi polling.
/// </summary>
internal sealed class HidDeviceArrivalWatcher : IDisposable
{
    private static readonly Guid GuidDevInterfaceHid = new("4D1E55B2-F16F-11CF-88CB-001111000030");
    private const int WmDeviceChange = 0x0219;
    private const int DbtDeviceArrival = 0x8000;
    private const int DbtDeviceRemoveComplete = 0x8004;
    private const int DbtDevtypDeviceInterface = 0x00000005;

    private readonly HwndSource _source;
    private IntPtr _notificationHandle;
    private bool _disposed;

    public HidDeviceArrivalWatcher(IntPtr hwnd)
    {
        _source = HwndSource.FromHwnd(hwnd)
            ?? throw new InvalidOperationException("HwndSource required for HID arrival notifications.");
        _source.AddHook(WndProc);

        var filter = new DevBroadcastDeviceInterface
        {
            Size = Marshal.SizeOf<DevBroadcastDeviceInterface>(),
            DeviceType = DbtDevtypDeviceInterface,
            ClassGuid = GuidDevInterfaceHid
        };

        IntPtr buffer = Marshal.AllocHGlobal(filter.Size);
        try
        {
            Marshal.StructureToPtr(filter, buffer, false);
            _notificationHandle = RegisterDeviceNotification(hwnd, buffer, 0);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmDeviceChange && wParam.ToInt32() is DbtDeviceArrival or DbtDeviceRemoveComplete && lParam != IntPtr.Zero)
        {
            int deviceType = Marshal.ReadInt32(lParam, 4);
            if (deviceType == DbtDevtypDeviceInterface)
                HidArrivalHub.NotifyArrived();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _source.RemoveHook(WndProc); } catch { }
        if (_notificationHandle != IntPtr.Zero)
        {
            UnregisterDeviceNotification(_notificationHandle);
            _notificationHandle = IntPtr.Zero;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterDeviceNotification(IntPtr hRecipient, IntPtr notificationFilter, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterDeviceNotification(IntPtr handle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct DevBroadcastDeviceInterface
    {
        public int Size;
        public int DeviceType;
        public int Reserved;
        public Guid ClassGuid;
        public char Name;
    }
}
