using System.Runtime.InteropServices;

namespace PreySense.Mode;

public readonly record struct CpuBoostPolicySnapshot(Guid Scheme, uint AcMode, uint DcMode);

/// <summary>Captures both power-source policies so a temporary game override can be undone exactly.</summary>
public static class WindowsCpuBoostPolicy
{
    private static readonly Guid Processor = new("54533251-82be-4824-96c1-47b60b740d00");
    private static readonly Guid Boost = new("be337238-0d82-4146-a960-4f3749d470c7");

    private static Guid? ActiveScheme()
    {
        if (PowerGetActiveScheme(0, out nint pointer) != 0) return null;
        try { return Marshal.PtrToStructure<Guid>(pointer); }
        finally { LocalFree(pointer); }
    }

    public static CpuBoostPolicySnapshot? Read()
    {
        try
        {
            if (ActiveScheme() is not Guid scheme ||
                PowerReadACValueIndex(0, in scheme, in Processor, in Boost, out uint ac) != 0 ||
                PowerReadDCValueIndex(0, in scheme, in Processor, in Boost, out uint dc) != 0 || ac > 6 || dc > 6) return null;
            return new(scheme, ac, dc);
        }
        catch { return null; }
    }

    public static bool Restore(CpuBoostPolicySnapshot snapshot)
    {
        try
        {
            Guid scheme = snapshot.Scheme;
            if (PowerWriteACValueIndex(0, in scheme, in Processor, in Boost, snapshot.AcMode) != 0 ||
                PowerWriteDCValueIndex(0, in scheme, in Processor, in Boost, snapshot.DcMode) != 0) return false;
            // Never replace a power plan the user selected while the game was running.
            return ActiveScheme() != scheme || PowerSetActiveScheme(0, in scheme) == 0;
        }
        catch { return false; }
    }

    /// <summary>Undo only policy values still equal to this game's override.</summary>
    public static CpuBoostPolicySnapshot MergeTemporaryRestore(CpuBoostPolicySnapshot before,
        CpuBoostPolicySnapshot current, int gameBoost) => before with
    {
        AcMode = current.AcMode == gameBoost ? before.AcMode : current.AcMode,
        DcMode = current.DcMode == gameBoost ? before.DcMode : current.DcMode
    };

    public static bool RestoreTemporary(CpuBoostPolicySnapshot before, int gameBoost)
    {
        try
        {
            Guid scheme = before.Scheme;
            if (PowerReadACValueIndex(0, in scheme, in Processor, in Boost, out uint ac) != 0 ||
                PowerReadDCValueIndex(0, in scheme, in Processor, in Boost, out uint dc) != 0) return false;
            return Restore(MergeTemporaryRestore(before, new(scheme, ac, dc), gameBoost));
        }
        catch { return false; }
    }

    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(nint root, out nint scheme);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint pointer);
    [DllImport("powrprof.dll")] private static extern uint PowerReadACValueIndex(nint root, in Guid scheme, in Guid subgroup, in Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerReadDCValueIndex(nint root, in Guid scheme, in Guid subgroup, in Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteACValueIndex(nint root, in Guid scheme, in Guid subgroup, in Guid setting, uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteDCValueIndex(nint root, in Guid scheme, in Guid subgroup, in Guid setting, uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(nint root, in Guid scheme);
}
