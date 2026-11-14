using CommunityToolkit.Mvvm.ComponentModel;
using PreySense.Core.Hardware;
using System.Threading.Tasks;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProchotActivationLabel))]
    [NotifyPropertyChangedFor(nameof(TjMaxLabel))]
    private int? _prochotOffsetCelsius;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProchotActivationLabel))]
    [NotifyPropertyChangedFor(nameof(TjMaxLabel))]
    private int? _tjMaxCelsius;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditProchotOffset))]
    private bool _isProchotOffsetEnabled;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditProchotOffset))]
    [NotifyPropertyChangedFor(nameof(CanToggleProchotOffset))]
    [NotifyPropertyChangedFor(nameof(ProchotActivationLabel))]
    private bool _isProchotOffsetLocked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditProchotOffset))]
    [NotifyPropertyChangedFor(nameof(CanToggleProchotOffset))]
    private bool _isProchotOffsetAvailable;

    public bool CanToggleProchotOffset => IsProchotOffsetAvailable && !IsProchotOffsetLocked;
    public bool CanEditProchotOffset => CanToggleProchotOffset && IsProchotOffsetEnabled;

    public string ProchotActivationLabel
    {
        get
        {
            string label = ResolveTjMaxCelsius() is int tjMaxCelsius && TuningEditorProchotOffset is int offsetCelsius
                ? $"PROCHOT {tjMaxCelsius - offsetCelsius}°C"
                : "PROCHOT —";
            return IsProchotOffsetLocked ? $"{label} - {Services.LocalizationService.Translate("Locked")}" : label;
        }
    }

    public string TjMaxLabel =>
        ResolveTjMaxCelsius() is int tjMaxCelsius ? $"Tj-Max {tjMaxCelsius}°C" : string.Empty;

    private int? ResolveTjMaxCelsius() =>
        TjMaxCelsius ?? TuningStore.GetCurrent().Tuning.FactoryTjMaxCelsius;

    internal void UpdateProchotLockState(CpuProchotController.Snapshot? snapshot)
    {
        IsProchotOffsetAvailable = snapshot.HasValue;
        IsProchotOffsetLocked = snapshot?.Locked == true;
        TjMaxCelsius = snapshot?.TjMaxCelsius ?? TuningStore.GetCurrent().Tuning.FactoryTjMaxCelsius;
        if (!CanToggleProchotOffset) IsProchotOffsetEnabled = false;
        OnPropertyChanged(nameof(ProchotActivationLabel));
        OnPropertyChanged(nameof(TjMaxLabel));
    }

    internal bool SaveProchotSettings()
    {
        if (ProchotOffsetCelsius is < 0 or > 63 || (IsProchotOffsetEnabled && ProchotOffsetCelsius is null))
        {
            DiagnosticMessage = "Enter a PROCHOT offset between 0 and 63°C, or turn its switch off.";
            return false;
        }
        try
        {
            TuningStore.Update(cfg =>
            {
                cfg.Tuning.ProchotOffsetCelsius = ProchotOffsetCelsius;
                cfg.Tuning.ApplyProchotOffset = IsProchotOffsetEnabled && !IsProchotOffsetLocked;
            });
            DiagnosticMessage = "PROCHOT settings saved.";
            return true;
        }
        catch (System.Exception ex) { DiagnosticMessage = $"Could not save PROCHOT: {ex.Message}"; return false; }
    }

    internal async Task RefreshProchotLockStateAsync()
    {
        var snapshot = await Task.Run(() =>
        {
            var backend = CpuDriverRuntime.Backend;
            return backend is null ? null : new CpuProchotController(backend).Read();
        });
        if (!_disposed)
        {
            if (snapshot is { } value)
                TuningStore.Update(cfg =>
                {
                    cfg.Tuning.FactoryProchotOffsetLocked = value.Locked;
                    cfg.Tuning.FactoryProchotOffsetCelsius ??= value.OffsetCelsius;
                    cfg.Tuning.FactoryTjMaxCelsius ??= value.TjMaxCelsius;
                });
            UpdateProchotLockState(snapshot);
        }
    }
}
