using System.Runtime.InteropServices;
using DtsCnt;

namespace PreySense.Core.Audio
{
    /// <summary>
    /// Talks to the installed DTS control object in dtscnt64.dll.
    /// The audio processor in dtsapo64.dll is left alone.
    /// </summary>
    public sealed partial class DtsAudioManager : IDisposable
    {
        public const string ApoDirectory = @"C:\Windows\System32\DTS\PC\APO4x";
        private const int NotInitialized = unchecked((int)0x801F0007);

        private DTSAPOSystemClass? _system;
        private bool _ready;
        private bool _disposed;

        public bool TryGetEndpoints(out IReadOnlyList<DtsAudioEndpoint> endpoints, out string? error)
        {
            endpoints = Array.Empty<DtsAudioEndpoint>();
            if (!EnsureSystem(out error))
                return false;

            try
            {
                endpoints = ReadEndpoints();
                error = null;
                return true;
            }
            catch (COMException ex) when (ex.HResult == NotInitialized)
            {
                _ready = false;
                if (!EnsureSystem(out error))
                    return false;
                try
                {
                    endpoints = ReadEndpoints();
                    error = null;
                    return true;
                }
                catch (Exception retry)
                {
                    error = retry.Message;
                    AppLogger.Error($"DTS endpoints: {retry.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                AppLogger.Error($"DTS endpoints: {ex.Message}");
                return false;
            }
        }

        public bool TrySetAutomatic(string endpointId, out string? error)
        {
            if (string.IsNullOrWhiteSpace(endpointId))
            {
                error = "DTS endpoint is missing.";
                return false;
            }

            if (!EnsureSystem(out error))
                return false;

            try
            {
                return TryEnableAutomatic(endpointId, out error);
            }
            catch (COMException ex) when (ex.HResult == NotInitialized)
            {
                _ready = false;
                if (!EnsureSystem(out error))
                    return false;
                try
                {
                    return TryEnableAutomatic(endpointId, out error);
                }
                catch (Exception retry)
                {
                    error = retry.Message;
                    AppLogger.Error($"DTS auto: {retry.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                AppLogger.Error($"DTS auto: {ex.Message}");
                return false;
            }
        }

        public bool TrySetPreset(string endpointId, DtsPreset preset, out string? error)
        {
            if (string.IsNullOrWhiteSpace(endpointId))
            {
                error = "DTS endpoint is missing.";
                return false;
            }

            if (!EnsureSystem(out error))
                return false;

            try
            {
                if (!TryApply(endpointId, preset, out error))
                    return false;
                error = null;
                return true;
            }
            catch (COMException ex) when (ex.HResult == NotInitialized)
            {
                _ready = false;
                if (!EnsureSystem(out error))
                    return false;
                try
                {
                    return TryApply(endpointId, preset, out error);
                }
                catch (Exception retry)
                {
                    error = retry.Message;
                    AppLogger.Error($"DTS preset: {retry.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                AppLogger.Error($"DTS preset: {ex.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_system != null)
            {
                try { Marshal.ReleaseComObject(_system); } catch { }
                _system = null;
            }
            _ready = false;
        }

        private bool EnsureSystem(out string? error)
        {
            error = null;
            if (_disposed)
            {
                error = "DTS control has been closed.";
                return false;
            }

            if (_system != null && _ready)
                return true;

            try
            {
                _system ??= new DTSAPOSystemClass();
                _system.InitializeSystem(ApoDirectory);
                _ready = true;
                return true;
            }
            catch (Exception ex)
            {
                _ready = false;
                error = ex.Message;
                AppLogger.Error($"DTS init: {ex.Message}");
                return false;
            }
        }

        private List<DtsAudioEndpoint> ReadEndpoints()
        {
            var list = new List<DtsAudioEndpoint>();
            IDTSEndpointEnumerator enumerator = (IDTSEndpointEnumerator)_system!.EnumerateDTSEndpoints();
            try
            {
                while (true)
                {
                    IDTSEndpointInfo? endpoint = NextEndpoint(enumerator);
                    if (endpoint == null)
                        break;
                    if (TrySnapshot(endpoint, out DtsAudioEndpoint? snapshot) && snapshot != null)
                        list.Add(snapshot);
                }
            }
            finally
            {
                Release(enumerator);
            }

            return list;
        }

        private bool TryApply(string endpointId, DtsPreset preset, out string? error)
        {
            error = null;
            IDTSEndpointEnumerator enumerator = (IDTSEndpointEnumerator)_system!.EnumerateDTSEndpoints();
            try
            {
                while (true)
                {
                    IDTSEndpointInfo? endpoint = NextEndpoint(enumerator);
                    if (endpoint == null)
                        break;
                    if (!string.Equals(endpoint.RegistryPath, endpointId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    IDTSOperatingMode current = (IDTSOperatingMode)endpoint.OpMode;
                    if (!DtsAudioPreset.TryParseModeName(current.Name, out _, out string outputKind))
                    {
                        error = "DTS did not report an output type for this device.";
                        return false;
                    }

                    string fragment = DtsAudioPreset.FormatModeFragment(preset, outputKind);
                    DTSOperatingMode? match = FindMode(endpoint, fragment);
                    if (match == null)
                    {
                        error = "That preset is not available for this device.";
                        return false;
                    }

                    endpoint.OpMode = match;
                    IDTSOperatingMode appliedMode = (IDTSOperatingMode)endpoint.OpMode;
                    if (appliedMode.Name.IndexOf(fragment, StringComparison.Ordinal) < 0)
                    {
                        error = "DTS did not accept the preset.";
                        return false;
                    }

                    appliedMode.AutoContentModeEnable = 0;
                    if (appliedMode.AutoContentModeEnable != 0)
                    {
                        error = "DTS did not turn Auto off.";
                        return false;
                    }

                    return true;
                }
            }
            finally
            {
                Release(enumerator);
            }

            error = "That audio device is no longer available.";
            return false;
        }

        private static bool TrySnapshot(IDTSEndpointInfo endpoint, out DtsAudioEndpoint? snapshot)
        {
            snapshot = null;
            IDTSOperatingMode current = (IDTSOperatingMode)endpoint.OpMode;
            if (!DtsAudioPreset.TryParseModeName(current.Name, out DtsPreset preset, out string outputKind))
                return false;

            snapshot = new DtsAudioEndpoint(
                endpoint.RegistryPath,
                endpoint.FriendlyName ?? "",
                outputKind,
                preset,
                current.APOEnable != 0,
                current.AutoContentModeEnable != 0);
            return true;
        }

        private bool TryEnableAutomatic(string endpointId, out string? error)
        {
            error = null;
            IDTSEndpointEnumerator enumerator = (IDTSEndpointEnumerator)_system!.EnumerateDTSEndpoints();
            bool turnOnAfterMusic = false;
            try
            {
                while (true)
                {
                    IDTSEndpointInfo? endpoint = NextEndpoint(enumerator);
                    if (endpoint == null)
                        break;
                    if (!string.Equals(endpoint.RegistryPath, endpointId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    IDTSOperatingMode current = (IDTSOperatingMode)endpoint.OpMode;
                    if (!DtsAudioPreset.TryParseModeName(current.Name, out DtsPreset preset, out _))
                    {
                        error = "DTS did not report an output type for this device.";
                        return false;
                    }

                    if (preset == DtsPreset.Off)
                    {
                        turnOnAfterMusic = true;
                        break;
                    }

                    return EnableAuto(current, out error);
                }
            }
            finally
            {
                Release(enumerator);
            }

            if (!turnOnAfterMusic)
            {
                error = "That audio device is no longer available.";
                return false;
            }

            if (!TryApply(endpointId, DtsPreset.Music, out error))
                return false;
            return SetAuto(endpointId, out error);
        }

        private bool SetAuto(string endpointId, out string? error)
        {
            error = null;
            IDTSEndpointEnumerator enumerator = (IDTSEndpointEnumerator)_system!.EnumerateDTSEndpoints();
            try
            {
                while (true)
                {
                    IDTSEndpointInfo? endpoint = NextEndpoint(enumerator);
                    if (endpoint == null)
                        break;
                    if (!string.Equals(endpoint.RegistryPath, endpointId, StringComparison.OrdinalIgnoreCase))
                        continue;
                    return EnableAuto((IDTSOperatingMode)endpoint.OpMode, out error);
                }
            }
            finally
            {
                Release(enumerator);
            }

            error = "That audio device is no longer available.";
            return false;
        }

        private static bool EnableAuto(IDTSOperatingMode mode, out string? error)
        {
            mode.AutoContentModeEnable = 1;
            if (mode.AutoContentModeEnable == 0)
            {
                error = "DTS did not turn Auto on.";
                return false;
            }

            error = null;
            return true;
        }

        private static DTSOperatingMode? FindMode(IDTSEndpointInfo endpoint, string fragment)
        {
            IDTSAPOOpModeEnumerator modes = (IDTSAPOOpModeEnumerator)endpoint.GetOpModeEnumerator();
            try
            {
                while (true)
                {
                    IDTSOperatingMode? mode = NextMode(modes);
                    if (mode == null)
                        return null;
                    if (mode.Name.IndexOf(fragment, StringComparison.Ordinal) >= 0)
                        return (DTSOperatingMode)mode;
                }
            }
            finally
            {
                Release(modes);
            }
        }

        private static IDTSEndpointInfo? NextEndpoint(IDTSEndpointEnumerator endpoints)
        {
            try
            {
                object? next = endpoints.Next();
                return next == null ? null : (IDTSEndpointInfo)next;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static IDTSOperatingMode? NextMode(IDTSAPOOpModeEnumerator modes)
        {
            try
            {
                object? next = modes.Next();
                return next == null ? null : (IDTSOperatingMode)next;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void Release(object com)
        {
            try
            {
                if (Marshal.IsComObject(com))
                    Marshal.ReleaseComObject(com);
            }
            catch
            {
            }
        }
    }
}
