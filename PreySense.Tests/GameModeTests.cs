using PreySense.Core.Configuration;
using PreySense.Mode;

namespace PreySense.Tests;

[TestClass]
public class GameModeTests
{
    [TestMethod]
    public void GameMatchingUsesTheSelectedFullPathAndIgnoresDisabledAndInvalidProfiles()
    {
        var selected = new GameProfile { ExecutablePath = @"D:\Games\MyGame\game.exe", PowerMode = 5 };
        Assert.AreSame(selected, GameProfileStore.FindMatchingProfile(@"d:\games\mygame\GAME.EXE", [selected]));
        Assert.IsNull(GameProfileStore.FindMatchingProfile(@"D:\Other\game.exe", [selected]), "Matching only a filename could apply tuning to the wrong app.");
        selected.Enabled = false;
        Assert.IsNull(GameProfileStore.FindMatchingProfile(selected.ExecutablePath, [selected]));
        selected.Enabled = true;
        selected.PowerMode = 3;
        Assert.IsNull(GameProfileStore.FindMatchingProfile(selected.ExecutablePath, [selected]));
        Assert.IsNull(GameProfileStore.FindMatchingProfile(null, [selected]));
    }

    [TestMethod]
    public void GameExitOrDisablingRestoresOriginalModeAndClearsTheSession()
    {
        var session = new GameModeSession();
        session.Begin(1, 5);
        session.Begin(5, 4);
        Assert.AreEqual((byte)1, session.End(4));
        Assert.IsNull(session.End(4));
    }

    [TestMethod]
    public void GameExitDoesNotUndoAManualOrPowerSourceModeChange()
    {
        var session = new GameModeSession();
        session.Begin(1, 5);
        Assert.IsNull(session.End(6));
        session.Begin(6, 4);
        Assert.AreEqual((byte)6, session.End(4));
    }

    [TestMethod]
    public void GameModeDefaultsOffAndProfilesPersistInTheCurrentConfiguration()
    {
        string directory = Path.Combine(Path.GetTempPath(), "preysense-games-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "preysense.json");
            var store = new ConfigurationService(path);
            Assert.IsFalse(store.GetCurrent().Games.Enabled);
            store.Update(config =>
            {
                config.Games.Enabled = true;
                config.Games.Profiles.Add(new() { ExecutablePath = @"D:\Games\game.exe", DisplayName = "Game", PowerMode = 5 });
            });
            var saved = new ConfigurationService(path).GetCurrent();
            Assert.IsTrue(saved.Games.Enabled);
            Assert.HasCount(1, saved.Games.Profiles);
            Assert.AreEqual((byte)5, saved.Games.Profiles[0].PowerMode);
            Assert.AreEqual(@"D:\Games\game.exe", saved.Games.Profiles[0].ExecutablePath);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
