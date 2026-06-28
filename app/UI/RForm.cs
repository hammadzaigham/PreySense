using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace PreySense.UI
{
    internal static class AppConfig
    {
        public static string? GetString(string key) => null;
    }

    public class RForm : Form
    {
        public static Color colorEco = Color.FromArgb(255, 6, 180, 138);
        public static Color colorStandard = Color.FromArgb(255, 58, 174, 239);
        public static Color colorTurbo = Color.FromArgb(255, 255, 32, 32);
        public static Color colorCustom = Color.FromArgb(255, 255, 128, 0);
        public static Color colorGray = Color.FromArgb(255, 168, 168, 168);

        public static Color buttonMain;
        public static Color buttonSecond;

        public static Color formBack;
        public static Color foreMain;
        public static Color borderMain;
        public static Color borderSecond;
        public static Color chartMain;
        public static Color chartGrid;

        public static bool flatTheme = false;

        [DllImport("UXTheme.dll", SetLastError = true, EntryPoint = "#138")]
        public static extern bool CheckSystemDarkModeStatus();

        [DllImport("UXTheme.dll", SetLastError = true, EntryPoint = "#135")]
        private static extern int SetPreferredAppMode(int preferredAppMode);

        [DllImport("UXTheme.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(nint hWnd, string pszSubAppName, string? pszSubIdList);

        [DllImport("DwmApi")]
        private static extern int DwmSetWindowAttribute(nint hwnd, int attr, int[] attrValue, int attrSize);

        public bool darkTheme = false;
        private bool themeInitialized = false;

        public RForm()
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            Disposed += (sender, args) =>
            {
                SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            };
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General)
            {
                if (InvokeRequired)
                {
                    try { BeginInvoke(new Action(() => RefreshThemeIfChanged())); } catch { }
                }
                else
                {
                    RefreshThemeIfChanged();
                }
            }
        }

        private void RefreshThemeIfChanged()
        {
            bool themeChanged = InitTheme(false);
            if (themeChanged)
            {
                Invalidate(true);
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var parms = base.CreateParams;
                parms.Style &= ~0x02000000;  // Turn off WS_CLIPCHILDREN
                parms.ClassStyle &= ~0x00020000;
                return parms;
            }
        }

        public static void InitColors(bool darkTheme)
        {
            flatTheme = AppConfig.GetString("theme")?.ToLower() == "flat";

            if (darkTheme)
            {
                buttonMain = Color.FromArgb(255, 46, 46, 46);
                buttonSecond = Color.FromArgb(255, 36, 36, 36);

                formBack = Color.FromArgb(255, 28, 28, 28);
                foreMain = Color.FromArgb(255, 240, 240, 240);
                borderMain = Color.FromArgb(255, 55, 55, 55);
                borderSecond = Color.FromArgb(255, 42, 42, 42);

                chartMain = Color.FromArgb(255, 35, 35, 35);
                chartGrid = Color.FromArgb(255, 70, 70, 70);
            }
            else
            {
                buttonMain = Color.FromArgb(255, 220, 220, 220);
                buttonSecond = Color.FromArgb(255, 230, 230, 230);

                formBack = Color.FromArgb(255, 240, 240, 240);
                foreMain = Color.FromArgb(255, 20, 20, 20);
                borderMain = Color.FromArgb(255, 200, 200, 200);
                borderSecond = Color.FromArgb(255, 215, 215, 215);

                chartMain = Color.FromArgb(255, 250, 250, 250);
                chartGrid = Color.FromArgb(255, 220, 220, 220);
            }
        }

        private static bool IsDarkTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key != null)
                {
                    object? val = key.GetValue("AppsUseLightTheme");
                    if (val != null)
                    {
                        return Convert.ToInt32(val) == 0;
                    }
                }
            }
            catch { }
            return true; // Default to dark theme
        }

        public bool InitTheme(bool setDPI = false)
        {
            bool newDarkTheme = IsDarkTheme();
            bool changed = darkTheme != newDarkTheme;
            bool firstInit = !themeInitialized;
            darkTheme = newDarkTheme;
            themeInitialized = true;

            UiTheme.InitializeTheme();
            InitColors(darkTheme);
            BackColor = formBack;
            ForeColor = foreMain;
            DoubleBuffered = true;

            if (setDPI)
                ControlHelper.Resize(this);

            if (changed || firstInit)
            {
                if (darkTheme)
                {
                    SetPreferredAppMode(1);
                    SetWindowTheme(Handle, "DarkMode_Explorer", null);
                }
                else
                {
                    SetPreferredAppMode(2);
                    SetWindowTheme(Handle, "Explorer", null);
                }
                ApplyCaptionColors();
                ShowIcon = ShowWindowIcon;
                ShowInTaskbar = ShowWindowInTaskbar;
                if (!ShowWindowIcon)
                {
                    Icon = null;
                }
                ControlHelper.Adjust(this, changed);
                this.Invalidate();
            }

            return changed;
        }

        protected virtual bool ShowWindowIcon => false;
        protected virtual bool ShowWindowInTaskbar => false;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (themeInitialized)
                ApplyCaptionColors();
        }

        private void ApplyCaptionColors()
        {
            try
            {
                int captionColor = ToColorRef(formBack);
                int textColor = ToColorRef(foreMain);
                int borderColor = ToColorRef(borderSecond);

                DwmSetWindowAttribute(Handle, 35, new[] { captionColor }, 4);
                DwmSetWindowAttribute(Handle, 36, new[] { textColor }, 4);
                DwmSetWindowAttribute(Handle, 37, new[] { borderColor }, 4);
            }
            catch
            {
            }
        }

        private static int ToColorRef(Color color) =>
            color.R | (color.G << 8) | (color.B << 16);
    }
}
