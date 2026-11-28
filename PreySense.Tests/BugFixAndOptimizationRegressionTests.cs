using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PreySense.Core.Configuration;
using PreySense.Core.Services;
using PreySense.Frontend.Controls;
using PreySense.Frontend.ViewModels;
using PreySense.Mode;
using PreySense.Overlay;

namespace PreySense.Tests;

[TestClass]
public class BugFixAndOptimizationRegressionTests
{
    [TestMethod]
    public void EtwFpsMonitor_SwitchingFromDxgiToDxgKrnlPid_RecordsFirstFrameAndSamplesFps()
    {
        using var monitor = new EtwFpsMonitor();
        long now = Stopwatch.GetTimestamp();
        long freq = Stopwatch.Frequency;

        var record = typeof(EtwFpsMonitor).GetMethod("RecordPresentTimestamp", BindingFlags.Instance | BindingFlags.NonPublic)!;

        // First PID (100) uses DXGI present events
        monitor.TargetPid = 100;
        record.Invoke(monitor, [100, true, now - freq / 2]);
        record.Invoke(monitor, [100, true, now - freq / 4]);
        Assert.IsTrue(double.IsFinite(monitor.SampleFps()));

        // Switch to second PID (200) which only emits DxgKrnl flip events (e.g. Vulkan)
        monitor.TargetPid = 200;
        record.Invoke(monitor, [200, false, now - freq / 10]);
        record.Invoke(monitor, [200, false, now]);

        double fps = monitor.SampleFps();
        Assert.IsTrue(double.IsFinite(fps) && fps > 0, "DxgKrnl frames for new PID must not be ignored after a DXGI PID.");
    }

    [TestMethod]
    public void HardwareOverlay_PowerLimitHistory_TracksCpuAndGpuWithoutMutatingEmptyHistory()
    {
        using var overlay = new HardwareOverlay();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var staticFlags = BindingFlags.Static | BindingFlags.NonPublic;
        var historyFor = typeof(HardwareOverlay).GetMethod("HistoryFor", flags)!;
        var emptyHistory = (float[])typeof(HardwareOverlay).GetField("EmptyHistory", staticFlags)!.GetValue(null)!;

        var cpuPlHistory = (float[])historyFor.Invoke(overlay, ["Power Limit", false])!;
        var gpuPlHistory = (float[])historyFor.Invoke(overlay, ["Power Limit", true])!;
        Assert.AreNotSame(emptyHistory, cpuPlHistory);
        Assert.AreNotSame(emptyHistory, gpuPlHistory);
        Assert.AreNotSame(cpuPlHistory, gpuPlHistory);
    }

    private sealed class OneWaySource
    {
        public bool Enabled { get; set; } = true;
        public double Cutoff { get; set; } = 10;
    }

    [TestMethod]
    public void SettingSwitchRow_And_SettingSliderRow_PreserveOuterOneWayBindings()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var source = new OneWaySource();

                var switchRow = new SettingSwitchRow { DataContext = source };
                BindingOperations.SetBinding(switchRow, SettingSwitchRow.IsCheckedProperty,
                    new Binding(nameof(OneWaySource.Enabled)) { Mode = BindingMode.OneWay });
                Assert.IsTrue(switchRow.IsChecked.GetValueOrDefault());

                var innerSwitch = (RectangularSwitch)switchRow.FindName("InnerSwitch")!;
                innerSwitch.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, false);
                Assert.IsNotNull(BindingOperations.GetBindingExpression(switchRow, SettingSwitchRow.IsCheckedProperty),
                    "Toggling InnerSwitch must not overwrite an outer OneWay binding.");

                source.Enabled = true;
                BindingOperations.GetBindingExpression(switchRow, SettingSwitchRow.IsCheckedProperty)!.UpdateTarget();
                Assert.IsTrue(switchRow.IsChecked.GetValueOrDefault());

                var sliderRow = new SettingSliderRow { DataContext = source, Minimum = 0, Maximum = 30 };
                BindingOperations.SetBinding(sliderRow, SettingSliderRow.ValueProperty,
                    new Binding(nameof(OneWaySource.Cutoff)) { Mode = BindingMode.OneWay });
                var innerSlider = (Slider)sliderRow.FindName("InnerSlider")!;
                innerSlider.SetCurrentValue(System.Windows.Controls.Primitives.RangeBase.ValueProperty, 20d);
                Assert.AreEqual(20d, sliderRow.Value, 0.001);
                Assert.IsNotNull(BindingOperations.GetBindingExpression(sliderRow, SettingSliderRow.ValueProperty),
                    "Moving InnerSlider must not overwrite an outer OneWay binding.");
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(10)));
        if (failure is not null) throw new AssertFailedException($"OneWay binding preservation failed: {failure}", failure);
    }

    [TestMethod]
    public void PreySenseConfig_Clone_DeepCopiesAllSectionsIndependently()
    {
        var original = new PreySenseConfig();
        original.Tuning.Turbo.CpuPl1Watts = 95;
        original.Tuning.FactoryDefaults["Turbo"] = new ModeTuningConfig { CpuPl1Watts = 85 };
        original.Fans.Balanced.Stages = [new FanStageConfig { CpuTempC = 55, CpuDutyPercent = 35 }];
        original.Lighting.PowerModeColors["5"] = "#112233";
        original.Lighting.Devices["kbd"] = new DeviceLightingConfig
        {
            ColorHex = "#445566",
            LedColors = new Dictionary<int, string> { [0] = "#778899" }
        };
        original.Overlay.Slots.Add(new OverlaySlotConfig { CpuMetric = "Temperature", GpuMetric = "Temperature" });
        original.Games.Profiles.Add(new GameProfile { ExecutablePath = @"C:\Games\test.exe", PowerMode = 5, ApplyCpuBoost = true });

        var clone = original.Clone();
        clone.Tuning.Turbo.CpuPl1Watts = 50;
        clone.Tuning.FactoryDefaults["Turbo"].CpuPl1Watts = 45;
        clone.Fans.Balanced.Stages![0].CpuDutyPercent = 90;
        clone.Lighting.PowerModeColors["5"] = "#FFFFFF";
        clone.Lighting.Devices["kbd"].LedColors[0] = "#000000";
        clone.Overlay.Slots[0].CpuMetric = "None";
        clone.Games.Profiles[0].ApplyCpuBoost = false;

        Assert.AreEqual(95, original.Tuning.Turbo.CpuPl1Watts);
        Assert.AreEqual(85, original.Tuning.FactoryDefaults["Turbo"].CpuPl1Watts);
        Assert.AreEqual(35, original.Fans.Balanced.Stages![0].CpuDutyPercent);
        Assert.AreEqual("#112233", original.Lighting.PowerModeColors["5"]);
        Assert.AreEqual("#778899", original.Lighting.Devices["kbd"].LedColors[0]);
        Assert.AreNotEqual("None", original.Overlay.Slots[0].CpuMetric);
        Assert.IsTrue(original.Games.Profiles[0].ApplyCpuBoost);
    }

    [TestMethod]
    public void FanStage_EnforceStrictlyIncreasing_HandlesHighClusterWithoutCollidingAt126()
    {
        int[] temps = [124, 125, 125, 126];
        FanStage.EnforceStrictlyIncreasing(temps);
        CollectionAssert.AreEqual(new[] { 123, 124, 125, 126 }, temps);
    }
}
