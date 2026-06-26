using System;
using System.Management;
using System.Runtime.Versioning;
using PreySense;

namespace PreySense.Input
{
    [SupportedOSPlatform("windows")]
    public class WmiHotkeyWatcher : IDisposable
    {
        private ManagementEventWatcher? _apgeWatcher;
        private ManagementEventWatcher? _genericWatcher;
        private readonly Action<int> _onHotkeyEvent;
        private bool _isDisposed;

        public WmiHotkeyWatcher(Action<int> onHotkeyEvent)
        {
            _onHotkeyEvent = onHotkeyEvent ?? throw new ArgumentNullException(nameof(onHotkeyEvent));
            StartWatching();
        }

        private void StartWatching()
        {
            // Subscribe to APGeEvent (primary class for most hotkey events)
            try
            {
                var scope = new ManagementScope(@"\\localhost\root\wmi");
                scope.Connect();

                var query = new EventQuery("SELECT * FROM APGeEvent");
                _apgeWatcher = new ManagementEventWatcher(scope, query);
                _apgeWatcher.EventArrived += WmiEventArrived;
                _apgeWatcher.Start();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to start APGeEvent watcher: {ex.Message}");
                AppLogger.Log($"WmiHotkeyWatcher: APGeEvent unavailable: {ex.Message}");
            }

            // Subscribe to AcerGenericEvent (secondary/fallback class)
            try
            {
                var scope = new ManagementScope(@"\\localhost\root\wmi");
                scope.Connect();

                var query = new EventQuery("SELECT * FROM AcerGenericEvent");
                _genericWatcher = new ManagementEventWatcher(scope, query);
                _genericWatcher.EventArrived += WmiEventArrived;
                _genericWatcher.Start();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to start AcerGenericEvent watcher: {ex.Message}");
                AppLogger.Log($"WmiHotkeyWatcher: AcerGenericEvent unavailable: {ex.Message}");
            }
        }

        private void WmiEventArrived(object sender, EventArrivedEventArgs e)
        {
            if (_isDisposed) return;

            try
            {
                var eventObj = e.NewEvent;
                if (eventObj == null) return;

                var detailProp = eventObj.Properties["EventDetail"];
                if (detailProp != null && detailProp.Value != null)
                {
                    int eventDetail = Convert.ToInt32(detailProp.Value);
                    AppLogger.Log($"WmiHotkeyWatcher: {eventObj.ClassPath.ClassName} EventDetail={eventDetail}");
                    _onHotkeyEvent(eventDetail);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling WMI hotkey event: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            try
            {
                if (_apgeWatcher != null)
                {
                    _apgeWatcher.Stop();
                    _apgeWatcher.Dispose();
                }
            }
            catch { }

            try
            {
                if (_genericWatcher != null)
                {
                    _genericWatcher.Stop();
                    _genericWatcher.Dispose();
                }
            }
            catch { }
        }
    }
}
