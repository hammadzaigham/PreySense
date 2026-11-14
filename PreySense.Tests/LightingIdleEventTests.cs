using System.Drawing;
using PreySense.Helpers;
using PreySense.Rgb;

namespace PreySense.Tests;

[TestClass]
public class LightingIdleEventTests
{
    [TestMethod]
    public void AccessoryNotificationsIgnoreOrdinaryFramesAndDeliverBothIdleTransitions()
    {
        using var wmi = new WmiController();
        using var engine = new CustomLightingEffectsEngine(wmi);
        var notifications = new List<(Color Color, bool Idle)>();
        engine.IdleStateChanged += (frame, idle) => notifications.Add((frame[0], idle));
        Color[] active = [Color.Red, Color.Green, Color.Blue, Color.White];
        Color[] off = [Color.Black, Color.Black, Color.Black, Color.Black];
        for (int i = 0; i < 100; i++) engine.PublishIdleStateFrame(active, false);
        CollectionAssert.AreEqual(new[] { (Color.Red, false) }, notifications.ToArray());
        engine.PublishIdleStateFrame(off, true);
        engine.PublishIdleStateFrame(off, true);
        engine.PublishIdleStateFrame(active, false);
        engine.PublishIdleStateFrame(active, false);
        CollectionAssert.AreEqual(new[] { (Color.Red, false), (Color.Black, true), (Color.Red, false) }, notifications.ToArray());
    }

    [TestMethod]
    public void ResubscribingReceivesAnInitialFrameWithoutWaitingForIdleChange()
    {
        using var wmi = new WmiController();
        using var engine = new CustomLightingEffectsEngine(wmi);
        int notifications = 0;
        void Changed(Color[] frame, bool idle) => notifications++;
        Color[] active = [Color.Red, Color.Green, Color.Blue, Color.White];
        engine.IdleStateChanged += Changed;
        engine.PublishIdleStateFrame(active, false);
        Assert.AreEqual(1, notifications);
        engine.IdleStateChanged -= Changed;
        engine.PublishIdleStateFrame(active, true);
        engine.PublishIdleStateFrame(active, false);
        Assert.AreEqual(1, notifications);
        engine.IdleStateChanged += Changed;
        engine.PublishIdleStateFrame(active, false);
        engine.PublishIdleStateFrame(active, false);
        Assert.AreEqual(2, notifications);
    }
}
