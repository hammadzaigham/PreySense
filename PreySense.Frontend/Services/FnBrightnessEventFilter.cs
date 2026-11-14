namespace PreySense.Frontend.Services;

/// <summary>One Fn press can arrive through both raw HID and the Windows keyboard hook.</summary>
internal sealed class FnBrightnessEventFilter
{
    private readonly object _gate = new();
    private string? _source;
    private int _direction;
    private long _at;
    internal bool Accept(string source, int direction, long now)
    {
        lock (_gate)
        {
            if (_source != null && _source != source && _direction == direction && now - _at is >= 0 and < 100)
                return false;
            _source = source; _direction = direction; _at = now;
            return direction != 0;
        }
    }
}
