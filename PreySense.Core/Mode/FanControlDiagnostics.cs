namespace PreySense.Mode;

public enum FanControllerState
{
    Inactive,
    FirmwareAuto,
    HardwareMax,
    Settling,
    Following,
    WaitingForCpuTemperature,
    WriteFailed,
    ManualDuty
}

/// <summary>
/// Software controller state only. Requests are WMI-reported successful commands,
/// not measured PWM, EC ownership, or proof the firmware accepted the transaction.
/// </summary>
public sealed record FanControlDiagnostics(
    FanControllerState State,
    byte? PowerMode,
    int? CpuRequestPercent,
    int? GpuRequestPercent,
    int? CpuStage,
    int? GpuStage,
    int? CpuPendingStage,
    int? GpuPendingStage);
