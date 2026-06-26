using System.IO;
using System.Runtime.Versioning;
using PreySense.Mode;

namespace PreySense.Helpers;

[SupportedOSPlatform("windows")]
public sealed record PawnIoCapability
{
    public bool Installed { get; init; }
    public bool Accessible { get; init; }
    public bool AdminRequired { get; init; }
    public bool ModuleLoaded { get; init; }
    public bool CanReadMsr { get; init; }
    public bool CanWriteMsr { get; init; }
    public bool LockLikelyPresent { get; init; }
    public string StatusText { get; init; } = string.Empty;
    public string DetailText { get; init; } = string.Empty;

    public static PawnIoCapability Probe()
    {
        var report = new PawnIoCapability();

        try
        {
            if (!PawnIO.IntelMsr.IsPawnIoAvailable(out string reason))
            {
                return report with
                {
                    Installed = false,
                    Accessible = false,
                    AdminRequired = reason.Contains("Administrator", StringComparison.OrdinalIgnoreCase),
                    StatusText = "PawnIO missing",
                    DetailText = reason
                };
            }

            using var msr = new PawnIO.IntelMsr();
            string resourceName = typeof(PowerLimitController).Assembly.GetName().Name + ".IntelMSR.bin";
            using var moduleStream = typeof(PowerLimitController).Assembly.GetManifestResourceStream(resourceName);
            if (moduleStream == null)
            {
                return report with
                {
                    Installed = true,
                    Accessible = true,
                    StatusText = "PawnIO installed",
                    DetailText = $"Embedded resource '{resourceName}' not found."
                };
            }

            using var ms = new MemoryStream();
            moduleStream.CopyTo(ms);

            if (!msr.TryInitialize(ms.ToArray(), out string initError))
            {
                return report with
                {
                    Installed = true,
                    Accessible = true,
                    AdminRequired = initError.Contains("Administrator", StringComparison.OrdinalIgnoreCase),
                    StatusText = "PawnIO unavailable",
                    DetailText = initError
                };
            }

            bool readOk = false;
            ulong unitRaw = 0;
            ulong currentVal = 0;
            if (msr.ReadMsr(0x606, out unitRaw) && msr.ReadMsr(0x610, out currentVal))
                readOk = true;
            bool writeOk = false;
            bool lockLikely = false;

            if (readOk)
            {
                writeOk = msr.WriteMsr(0x610, currentVal) && msr.ReadMsr(0x610, out ulong verifyVal) && verifyVal == currentVal;
                lockLikely = !writeOk;
            }

            return report with
            {
                Installed = true,
                Accessible = true,
                ModuleLoaded = true,
                CanReadMsr = readOk,
                CanWriteMsr = writeOk,
                LockLikelyPresent = lockLikely,
                StatusText = writeOk ? "PawnIO ready" : readOk ? "MSR read only" : "PawnIO partial",
                DetailText = writeOk
                    ? "CPU power tuning is available."
                    : readOk
                        ? "MSR reads work, but writes appear blocked or locked."
                        : "CPU MSR access is not available."
            };
        }
        catch (Exception ex)
        {
            return report with
            {
                StatusText = "PawnIO check failed",
                DetailText = ex.Message
            };
        }
    }
}
