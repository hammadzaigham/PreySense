using System;
using System.IO;
using System.Reflection;

namespace PawnIO
{
    public sealed class IntelMsr : IDisposable
    {
        private const uint MSR_RAPL_POWER_UNIT   = 0x606;
        private const uint MSR_PKG_ENERGY_STATUS = 0x611;

        private readonly PawnIOWrapper _io = new();
        private bool _init;
        private double _energyUnit; 
        private uint _lastEnergy;
        private long _lastTick;

        public bool IsInitialized => _init;

        public bool Initialize(Assembly assembly)
        {
            string name = assembly.GetName().Name + ".IntelMSR.bin";
            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Embedded resource '{name}' not found.");
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return Initialize(ms.ToArray());
        }

        public bool Initialize(byte[] moduleData)
        {
            if (_init) return true;
            if (_io.Connect() != PawnIOWrapper.ConnectResult.OK || !_io.LoadModule(moduleData)) return false;

            if (!ReadMsr(MSR_RAPL_POWER_UNIT, out ulong unit)) return false;
            int esu = (int)((unit >> 8) & 0x1F);   // energy status units, bits [12:8]
            _energyUnit = 1.0 / (1UL << esu);

            _init = true;
            return true;
        }

        public bool TryInitialize(byte[] moduleData, out string error)
        {
            error = string.Empty;

            if (_init)
                return true;

            var connectResult = _io.Connect();
            if (connectResult != PawnIOWrapper.ConnectResult.OK)
            {
                error = connectResult switch
                {
                    PawnIOWrapper.ConnectResult.NotInstalled => "PawnIO driver/device was not found. Install the PawnIO driver first.",
                    PawnIOWrapper.ConnectResult.AccessDenied => "Access denied while opening PawnIO. Run the app as Administrator.",
                    _ => $"PawnIO connect failed: {connectResult}"
                };
                return false;
            }

            if (!_io.LoadModule(moduleData))
            {
                error = "PawnIO driver connected, but loading IntelMSR.bin failed.";
                return false;
            }

            if (!ReadMsr(MSR_RAPL_POWER_UNIT, out ulong unit))
            {
                error = "PawnIO loaded, but MSR 0x606 could not be read.";
                return false;
            }

            int esu = (int)((unit >> 8) & 0x1F);
            _energyUnit = 1.0 / (1UL << esu);

            _init = true;
            return true;
        }

        public static bool IsPawnIoAvailable(out string reason)
        {
            using var io = new PawnIOWrapper();
            var result = io.Connect();
            reason = result switch
            {
                PawnIOWrapper.ConnectResult.OK => string.Empty,
                PawnIOWrapper.ConnectResult.NotInstalled => "PawnIO driver/device was not found.",
                PawnIOWrapper.ConnectResult.AccessDenied => "Access denied while opening PawnIO.",
                _ => $"PawnIO connect failed: {result}"
            };
            return result == PawnIOWrapper.ConnectResult.OK;
        }

        public float? GetPackagePower()
        {
            if (!_init || !ReadMsr(MSR_PKG_ENERGY_STATUS, out ulong raw)) return null;

            uint energy = (uint)raw;
            long tick = Environment.TickCount64;

            if (_lastTick == 0) { _lastEnergy = energy; _lastTick = tick; return null; }

            double seconds = (tick - _lastTick) / 1000.0;
            if (seconds < 0.05) return null;

            double joules = unchecked(energy - _lastEnergy) * _energyUnit; 
            _lastEnergy = energy;
            _lastTick = tick;

            return (float)(joules / seconds);
        }

        public bool ReadMsr(uint msr, out ulong value)
        {
            value = 0;
            var output = new ulong[1];
            if (!_io.Execute("ioctl_read_msr", new ulong[] { msr }, output)) return false;
            value = output[0];
            return true;
        }

        public bool WriteMsr(uint msr, ulong value)
        {
            if (!_init) return false;
            return _io.Execute("ioctl_write_msr", new ulong[] { msr, value }, null);
        }

        public void Dispose() => _io.Dispose();
    }
}
