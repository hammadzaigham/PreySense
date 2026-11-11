using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PreySense.Frontend.Views
{
    public partial class ModeToastWindow : Window
    {
        private const int GwlExStyle = -20;
        private const int WsExTransparent = 0x00000020;
        private const int WsExToolWindow = 0x00000080;
        private const int WsExNoActivate = 0x08000000;
        private const uint MonitorDefaultToNearest = 2;
        private const double BottomOffsetPhysicalPixels = 72;

        private static readonly Duration FadeInDuration = new(TimeSpan.FromMilliseconds(150));
        private static readonly Duration FadeOutDuration = new(TimeSpan.FromMilliseconds(250));
        private static ModeToastWindow? _currentToast;

        private readonly DispatcherTimer _hideTimer;
        private bool _isShuttingDown;

        public ModeToastWindow()
        {
            InitializeComponent();
            _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
            _hideTimer.Tick += OnHideTimerTick;
        }

        public static void ShowConfirmedMode(byte mode, string modeName)
        {
            App.Current?.Dispatcher?.InvokeAsync(() =>
            {
                _currentToast ??= new ModeToastWindow();
                var (icon, borderBrushKey) = GetModeAppearance(mode);
                _currentToast.Display(modeName, icon, borderBrushKey);
            });
        }

        public static void Shutdown()
        {
            if (_currentToast is null)
                return;

            _currentToast._isShuttingDown = true;
            _currentToast._hideTimer.Stop();
            _currentToast.Close();
            _currentToast = null;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var handle = new WindowInteropHelper(this).Handle;
            int styles = GetWindowLong(handle, GwlExStyle);
            SetWindowLong(handle, GwlExStyle, styles | WsExTransparent | WsExToolWindow | WsExNoActivate);
        }

        protected override void OnClosed(EventArgs e)
        {
            _hideTimer.Stop();
            _hideTimer.Tick -= OnHideTimerTick;
            if (!_isShuttingDown && ReferenceEquals(_currentToast, this))
                _currentToast = null;
            base.OnClosed(e);
        }

        private void Display(string modeName, string iconPath, string borderBrushKey)
        {
            _hideTimer.Stop();
            BeginAnimation(OpacityProperty, null);

            ModeLabel.Text = modeName;
            IconPathShape.Data = Geometry.Parse(iconPath);
            ToastBorder.BorderThickness = new Thickness(0);
            Opacity = 0;

            if (!IsVisible)
                Show();

            UpdateLayout();
            PositionOnMainWindowMonitor();

            var fadeIn = new DoubleAnimation(0, 1, FadeInDuration)
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            fadeIn.Completed += (_, _) =>
            {
                if (IsVisible)
                    _hideTimer.Start();
            };
            BeginAnimation(OpacityProperty, fadeIn, HandoffBehavior.SnapshotAndReplace);
        }

        private void OnHideTimerTick(object? sender, EventArgs e)
        {
            _hideTimer.Stop();
            var fadeOut = new DoubleAnimation(Opacity, 0, FadeOutDuration)
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (_, _) =>
            {
                if (Opacity <= 0.01)
                    Hide();
            };
            BeginAnimation(OpacityProperty, fadeOut, HandoffBehavior.SnapshotAndReplace);
        }

        private void PositionOnMainWindowMonitor()
        {
            nint targetHandle = App.Current?.MainWindow is Window mainWindow
                ? new WindowInteropHelper(mainWindow).Handle
                : nint.Zero;
            if (targetHandle == nint.Zero)
                targetHandle = new WindowInteropHelper(this).Handle;

            nint monitor = MonitorFromWindow(targetHandle, MonitorDefaultToNearest);
            var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (monitor == nint.Zero || !GetMonitorInfo(monitor, ref monitorInfo))
                return;

            uint dpi = GetDpiForWindow(targetHandle);
            double scale = dpi > 0 ? dpi / 96.0 : 1.0;
            double workLeft = monitorInfo.WorkArea.Left / scale;
            double workBottom = monitorInfo.WorkArea.Bottom / scale;
            double workWidth = (monitorInfo.WorkArea.Right - monitorInfo.WorkArea.Left) / scale;

            Left = workLeft + ((workWidth - ActualWidth) / 2.0);
            Top = workBottom - ActualHeight - (BottomOffsetPhysicalPixels / scale);
        }

        private static (string Icon, string BorderBrushKey) GetModeAppearance(byte mode) => mode switch
        {
            0x00 => (ModeIconData.Quiet, "ModeQuietBrush"),
            0x01 => (ModeIconData.Balanced, "ModeBalancedBrush"),
            0x04 => (ModeIconData.Performance, "ModePerformanceBrush"),
            0x05 => (ModeIconData.Turbo, "ModeTurboBrush"),
            0x06 => (ModeIconData.Eco, "ModeEcoBrush"),
            _ => (ModeIconData.Balanced, "AccentBrush")
        };

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public NativeRect Monitor;
            public NativeRect WorkArea;
            public uint Flags;
        }

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(nint windowHandle, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(nint windowHandle, int index, int newLong);

        [DllImport("user32.dll")]
        private static extern nint MonitorFromWindow(nint windowHandle, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo monitorInfo);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(nint windowHandle);
    }
}
