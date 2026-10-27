using System.Collections.ObjectModel;
using System.Threading;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using PreySense.Core.Audio;
using PreySense.Frontend.Services;

namespace PreySense.Frontend.ViewModels
{
    public partial class DtsAudioViewModel : ObservableObject
    {
        private readonly Dispatcher _ui;
        private readonly ManualResetEventSlim _comReady = new(false);
        private Dispatcher? _com;
        private DtsAudioManager? _manager;
        private int _refreshing;

        public DtsAudioViewModel(Dispatcher ui)
        {
            _ui = ui;
        }

        private void EnsureComThread()
        {
            if (_com != null)
                return;

            var thread = new Thread(RunComThread)
            {
                IsBackground = true,
                Name = "DTS"
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            _comReady.Wait(TimeSpan.FromSeconds(2));
        }

        public ObservableCollection<DtsAudioEndpointViewModel> Endpoints { get; } = new();

        [ObservableProperty] private bool _isAvailable;
        [ObservableProperty] private string? _error;

        public void RefreshLocalizedText()
        {
            OnPropertyChanged(nameof(Error));
            foreach (DtsAudioEndpointViewModel endpoint in Endpoints)
                endpoint.RefreshLocalizedTitle();
        }

        public void Refresh()
        {
            EnsureComThread();
            if (_com == null || Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0)
                return;

            _com.BeginInvoke(ReadEndpoints);
        }

        private void RunComThread()
        {
            _com = Dispatcher.CurrentDispatcher;
            _comReady.Set();
            Dispatcher.Run();
        }

        private void ReadEndpoints()
        {
            bool ok = false;
            DtsAudioEndpoint[] snapshot = Array.Empty<DtsAudioEndpoint>();
            try
            {
                ok = Manager().TryGetEndpoints(out IReadOnlyList<DtsAudioEndpoint> found, out _);
                if (ok)
                    snapshot = found.ToArray();
            }
            catch (Exception ex)
            {
                AppLogger.Error($"DTS endpoints: {ex.Message}");
            }

            _ui.BeginInvoke(() =>
            {
                try
                {
                    ApplyEndpoints(ok, snapshot);
                }
                finally
                {
                    Interlocked.Exchange(ref _refreshing, 0);
                }
            });
        }

        private void ApplyEndpoints(bool ok, IReadOnlyList<DtsAudioEndpoint> found)
        {
            if (!ok)
            {
                IsAvailable = false;
                Error = null;
                Endpoints.Clear();
                return;
            }

            IsAvailable = found.Count > 0;
            if (!IsAvailable)
            {
                Endpoints.Clear();
                return;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DtsAudioEndpoint endpoint in found)
            {
                seen.Add(endpoint.Id);
                DtsAudioEndpointViewModel? row = Endpoints.FirstOrDefault(item =>
                    string.Equals(item.Id, endpoint.Id, StringComparison.OrdinalIgnoreCase));
                if (row == null)
                    Endpoints.Add(new DtsAudioEndpointViewModel(endpoint, this));
                else
                    row.Apply(endpoint);
            }

            for (int i = Endpoints.Count - 1; i >= 0; i--)
            {
                if (!seen.Contains(Endpoints[i].Id))
                    Endpoints.RemoveAt(i);
            }
        }

        internal bool ApplyPreset(string endpointId, DtsPreset preset, out string? error)
        {
            string? comError = null;
            bool ok = false;
            if (!Invoke(manager => ok = manager.TrySetPreset(endpointId, preset, out comError)))
            {
                error = "DTS control is not ready.";
                Error = error;
                return false;
            }

            error = comError;
            Error = ok ? null : error;
            return ok;
        }

        internal bool ApplyAutomatic(string endpointId, out string? error)
        {
            string? comError = null;
            bool ok = false;
            if (!Invoke(manager => ok = manager.TrySetAutomatic(endpointId, out comError)))
            {
                error = "DTS control is not ready.";
                Error = error;
                return false;
            }

            error = comError;
            Error = ok ? null : error;
            return ok;
        }

        internal bool TryGetCustom(string endpointId, out DtsCustomAudioSettings? settings, out string? error)
        {
            DtsCustomAudioSettings? read = null;
            string? comError = null;
            bool ok = false;
            if (!Invoke(manager => ok = manager.TryGetCustomSettings(endpointId, out read, out comError)))
            {
                settings = null;
                error = "DTS control is not ready.";
                return false;
            }

            settings = read;
            error = comError;
            return ok;
        }

        internal bool TrySetStereo(string endpointId, DtsStereoMode mode, out string? error)
            => Write((manager, out comError) => manager.TrySetStereo(endpointId, mode, out comError), out error);

        internal bool TrySetTrebleEnabled(string endpointId, bool enabled, out string? error)
            => Write((manager, out comError) => manager.TrySetTrebleEnabled(endpointId, enabled, out comError), out error);

        internal bool TrySetTrebleLevel(string endpointId, int level, out string? error)
            => Write((manager, out comError) => manager.TrySetTrebleLevel(endpointId, level, out comError), out error);

        internal bool TrySetBassEnabled(string endpointId, bool enabled, out string? error)
            => Write((manager, out comError) => manager.TrySetBassEnabled(endpointId, enabled, out comError), out error);

        internal bool TrySetBassLevel(string endpointId, int level, out string? error)
            => Write((manager, out comError) => manager.TrySetBassLevel(endpointId, level, out comError), out error);

        internal bool TrySetDialogEnabled(string endpointId, bool enabled, out string? error)
            => Write((manager, out comError) => manager.TrySetDialogEnabled(endpointId, enabled, out comError), out error);

        internal bool TrySetDialogLevel(string endpointId, int level, out string? error)
            => Write((manager, out comError) => manager.TrySetDialogLevel(endpointId, level, out comError), out error);

        internal bool TrySetVolumeSmoothing(string endpointId, bool enabled, out string? error)
            => Write((manager, out comError) => manager.TrySetVolumeSmoothing(endpointId, enabled, out comError), out error);

        internal bool TryGetEqualizer(string endpointId, out DtsEqualizerSettings? settings, out string? error)
        {
            DtsEqualizerSettings? read = null;
            string? comError = null;
            bool ok = false;
            if (!Invoke(manager => ok = manager.TryGetEqualizer(endpointId, out read, out comError)))
            {
                settings = null;
                error = "DTS control is not ready.";
                return false;
            }

            settings = read;
            error = comError;
            return ok;
        }

        internal bool TrySetEqualizerEnabled(string endpointId, bool enabled, out string? error)
            => Write((manager, out comError) => manager.TrySetEqualizerEnabled(endpointId, enabled, out comError), out error);

        internal bool TrySetEqualizerGain(string endpointId, int band, int comGain, out string? error)
            => Write((manager, out comError) => manager.TrySetEqualizerGain(endpointId, band, comGain, out comError), out error);

        internal bool RestoreAllFactory(out string? error)
        {
            string[] ids = Endpoints.Select(endpoint => endpoint.Id).ToArray();
            if (ids.Length == 0)
            {
                error = "No DTS device is available.";
                Error = error;
                return false;
            }

            string? comError = null;
            bool ok = false;
            if (!Invoke(manager =>
                {
                    foreach (string id in ids)
                    {
                        ok = manager.TryRestoreFactory(id, out comError);
                        if (!ok)
                            return;
                    }
                }))
            {
                error = "DTS control is not ready.";
                Error = error;
                return false;
            }

            error = comError;
            Error = ok ? null : error;
            if (ok)
                Refresh();
            return ok;
        }

        public void DisposeManager()
        {
            if (_com == null)
                return;
            try
            {
                _com.Invoke(() => _manager?.Dispose());
                _com.BeginInvokeShutdown(DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"DTS shutdown: {ex.Message}");
            }
        }

        private bool Write(ComWrite call, out string? error)
        {
            string? comError = null;
            bool ok = false;
            if (!Invoke(manager => ok = call(manager, out comError)))
            {
                error = "DTS control is not ready.";
                return false;
            }

            error = comError;
            return ok;
        }

        private delegate bool ComWrite(DtsAudioManager manager, out string? error);

        private bool Invoke(Action<DtsAudioManager> action)
        {
            if (_com == null)
                return false;
            try
            {
                _com.Invoke(() => action(Manager()));
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"DTS: {ex.Message}");
                return false;
            }
        }

