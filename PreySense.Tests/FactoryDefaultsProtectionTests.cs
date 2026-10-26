using System.IO;

namespace PreySense.Tests;

[TestClass]
public class FactoryDefaultsProtectionTests
{
    private static string Read(string path)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "PreySense.slnx"))) root = root.Parent;
        return File.ReadAllText(Path.Combine(root?.FullName ?? throw new InvalidOperationException(), path));
    }

    [TestMethod]
    public void CalibrationChecksPersistentLockBeforeTouchingHardware()
    {
        string vm = Read("PreySense.Frontend/ViewModels/MainViewModel.Acpi.cs");
        int calibration = vm.IndexOf("public async Task CalibrateFactoryProfilesAsync()", StringComparison.Ordinal);
        int guard = vm.IndexOf("if (FactoryCalibrationIsLocked())", calibration, StringComparison.Ordinal);
        int hardware = vm.IndexOf("byte originalMode = _hw.CurrentPowerMode", calibration, StringComparison.Ordinal);
        Assert.IsTrue(guard > calibration && guard < hardware);
        Assert.Contains("CanExecute = nameof(CanCalibrateFactoryProfiles)", vm);
        Assert.Contains("if ((calibratedMask & entry.Bit) != 0) continue;", vm);
        Assert.Contains("if ((savedMask & mode.Bit) != 0 || (calibratedMask & mode.Bit) == 0) continue;", vm);
        Assert.Contains("key.SetValue(\"CalibrationLocked\", 1)", vm);
        Assert.DoesNotContain("Already calibrated", vm);
        int save = vm.IndexOf("SaveFactoryCalibrationToRegistry(calibratedMask);", calibration, StringComparison.Ordinal);
        int completed = vm.IndexOf("IsFactoryCalibrationComplete = IsPlatformLimitCalibrated", save, StringComparison.Ordinal);
        Assert.IsTrue(save < completed);
    }

    [TestMethod]
    public void FactoryViewIsReadOnlyAndSeparateFromCalibration()
    {
        string vm = Read("PreySense.Frontend/ViewModels/MainViewModel.Acpi.cs");
        int start = vm.IndexOf("public void ViewFactoryDefaults()", StringComparison.Ordinal);
        int end = vm.IndexOf("private string BuildFactoryLimitsMessage", start, StringComparison.Ordinal);
        string viewer = vm[start..end];
        Assert.DoesNotContain("SetValue(", viewer);
        Assert.DoesNotContain("Capture(", viewer);
        Assert.DoesNotContain("SetPowerMode(", viewer);
        Assert.Contains("BuildFactoryLimitsMessage", viewer);
        Assert.Contains("FactoryCalibrationSummary.Build", vm);
        string ui = Read("PreySense.Frontend/Views/AdvancedWindow.xaml");
        Assert.DoesNotContain("CalibrateFactoryProfilesCommand", ui);
        Assert.DoesNotContain("Title=\"Factory Profile Calibration\"", ui);
        Assert.Contains("ViewFactoryDefaultsCommand", ui);
    }

    [TestMethod]
    public void TuningDropdownSwitchesModeButIgnoresProgrammaticSync()
    {
        string window = Read("PreySense.Frontend/Views/ModeSettingsWindow.xaml.cs");
        int start = window.IndexOf("private async void ComboModeSelect_SelectionChanged", StringComparison.Ordinal);
        int end = window.IndexOf("private static string FormatTau", start, StringComparison.Ordinal);
        string handler = window[start..end];
        Assert.Contains("_isUpdatingUi", handler);
        Assert.Contains("!IsLoaded", handler);
        Assert.Contains("SelectedIndex < 0", handler);
        Assert.Contains("await vm.SetModeAsync(ModeIndexToByte(_selectedModeIndex))", handler);
    }

    [TestMethod]
    public void StartupPromptsAfterValidationAndBeforeApplyingSavedLimits()
    {
        string session = Read("PreySense.Frontend/ViewModels/MainViewModel.Session.cs");
        int start = session.IndexOf("private async Task InitializeSessionAsync()", StringComparison.Ordinal);
        int end = session.IndexOf("public async Task RestoreSessionSettingsAsync", start, StringComparison.Ordinal);
        string startup = session[start..end];
        int window = startup.IndexOf("await _startupWindowReady.Task", StringComparison.Ordinal);
        int validation = startup.IndexOf("await ValidateOpenCoreIntegrationAsync()", StringComparison.Ordinal);
        int prompt = startup.IndexOf("await PromptStartupFactoryCalibrationAsync()", StringComparison.Ordinal);
        int restore = startup.IndexOf("await RestoreSessionSettingsAsync(\"startup\")", StringComparison.Ordinal);
        Assert.IsTrue(window >= 0 && window < validation && validation < prompt && prompt < restore);
        Assert.Contains("if (IsFactoryCalibrationComplete && IsOpenCoreLoaded && IsAcpiOverrideEnabled)", session);
        string vm = Read("PreySense.Frontend/ViewModels/MainViewModel.Acpi.cs");
        int promptStart = vm.IndexOf("private async Task PromptStartupFactoryCalibrationAsync()", StringComparison.Ordinal);
        int promptEnd = vm.IndexOf("[RelayCommand", promptStart, StringComparison.Ordinal);
        string body = vm[promptStart..promptEnd];
        Assert.Contains("!IsOpenCoreLoaded || FactoryCalibrationIsLocked()", body);
        Assert.Contains("new Views.CustomModalDialog", body);
        Assert.DoesNotContain("Remind Me Later", body);
        Assert.Contains("Uninstall OpenCore", body);
        Assert.Contains("if (choice == false)", body);
        Assert.Contains("if (UninstallOpenCoreFromEsp())", body);
        Assert.Contains("Application.Current.Shutdown()", body);
        Assert.Contains("while (!_disposed && !IsFactoryCalibrationComplete)", body);
        Assert.Contains("await CalibrateFactoryProfilesAsync()", body);
        Assert.Contains("if (!sample.HasCompleteFirmwareReadback || sample.GpuOffsets is null", vm);
        Assert.Contains("AllowClose = false", vm);
        Assert.Contains("dialog.ShowDialog()", vm);
        string modes = Read("PreySense.Frontend/ViewModels/MainViewModel.PowerModes.cs");
        Assert.Contains("IsFactoryCalibrationRunning || IsOpenCoreLoaded && !IsFactoryCalibrationComplete", modes);
        Assert.Contains("!IsAcpiOverrideEnabled || !IsOpenCoreLoaded || !IsFactoryCalibrationComplete", modes);
        Assert.Contains("vm.NotifyMainWindowLoaded()", Read("PreySense.Frontend/MainWindow.xaml.cs"));
    }

    [TestMethod]
    public void SettingDescriptionsAreInertInSharedControls()
    {
        foreach (string name in new[] { "SettingActionRow", "SettingValueRow", "SettingSwitchRow", "SettingSliderRow", "SettingDropdownRow" })
        {
            string xaml = Read($"PreySense.Frontend/Controls/{name}.xaml");
            Assert.DoesNotContain("Binding Description", xaml);
            Assert.Contains("Binding Title", xaml);
        }
    }
}
