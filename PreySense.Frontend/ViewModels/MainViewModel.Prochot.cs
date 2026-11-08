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
    private bool _isProchotOffsetLocked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditProchotOffset))]
    [NotifyPropertyChangedFor(nameof(CanToggleProchotOffset))]
    private bool _isProchotOffsetAvailable;

    public bool CanToggleProchotOffset => IsProchotOffsetAvailable && !IsProchotOffsetLocked;
    public bool CanEditProchotOffset => CanToggleProchotOffset && IsProchotOffsetEnabled;

    public string ProchotActivationLabel =>
        ResolveTjMaxCelsius() is int tjMax
            ? $"PROCHOT {tjMax - (ProchotOffsetCelsius ?? TuningStore.GetCurrent().Tuning.FactoryProchotOffsetCelsius ?? 0)}°C"
            : string.Empty;

    public string TjMaxLabel =>
        ResolveTjMaxCelsius() is int tjMax ? $"TjMax {tjMax}°C" : string.Empty;

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
