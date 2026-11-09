using PreySense.Helpers;
using PreySense.Rgb;

namespace PreySense.Tests;

[TestClass]
public class LightingFailureTests
{
    [TestMethod]
    public async Task AnimationFrameFailureStopsWithoutPeriodicRetries()
    {
        using var wmi = new WmiController();
        using var engine = new CustomLightingEffectsEngine(wmi);
        int frames = 0;
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        // Fails before the physical dispatch; this regression never writes lighting hardware.
        engine.FrameUpdated += _ =>
        {
            Interlocked.Increment(ref frames);
            started.Set();
            release.Wait(TimeSpan.FromSeconds(3));
            throw new InvalidOperationException("Simulated frame consumer failure.");
        };
        engine.StartEffect(CustomEffectMode.RainbowCycle, 50, 50);
        Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(3)));
        var task = (Task)typeof(CustomLightingEffectsEngine)
            .GetField("_animationTask", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(engine)!;
        release.Set();
        await task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.AreEqual(1, frames);
        Assert.IsFalse(engine.IsRunning);
    }
}
