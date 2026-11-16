namespace PreySense.Mode;

public enum FanControllerState
{
    Inactive,
    FirmwareAuto,
    HardwareMax,
    Settling,
    Following,
    WaitingForCpuTemperature,
    WriteFailed
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
    int? GpuPendingStage)
{
    public string Summary => State switch
    {
        FanControllerState.FirmwareAuto => "Auto: firmware controls the fans.",
        FanControllerState.HardwareMax => "Max requested; RPM is measured separately.",
        FanControllerState.Settling => "Custom: waiting for the performance mode to settle.",
        FanControllerState.WaitingForCpuTemperature => "Custom: waiting for a valid CPU temperature; last request retained.",
        FanControllerState.WriteFailed => "Fan command failed. Apply fan settings again to resume.",
        FanControllerState.Following =>
            $"Custom: software stage CPU {CpuStage} / GPU {GpuStage}." +
            (CpuPendingStage.HasValue || GpuPendingStage.HasValue ? " Transition delay pending." : ""),
        _ => "Fan controller is inactive."
    };
}
