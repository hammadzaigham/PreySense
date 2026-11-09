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