        private DtsAudioManager Manager() => _manager ??= new DtsAudioManager();
    }

    public partial class DtsAudioEndpointViewModel : ObservableObject
    {
        private readonly DtsAudioViewModel _owner;
        private bool _suppress;
        private int _appliedIndex;
        private string _sourceTitle = "";

        public DtsAudioEndpointViewModel(DtsAudioEndpoint endpoint, DtsAudioViewModel owner)
        {
            _owner = owner;
            Id = endpoint.Id;
            Presets = DtsAudioPreset.Menu;
            Apply(endpoint);
        }

        public string Id { get; }

        public override string ToString() => Title;

        public IReadOnlyList<string> Presets { get; }

        public bool IsCustom => DtsAudioPreset.TryFromMenuIndex(SelectedPresetIndex, out bool automatic, out DtsPreset preset)
            && !automatic
            && preset == DtsPreset.Custom;

        [ObservableProperty] private string _title = "";

        [ObservableProperty] private int _selectedPresetIndex;

        public void RefreshLocalizedTitle() => Title = LocalizationService.Translate(_sourceTitle);

        public void Apply(DtsAudioEndpoint endpoint)
        {
            _sourceTitle = endpoint.Title;
            Title = LocalizationService.Translate(_sourceTitle);
            int index = DtsAudioPreset.MenuIndex(endpoint.Preset, endpoint.Automatic);
            _appliedIndex = index;
            if (SelectedPresetIndex == index)
                return;
            _suppress = true;
            SelectedPresetIndex = index;
            _suppress = false;
        }

        partial void OnSelectedPresetIndexChanged(int value)
        {
            OnPropertyChanged(nameof(IsCustom));
            if (_suppress || value < 0 || value >= Presets.Count || value == _appliedIndex)
                return;

            if (!DtsAudioPreset.TryFromMenuIndex(value, out bool automatic, out DtsPreset preset))
            {
                RevertPreset();
                return;
            }

            bool ok = automatic
                ? _owner.ApplyAutomatic(Id, out _)
                : _owner.ApplyPreset(Id, preset, out _);
            if (ok)
            {
                _appliedIndex = value;
                return;
            }

            RevertPreset();
        }

        private void RevertPreset()
        {
            _suppress = true;
            SelectedPresetIndex = _appliedIndex;
            _suppress = false;
        }
    }
}
