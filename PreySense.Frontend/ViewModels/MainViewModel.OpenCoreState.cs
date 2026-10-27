using Microsoft.Win32;
using PreySense.Core.Acpi;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    // OpenCore/ACPI state notifications are separate from installation and
    // calibration workflows.
    partial void OnIsOpenCoreLoadedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCalibrateFactoryProfiles));
        CalibrateFactoryProfilesCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(OpenCoreNotLoadedWarningVisibility));
        OnPropertyChanged(nameof(OpenCoreLoadedBannerVisibility));
        OnPropertyChanged(nameof(AcpiOverrideDisabledBannerVisibility));
        OnPropertyChanged(nameof(AcpiOverrideDisabledBannerText));
        OnPropertyChanged(nameof(CanEnableAcpiOverride));
        OnPropertyChanged(nameof(BootDriveActionText));
    }

    public string BootDriveActionText => Services.LocalizationService.Translate(
        IsOpenCorePartitionPresent ? "Uninstall" : "Install");

    partial void OnIsAcpiOverrideEnabledChanged(bool value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\PreySense\AdvancedSettings");
            key?.SetValue("AcpiOverrideEnabled", value ? 1 : 0);
        }
        catch { }

        OnPropertyChanged(nameof(AcpiOverrideDisabledBannerVisibility));
        OnPropertyChanged(nameof(AcpiOverrideDisabledBannerText));
        StatusMessage = value
            ? "ACPI Firmware Override: Enabled (PL1/PL2/Tau unlocked)"
            : "ACPI Firmware Override: Disabled (Controls locked to factory profile)";
    }

    partial void OnIsFactoryCalibrationCompleteChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCalibrateFactoryProfiles));
        CalibrateFactoryProfilesCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanEnableAcpiOverride));
        if (!value && IsAcpiOverrideEnabled)
            IsAcpiOverrideEnabled = false;
    }

    partial void OnIsFactoryCalibrationRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCalibrateFactoryProfiles));
        CalibrateFactoryProfilesCommand.NotifyCanExecuteChanged();
    }

    public void RefreshOpenCoreStatus()
    {
        ApplyOpenCoreIntegrationState(_openCoreIntegrationState);
        RefreshOpenCorePartition();
    }

    private void ApplyOpenCoreIntegrationState(OpenCoreSecurityValidator.OpenCoreIntegrationState state)
    {
        _openCoreIntegrationState = state;
        IsOpenCoreLoaded = state == OpenCoreSecurityValidator.OpenCoreIntegrationState.Active;
        if (!IsOpenCoreLoaded && IsAcpiOverrideEnabled)
            IsAcpiOverrideEnabled = false;
        AcpiOverrideStatus = Services.LocalizationService.Translate(
            OpenCoreSecurityValidator.GetAcpiOverrideLabel(state));
        OnPropertyChanged(nameof(AcpiOverrideDisabledBannerText));
        OnPropertyChanged(nameof(AcpiFilesNotification));
        OnPropertyChanged(nameof(AcpiFilesNotificationVisibility));
    }
}
