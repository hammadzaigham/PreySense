namespace PreySense.Mode;

/// <summary>Stable 5°C bands with the same response delay for rising and falling targets.</summary>
public sealed class FanCurveFollower
{
    private int? _band;
    private int? _applied;
    private int _pendingDirection;
    private long _pendingSince;

    public int Evaluate(int temperature, int[] curve, int delaySeconds, long nowMs, bool emergency = false)
    {
        if (emergency)
        {
            _applied = 100;
            _pendingDirection = 0;
            return 100;
        }
        int band = Math.Clamp(temperature / 5 * 5, FanCurveProfile.MinimumTemperature, FanCurveProfile.MaximumTemperature);
        if (!_band.HasValue || band > _band.Value || temperature <= _band.Value - 5) _band = band;
        int target = Math.Clamp(curve[FanCurveProfile.PointIndex(_band.Value)], 0, 100);
        if (!_applied.HasValue || target == _applied.Value)
        {
            _pendingDirection = 0;
            return (_applied = target).Value;
        }
        int direction = Math.Sign(target - _applied.Value);
        // Keep the clock while demand remains above/below the applied target.
        // A new hotter band must not postpone an already pending increase.
        if (_pendingDirection != direction)
        {
            _pendingDirection = direction;
            _pendingSince = nowMs;
        }
        int delay = Math.Clamp(delaySeconds, 0, 5);
        if (nowMs - _pendingSince >= delay * 1000L)
        {
            _applied = target;
            _pendingDirection = 0;
        }
        return _applied.Value;
    }
    public void Reset()
    {
        _band = _applied = null;
        _pendingDirection = 0;
    }
}
