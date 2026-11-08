using CommunityToolkit.Mvvm.ComponentModel;
using PreySense.Core.Hardware;
using System.Threading.Tasks;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private int? _prochotOffsetCelsius;
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

    internal void UpdateProchotLockState(CpuProchotController.Snapshot? snapshot)
    {
        IsProchotOffsetAvailable = snapshot.HasValue;
        IsProchotOffsetLocked = snapshot?.Locked == true;
        if (!CanToggleProchotOffset) IsProchotOffsetEnabled = false;
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
                });
            UpdateProchotLockState(snapshot);
        }
    }
}
