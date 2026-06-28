using System;
using System.Diagnostics;
using System.IO;

namespace PreySense.Mode
{
    public class PowerLimitController
    {
        private const uint MSR_RAPL_POWER_UNIT = 0x606;
        private const uint MSR_PKG_POWER_LIMIT = 0x610;

        // Bit 63 of MSR 0x610: hardware write-lock set by firmware.
        // Once set, all WRMSR calls are silently dropped until next reboot.
        private const ulong MSR_LOCK_BIT = 1UL << 63;

        // Cached IntelMsr instance — the PawnIO kernel module is loaded once
        // and reused for all subsequent reads/writes. Recreated if it goes stale.
        private static PawnIO.IntelMsr? _msr;
        private static readonly object _msrLock = new();

        /// <summary>
        /// Returns the shared IntelMsr, initializing it if needed.
        /// Returns null if the PawnIO driver is unavailable.
        /// </summary>
        private static PawnIO.IntelMsr? GetMsr()
        {
            lock (_msrLock)
            {
                if (_msr != null && _msr.IsInitialized)
                    return _msr;

                // Previous instance is stale — dispose and recreate.
                try { _msr?.Dispose(); } catch { }
                _msr = null;

                var candidate = new PawnIO.IntelMsr();
                try
                {
                    if (candidate.Initialize(typeof(PowerLimitController).Assembly))
                    {
                        _msr = candidate;
                        return _msr;
                    }
                }
                catch { }

                try { candidate.Dispose(); } catch { }
                return null;
            }
        }

        /// <summary>
        /// Applies sustained (PL1) and boost (PL2) power limits using MSR 0x610.
        ///
        /// Intel keeps the same package-power-limit register layout on newer generations:
        /// - bits 0..14   = PL1 power limit
        /// - bit 15       = PL1 enable
        /// - bit 16       = PL1 clamp
        /// - bits 17..23  = PL1 time window
        /// - bits 32..46  = PL2 power limit
        /// - bit 47       = PL2 enable
        /// - bit 48       = PL2 clamp
        /// - bit 63       = write-lock (hardware, sticky until reboot)
        ///
        /// We preserve the rest of the register so BIOS-set bits such as the lock bit remain intact.
        /// </summary>
        public static void SetCpuPowerLimits(
            int pl1Watts,
            int pl2Watts,
            bool pl2Enable = true)
        {
            if (AreCpuPowerLimitsAlreadyApplied(pl1Watts, pl2Watts, pl2Enable))
            {
                AppLogger.Log($"[Intel RAPL] CPU power limits already applied; skipped write (PL1={pl1Watts}W, PL2={pl2Watts}W, PL2Enable={pl2Enable}).");
                return;
            }

            TrySetLimitsViaPawnIo(pl1Watts, pl2Watts, pl2Enable);
        }

