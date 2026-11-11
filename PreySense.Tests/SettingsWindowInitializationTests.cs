using System.Runtime.ExceptionServices;
using System.Diagnostics;
using System.Windows;
using PreySense.Frontend.Controls;
using PreySense.Frontend.Views;

namespace PreySense.Tests;

[TestClass]
[DoNotParallelize]
public class SettingsWindowInitializationTests
{
    [TestMethod]
    public void SettingsWindow_LoadsCompiledXamlAndUsbSliderEventHandlers()
    {
        // WPF Application is a process singleton that survives Shutdown. Isolate this
        // real-resource test so its dispatcher cannot leak into unrelated unit tests.
        if (Environment.GetEnvironmentVariable("PREYSENSE_SETTINGS_SMOKE_CHILD") != "1")
        {
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("vstest");
            start.ArgumentList.Add(typeof(SettingsWindowInitializationTests).Assembly.Location);
            start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" +
                typeof(SettingsWindowInitializationTests).FullName + "." + nameof(SettingsWindow_LoadsCompiledXamlAndUsbSliderEventHandlers));
            start.Environment["PREYSENSE_SETTINGS_SMOKE_CHILD"] = "1";
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30000))
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail("Isolated Settings window test timed out.");
            }
            Assert.AreEqual(0, process.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
            return;
        }
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var app = new PreySense.Frontend.App();
            try
            {
                // Load the real resource dictionaries without running application startup/hardware.
                app.InitializeComponent();
                var window = new SettingsWindow();
                try
                {
                    var slider = (SettingSliderRow)window.FindName("UsbChargingSlider");
                    Assert.AreEqual(0d, slider.Minimum);
                    Assert.AreEqual(30d, slider.Maximum);
                    Assert.AreEqual(10d, slider.TickFrequency);
                    Assert.IsTrue(slider.IsSnapToTickEnabled);
                    Assert.AreEqual("Disabled", slider.ValueText);
                    slider.Value = 20;
                    Assert.AreEqual("20%", slider.ValueText);
                    slider.Value = 0;
                    Assert.AreEqual("Disabled", slider.ValueText);
                    Assert.AreEqual(150d, ((SettingDropdownRow)window.FindName("LanguageSelector")).DropdownWidth);
                    var themes = (SettingDropdownRow)window.FindName("ThemeSelector");
                    Assert.AreEqual(110d, themes.DropdownWidth);
                    CollectionAssert.AreEqual(new[] { "Dark", "Light", "Glass" }, themes.Items
                        .Cast<System.Windows.Controls.ComboBoxItem>().Select(item => (string)item.Content).ToArray());
                    var metrics = new MetricsWindow();
                    try
                    {
                        var overlayThemes = (SettingDropdownRow)metrics.FindName("DropdownOverlayTheme");
                        CollectionAssert.AreEqual(new[] { "Dark", "White" }, overlayThemes.Items
                            .Cast<System.Windows.Controls.ComboBoxItem>().Select(item => (string)item.Content).ToArray());
                    }
                    finally { metrics.Close(); }
                    var original = PreySense.Frontend.Services.ThemeManager.CurrentTheme;
                    try
                    {
                        PreySense.Frontend.Services.ThemeManager.Apply(PreySense.Core.Configuration.ApplicationTheme.Glass, persist: false);
                        var surface = (System.Windows.Media.SolidColorBrush)app.FindResource("WindowBgBrush");
                        if (PreySense.Frontend.Services.ThemeManager.IsGlassSupported)
                        {
                            Assert.IsLessThan((byte)255, surface.Color.A);
                            Assert.AreEqual((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#4508080C"), surface.Color);
                            Assert.AreEqual((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3514151C"),
                                ((System.Windows.Media.SolidColorBrush)app.FindResource("ButtonMainBrush")).Color);
                            foreach (string key in new[] { "WindowShellBgBrush", "ButtonRaisedBgBrush", "SelectedTileGradientBrush", "ButtonHoverBrush", "CardBgBrush" })
                                Assert.IsInstanceOfType<System.Windows.Media.SolidColorBrush>(app.FindResource(key));
                            var separator = (System.Windows.Media.SolidColorBrush)app.FindResource("SeparatorBrush");
                            Assert.AreEqual((byte)40, separator.Color.A);
                            Assert.AreEqual((byte)0, ((System.Windows.Media.SolidColorBrush)app.FindResource("BorderMainBrush")).Color.A);
                            Assert.IsLessThan((byte)255, ((System.Windows.Media.SolidColorBrush)app.FindResource("PopupBgBrush")).Color.A);
                            Assert.AreEqual(((System.Windows.Media.SolidColorBrush)app.FindResource("ButtonRaisedBgBrush")).Color,
                                ((System.Windows.Media.SolidColorBrush)app.FindResource("PerformanceModeActiveBrush")).Color);
                        }
                        var toast = new ModeToastWindow();
                        try
                        {
                            var toastSurface = (System.Windows.Controls.Border)toast.Content;
                            Assert.AreEqual(new Thickness(0), toastSurface.BorderThickness);
                            Assert.IsNull(toastSurface.Effect, "A shadow applied to the translucent capsule produces a second silhouette.");
                        }
                        finally { toast.Close(); }
                        var tuning = new ModeSettingsWindow();
                        try
                        {
                            Assert.AreEqual(new CornerRadius(0), ((System.Windows.Controls.Border)tuning.Content).CornerRadius,
                                "Only DWM should round the outer window; a second shell radius creates a visible rim.");
                            var core = (SettingSliderRow)tuning.FindName("RowGpuCoreClock");
                            var memory = (SettingSliderRow)tuning.FindName("RowGpuMemoryClock");
                            Assert.AreEqual(5d, core.TickFrequency);
                            Assert.AreEqual(5d, core.SmallChange);
                            Assert.AreEqual(100d, memory.TickFrequency);
                            Assert.AreEqual(100d, memory.SmallChange);
                            Assert.IsTrue(core.IsSnapToTickEnabled && memory.IsSnapToTickEnabled);
                            _ = new System.Windows.Interop.WindowInteropHelper(tuning).EnsureHandle();
                            PreySense.Frontend.Services.ThemeManager.Apply(PreySense.Core.Configuration.ApplicationTheme.Dark, persist: false);
                            Assert.AreEqual(new Thickness(0, 0, 0, 1), System.Windows.Shell.WindowChrome.GetWindowChrome(tuning).GlassFrameThickness);
                            Assert.AreEqual((byte)255, ((System.Windows.Media.SolidColorBrush)app.FindResource("WindowBgBrush")).Color.A);
                            Assert.AreEqual((byte)255, ((System.Windows.Media.SolidColorBrush)app.FindResource("WindowShellBgBrush")).Color.A);
                            Assert.IsInstanceOfType<System.Windows.Media.SolidColorBrush>(app.FindResource("ButtonRaisedBgBrush"));
                        }
                        finally { tuning.Close(); }
                    }
                    finally { PreySense.Frontend.Services.ThemeManager.Apply(original, persist: false); }
                }
                finally { window.Close(); }
            }
            catch (Exception ex) { failure = ex; }
            finally { app.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(20)), "Settings window initialization timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
