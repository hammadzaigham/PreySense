namespace PreySense.Mode;

/// <summary>Preserves the pre-game mode while respecting later manual or power-source changes.</summary>
public sealed class GameModeSession
{
    private byte? _before, _applied;

    public void Begin(byte currentMode, byte gameMode)
    {
        if (!ProfileManager.IsSupportedMode(gameMode)) throw new ArgumentOutOfRangeException(nameof(gameMode));
        if (_before == null && ProfileManager.IsSupportedMode(currentMode)) _before = currentMode;
        _applied = gameMode;
    }

    public byte? End(byte currentMode)
    {
        byte? restore = RestoreMode(currentMode);
        _before = _applied = null;
        return restore;
    }
    public byte? RestoreMode(byte currentMode) => _applied == currentMode ? _before : null;
}