        private static bool TrySetLimitsViaPawnIo(int pl1Watts, int pl2Watts, bool pl2Enable)
        {
            try
            {
                var msr = GetMsr();
                if (msr != null)
                {
                    if (msr.ReadMsr(MSR_RAPL_POWER_UNIT, out ulong unitRaw))
                    {
                        int powerUnits = (int)(unitRaw & 0x0F);
                        double powerScale = 1.0 / (1 << powerUnits);

                        if (msr.ReadMsr(MSR_PKG_POWER_LIMIT, out ulong currentVal))
                        {
                            // Bail early from the direct WRMSR path when firmware has write-locked it.
                            // Other tools may still succeed through XTU services, MMIO, or OEM firmware paths.
                            if ((currentVal & MSR_LOCK_BIT) != 0)
                            {
                                AppLogger.Log("[Intel RAPL] PawnIO MSR 0x610 is write-locked (bit 63). Direct MSR write skipped.");
                                return false;
                            }

                            uint pl1Units = (uint)Math.Round(pl1Watts / powerScale);
                            uint pl2Units = (uint)Math.Round(pl2Watts / powerScale);

                            ulong newVal = currentVal;

                            // Clear only PL1 power/enable/clamp. Bits 17..23 (tau) are left untouched.
                            newVal &= ~0x000000000001FFFFUL;
                            newVal |= (pl1Units & 0x7FFFUL);
                            newVal |= (1UL << 15); // PL1 Enable
                            newVal |= (1UL << 16); // PL1 Clamp

                            // Clear only PL2 fields so any lock bit or BIOS-reserved bits survive.
                            newVal &= ~0x0001FFFF00000000UL;
                            newVal |= ((ulong)(pl2Units & 0x7FFFUL) << 32);
                            if (pl2Enable)
                            {
                                newVal |= (1UL << 47); // PL2 Enable
                                newVal |= (1UL << 48); // PL2 Clamp
                            }
                            else
                            {
                                newVal &= ~(1UL << 47);
                                newVal &= ~(1UL << 48);
                            }

                            bool ok = msr.WriteMsr(MSR_PKG_POWER_LIMIT, newVal);
                            if (ok && msr.ReadMsr(MSR_PKG_POWER_LIMIT, out ulong verifyVal))
                            {
                                const ulong pl1Mask = 0x0000000000FFFFFFUL; // PL1 power, enable, clamp, time window
                                const ulong pl2Mask = 0x0001FFFF00000000UL; // PL2 power, enable, clamp

                                AppLogger.Log(
                                    $"[Intel RAPL] PawnIO MSR 0x610 write={ok}, readback=0x{verifyVal:X16}, target=0x{newVal:X16}, " +
                                    $"PL1Match={((verifyVal & pl1Mask) == (newVal & pl1Mask))}, " +
                                    $"PL2Match={((verifyVal & pl2Mask) == (newVal & pl2Mask))} " +
                                    $"(PL1: {pl1Watts}W, PL2: {pl2Watts}W, PL2Enable: {pl2Enable})");

                                if ((verifyVal & pl1Mask) == (newVal & pl1Mask) &&
                                    (verifyVal & pl2Mask) == (newVal & pl2Mask))
                                    return true; // Success confirmed by readback.
                            }
                            else
                            {
                                AppLogger.Log($"[Intel RAPL] PawnIO MSR 0x610 write succeeded but readback failed, target=0x{newVal:X16} (PL1: {pl1Watts}W, PL2: {pl2Watts}W, PL2Enable: {pl2Enable})");
                            }
                        }
                    }
                }
                else
                {
                    AppLogger.Log("[Intel RAPL] PawnIO MSR unavailable.");
                }
            }
            catch (Exception ex)
            {
                // The cached instance may have gone stale (e.g. after sleep/wake).
                // Invalidate it so the next call reinitializes cleanly.
                lock (_msrLock) { try { _msr?.Dispose(); } catch { } _msr = null; }
                AppLogger.Log($"[Intel RAPL] PawnIO MSR write failed: {ex.Message}");
            }

            return false;
        }


