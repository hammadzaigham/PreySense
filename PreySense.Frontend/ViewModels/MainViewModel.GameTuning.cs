using PreySense.Core.Configuration;
using PreySense.Core.Driver;
using PreySense.Core.Hardware;
using PreySense.Mode;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    internal readonly record struct GameTuningPlan(DriverTuningRequest Apply, DriverTuningRequest Restore, int? CpuBoost);
    internal readonly record struct GameGpuProfile(byte Mode, byte Delta, byte Boost, byte Platform);
    private GameTuningPlan? _activeGameTuning;
    private GameGpuProfile? _gameGpuBefore;
    private CpuBoostPolicySnapshot? _gameBoostBefore;
    private byte? _gameRollbackMode;
    internal Func<CpuBoostPolicySnapshot?>? GameBoostPolicyReader { get; set; }
    internal Func<CpuBoostPolicySnapshot, bool>? GameBoostPolicyRestorer { get; set; }
    internal Func<byte, GameGpuProfile?>? GameGpuProfileReader { get; set; }
    internal Func<GameGpuProfile, DriverTuningResult>? GameGpuProfileRestorer { get; set; }
    internal Func<byte, Task<bool>>? GameModeSwitcher { get; set; }

    internal bool TryCreateGameTuningPlan(GameProfile game, out GameTuningPlan plan, out string status)
    {
        plan = default;
        status = "";
        if (!ProfileManager.IsSupportedMode(game.PowerMode)) { status = "Invalid power mode."; return false; }
        var tuning = (TuningConfigurationReader?.Invoke() ?? TuningStore.GetCurrent()).Tuning;
        bool applyCpu = tuning.EnableCpuPowerLimitControl && game.ApplyCpuLimits;
        bool applyGpu = tuning.EnableGpuPowerLimitControl && game.ApplyGpuLimits;
        bool applyBoost = tuning.EnableCpuBoostTuningControl;
        tuning.FactoryDefaults.TryGetValue(ProfileManager.ModeToProfileName(game.PowerMode), out var factory);
        if (!IsHardwareTuningEnabled || ((applyCpu || applyGpu) && (!tuning.HasFactoryDefaults || factory is null)))
        { status = "Tuning is unavailable until factory defaults are ready."; return false; }
        factory ??= new();
        var saved = ProfileForMode(tuning, game.PowerMode);
        if (applyCpu && (
            game.CpuPl1Watts is not (>= 5 and <= 300) || game.CpuPl2Watts is not (>= 5 and <= 300) ||
            game.CpuPl2Watts < game.CpuPl1Watts || game.TauSeconds is not (>= 0 and <= 449)))
        { status = "CPU limits are out of range."; return false; }
        if (applyBoost && game.CpuBoostMode is < 0 or > 6)
        { status = "Choose a CPU boost option."; return false; }
        GpuPowerOffsets? gpuOffsets = null;
        if (applyGpu)
        {
            if (game.HasGpuOffsets)
            {
                gpuOffsets = new(game.GpuDeltaWatts!.Value, game.GpuDynamicBoostWatts!.Value, game.PlatformPowerOffsetWatts!.Value);
                if (!gpuOffsets.IsValid || gpuOffsets.DeltaWatts % 5 != 0 || gpuOffsets.PlatformOffsetWatts % 5 != 0
                    || !DriverFactoryCalibration.IsLegalGpuDynamicBoost(gpuOffsets.DynamicBoostWatts))
                { status = "GPU offsets are outside the supported Acer profile range or step."; return false; }
                if (!factory.HasGpuLimitValues) { status = "GPU profile table was not found on this laptop."; return false; }
            }
            else if (game.GpuPowerWatts is not int watts || !TryResolveGpuTarget(game.PowerMode, watts, out _, out status))
            { if (status.Length == 0) status = "Enter supported GPU offsets."; return false; }
        }
        bool restoreCpu = applyCpu || (applyGpu && tuning.EnableCpuPowerLimitControl && saved.ApplyCpuLimits);
        int pl1 = (saved.ApplyCpuLimits ? saved.CpuPl1Watts ?? factory.CpuPl1Watts : factory.CpuPl1Watts) ?? 0;
        int pl2 = (saved.ApplyCpuLimits ? saved.CpuPl2Watts ?? factory.CpuPl2Watts : factory.CpuPl2Watts) ?? 0;
        int tau = (saved.ApplyCpuLimits ? saved.TauSeconds ?? factory.TauSeconds : factory.TauSeconds) ?? 0;
        if (restoreCpu && (pl1 is < 5 or > 300 || pl2 < pl1 || pl2 > 300 || tau is < 0 or > 449))
        { status = "The saved CPU profile cannot be restored."; return false; }
        plan = new(new(game.PowerMode, game.CpuPl1Watts ?? 0, game.CpuPl2Watts ?? 0, game.TauSeconds ?? 0,
                applyGpu && gpuOffsets is null ? game.GpuPowerWatts : null, ApplyCpuLimits: applyCpu, GpuOffsets: gpuOffsets),
            new(game.PowerMode, pl1, pl2, tau, ApplyCpuLimits: restoreCpu),
            applyBoost ? game.CpuBoostMode ?? CpuBoostModeForMode(game.PowerMode) : null);
        return true;
    }

    private GameGpuProfile? ReadGameGpuProfile(byte mode)
    {
        var memory = CpuDriverRuntime.Backend;
        if (memory == null || !AcerErm4Table.EnsureResolved(memory, out _)) return null;
        ulong address = AcerProfileApplyTransaction.ProfileAddress(mode);
        if (!memory.ReadPhysicalByte(address, out byte boost) || !memory.ReadPhysicalByte(address + 1, out byte delta)
            || !memory.ReadPhysicalByte(address + 2, out byte platform)
            || delta > DriverFactoryCalibration.MaxGpuDeltaWatts || delta % 5 != 0
            || !DriverFactoryCalibration.IsLegalGpuDynamicBoost(boost) || platform % 5 != 0) return null;
        return new(mode, delta, boost, platform);
    }

    private DriverTuningResult RestoreGameGpuProfile(GameGpuProfile previous)
    {
        var memory = CpuDriverRuntime.Backend;
        if (memory == null) return new(false, "GPU profile table was not found on this laptop.");
        var result = new AcerProfileApplyTransaction(memory, new TuningModeController(this))
            .Apply(previous.Mode, previous.Delta, previous.Boost, previous.Platform);
        return new(result.Success, result.Status);
    }

    private Task<bool> SwitchGameModeAsync(byte mode) => GameModeSwitcher?.Invoke(mode) ?? ApplyModeSwitchCoreAsync(mode);

    internal DriverTuningResult ExecuteGameTuningPlan(GameTuningPlan plan)
    {
        var applied = ExecuteRequestedTuning(plan.Apply, false, null);
        if (!applied.Success) return applied;
        if (plan.CpuBoost is int boost && !(CpuBoostWriter ?? PowerLimitController.SetCpuBoost)(boost))
            return new(false, "Could not apply CPU boost.");
        return new(true, "Applied and read back successfully.");
    }

    internal async Task<bool> ApplyGameProfileWithOverridesAsync(GameProfile game)
    {
        game = game.Copy(); // Do not let an open editor change an in-flight request.
        if (!TryCreateGameTuningPlan(game, out var plan, out string status))
        { GameModeStatus = status; return false; }
        if (!await TryEnterModeSwitchGateAsync()) return false;
        byte originalMode = PeekPendingOrActiveMode();
        bool changedMode = false;
        try
        {
            if (_disposed || _gameModeStopping) return false;
            if (_activeGameTuning.HasValue)
            {
                // Retry a pending cleanup, including same-game resume callbacks, before
                // taking another snapshot. Never replace the only pre-game state.
                byte restore = GameRestoreMode(originalMode);
                if (!await RestoreActiveGameTuningAsync(restore)) return false;
                _gameModeSession.End(originalMode);
                originalMode = restore;
            }
            var gpuBefore = plan.Apply.ApplyGpuLimits
                ? await Task.Run(() => (GameGpuProfileReader ?? ReadGameGpuProfile)(game.PowerMode)) : null;
            if (plan.Apply.ApplyGpuLimits && gpuBefore == null)
            { GameModeStatus = "Could not read the current GPU profile."; return false; }
            var boostBefore = plan.CpuBoost.HasValue ? await Task.Run(GameBoostPolicyReader ?? WindowsCpuBoostPolicy.Read) : null;
            if (plan.CpuBoost.HasValue && boostBefore == null)
            { GameModeStatus = "Could not read the current CPU boost policy."; return false; }
            _activeGameTuning = plan;
            _gameGpuBefore = gpuBefore;
            _gameBoostBefore = boostBefore;
            _gameRollbackMode = originalMode;
            changedMode = true;
            if (!await SwitchGameModeAsync(game.PowerMode))
                throw new InvalidOperationException(string.IsNullOrEmpty(DiagnosticMessage) ? "Could not switch Acer mode." : DiagnosticMessage);
            var result = await Task.Run(() => ExecuteGameTuningPlan(plan));
            if (!result.Success) throw new InvalidOperationException(result.Status);
            _gameModeSession.Begin(originalMode, game.PowerMode);
            return true;
        }
        catch (Exception ex)
        {
            string failure = ex.Message;
            bool hadTuning = _activeGameTuning.HasValue;
            bool cleaned = await RestoreActiveGameTuningAsync(originalMode);
            if (changedMode && !hadTuning) cleaned &= await SwitchGameModeAsync(originalMode);
            GameModeStatus = cleaned ? failure : failure + " Previous game settings could not be fully restored.";
            return false;
        }
        finally { ExitModeSwitchGate(); }
    }

    internal DriverTuningResult RestoreGameTuningPlan(GameTuningPlan plan, GameGpuProfile? gpu, CpuBoostPolicySnapshot? boost)
    {
        var errors = new List<string>();
        if (gpu is { } previousGpu)
        {
            try
            {
                var result = (GameGpuProfileRestorer ?? RestoreGameGpuProfile)(previousGpu);
                if (!result.Success) errors.Add(result.Status);
            }
            catch (Exception ex) { errors.Add(ex.Message); }
        }
        try
        {
            var cpu = ExecuteRequestedTuning(plan.Restore, false, null);
            if (!cpu.Success) errors.Add(cpu.Status);
        }
        catch (Exception ex) { errors.Add(ex.Message); }
        try
        {
            if (plan.CpuBoost is int appliedBoost && boost is { } previous && !(GameBoostPolicyRestorer ?? (snapshot => WindowsCpuBoostPolicy.RestoreTemporary(snapshot, appliedBoost)))(previous))
                errors.Add("Could not restore the previous CPU boost policy.");
        }
        catch (Exception ex) { errors.Add(ex.Message); }
        return new(errors.Count == 0, string.Join(" ", errors));
    }

    private async Task<bool> RestoreActiveGameTuningAsync(byte finalMode)
    {
        if (_activeGameTuning is not { } plan) return true;
        var cleanup = await Task.Run(() => RestoreGameTuningPlan(plan, _gameGpuBefore, _gameBoostBefore));
        bool modeRestored;
        try { modeRestored = await SwitchGameModeAsync(finalMode); }
        catch (Exception ex) { GameModeStatus = ex.Message; return false; }
        if (cleanup.Success && modeRestored)
        {
            _activeGameTuning = null; _gameGpuBefore = null; _gameBoostBefore = null;
            _gameRollbackMode = null;
            return true;
        }
        GameModeStatus = cleanup.Success ? "Could not restore the previous power mode." : cleanup.Status;
        return false;
    }

    private byte GameRestoreMode(byte current) => _gameModeSession.RestoreMode(current)
        ?? (_activeGameTuning is { } active && current == active.Apply.Mode ? _gameRollbackMode ?? current : current);
}
