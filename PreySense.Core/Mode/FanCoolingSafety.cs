namespace PreySense.Mode;

/// <summary>Immediate emergency cooling with a separate 5°C release margin.</summary>
public sealed class FanCoolingSafety
{
    private bool _active;

    public bool Evaluate(int cpu, int gpu)
    {
        if (FanCurveProfile.RequiresMaxCooling(cpu, gpu)) _active = true;
        else if (FanCurveProfile.ValidTemperatures(cpu, gpu) && cpu <= 90 && gpu <= 82) _active = false;
        return _active;
    }
}