        public static string GetCpuPowerLimitDiagnostic(
            int pl1Watts,
            int pl2Watts,
            bool pl2Enable = true)
        {
            try
            {
                using var msr = new PawnIO.IntelMsr();
                var asm = typeof(PowerLimitController).Assembly;
                string resourceName = asm.GetName().Name + ".IntelMSR.bin";
                using var moduleStream = asm.GetManifestResourceStream(resourceName);
                if (moduleStream == null)
                    return $"Embedded resource '{resourceName}' not found. Check the project file.";

                using var moduleMs = new MemoryStream();
                moduleStream.CopyTo(moduleMs);

                if (!msr.TryInitialize(moduleMs.ToArray(), out string initError))
                    return $"Intel MSR module could not be initialized: {initError}";

                if (!msr.ReadMsr(MSR_RAPL_POWER_UNIT, out ulong unitRaw))
                    return "Failed to read MSR 0x606.";

                if (!msr.ReadMsr(MSR_PKG_POWER_LIMIT, out ulong currentVal))
                    return "Failed to read MSR 0x610.";

                int powerUnits = (int)(unitRaw & 0x0F);
                double powerScale = 1.0 / (1 << powerUnits);

                uint pl1Units = (uint)Math.Round(pl1Watts / powerScale);
                uint pl2Units = (uint)Math.Round(pl2Watts / powerScale);

                ulong targetVal = currentVal;
                targetVal &= ~0x000000000001FFFFUL;
                targetVal |= (pl1Units & 0x7FFFUL);
                targetVal |= (1UL << 15);
                targetVal |= (1UL << 16);

                targetVal &= ~0x0001FFFF00000000UL;
                targetVal |= ((ulong)(pl2Units & 0x7FFFUL) << 32);
                if (pl2Enable)
                {
                    targetVal |= (1UL << 47);
                    targetVal |= (1UL << 48);
                }
                else
                {
                    targetVal &= ~(1UL << 47);
                    targetVal &= ~(1UL << 48);
                }

                bool wrote = msr.WriteMsr(MSR_PKG_POWER_LIMIT, targetVal);
                ulong verifyVal = 0;
                bool readBackOk = wrote && msr.ReadMsr(MSR_PKG_POWER_LIMIT, out verifyVal);

                const ulong pl1Mask = 0x0000000000FFFFFFUL;
                const ulong pl2Mask = 0x0001FFFF00000000UL;

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"MSR 0x606 raw: 0x{unitRaw:X16}");
                sb.AppendLine($"MSR 0x610 current: 0x{currentVal:X16}");
                sb.AppendLine($"Target: 0x{targetVal:X16}");
                sb.AppendLine($"Write returned: {wrote}");

                if (readBackOk)
                {
                    sb.AppendLine($"Readback: 0x{verifyVal:X16}");
                    sb.AppendLine($"PL1 match: {(verifyVal & pl1Mask) == (targetVal & pl1Mask)}");
                    sb.AppendLine($"PL2 match: {(verifyVal & pl2Mask) == (targetVal & pl2Mask)}");
                }
                else
                {
                    sb.AppendLine("Readback failed after write.");
                }

                sb.AppendLine($"Decoded input: PL1={pl1Watts}W, PL2={pl2Watts}W, PL2Enable={pl2Enable}");
                sb.AppendLine($"Units: power=1/{1 << powerUnits}W");

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"Diagnostic failed: {ex.Message}";
            }
        }

        /// <summary>
        /// Toggles CPU Turbo Boost using hidden Windows Power Configuration attributes (PerfBoostMode).
        /// This is the exact method G-Helper uses to disable CPU Boost and drastically save battery.
        /// </summary>
        public static void SetCpuBoost(int value)
        {
            RunPowerCfg($"-setacvalueindex scheme_current sub_processor perfboostmode {value}");
            RunPowerCfg($"-setdcvalueindex scheme_current sub_processor perfboostmode {value}");
            RunPowerCfg("-setactive scheme_current");
        }

        private static void RunPowerCfg(string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powercfg",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi)?.WaitForExit();
            }
            catch { }
        }



        private static bool AreCpuPowerLimitsAlreadyApplied(int pl1Watts, int pl2Watts, bool pl2Enable)
        {
            try
            {
                var msr = GetMsr();
                if (msr == null)
                    return false;

                if (!msr.ReadMsr(MSR_RAPL_POWER_UNIT, out ulong unitRaw) ||
                    !msr.ReadMsr(MSR_PKG_POWER_LIMIT, out ulong currentVal))
                    return false;

                int powerUnits = (int)(unitRaw & 0x0F);
                double powerScale = 1.0 / (1 << powerUnits);
                uint targetPl1Units = (uint)Math.Round(pl1Watts / powerScale);
                uint targetPl2Units = (uint)Math.Round(pl2Watts / powerScale);

                uint currentPl1Units = (uint)(currentVal & 0x7FFFUL);
                uint currentPl2Units = (uint)((currentVal >> 32) & 0x7FFFUL);
                bool currentPl1Enabled = (currentVal & (1UL << 15)) != 0;
                bool currentPl2Enabled = (currentVal & (1UL << 47)) != 0;

                return currentPl1Units == (targetPl1Units & 0x7FFFU) &&
                       currentPl2Units == (targetPl2Units & 0x7FFFU) &&
                       currentPl1Enabled &&
                       currentPl2Enabled == pl2Enable;
            }
            catch
            {
                return false;
            }
        }

    }
}
