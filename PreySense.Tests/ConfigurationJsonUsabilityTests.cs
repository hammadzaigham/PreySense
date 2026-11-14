using System.Text.Json;
using PreySense.Core.Configuration;

namespace PreySense.Tests;

[TestClass]
public class ConfigurationJsonUsabilityTests
{
    private string _directory = null!;
    private string _path = null!;

    [TestInitialize]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "PreySenseJson-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "preysense.json");
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_directory, recursive: true);

    [TestMethod]
    public void CanonicalNames_PreserveNullAndZeroWithoutInventingValues()
    {
        File.WriteAllText(_path, """
            {"SchemaVersion":2,"Tuning":{"Balanced":{
              "CpuTurboTimeLimitSeconds":null,"GpuRuntimeLimitWatts":140,
              "GpuNominalPowerWatts":null}},
             "Overlay":{"BackgroundOpacityPercent":0,"TextOpacityPercent":0}}
            """);
        var service = new ConfigurationService(_path);
        var config = service.GetCurrent();
        Assert.IsNull(config.Tuning.Balanced.TauSeconds);
        Assert.AreEqual(140, config.Tuning.Balanced.GpuRuntimeLimitWatts);
        Assert.IsNull(config.Tuning.Balanced.GpuWatts);
        Assert.AreEqual(0, config.Overlay.BackgroundAlphaPercent);
        Assert.AreEqual(0, config.Overlay.TextAlphaPercent);
        service.Save(config);
        using var saved = JsonDocument.Parse(File.ReadAllText(_path));
        var profile = saved.RootElement.GetProperty("Tuning").GetProperty("Balanced");
        Assert.AreEqual(JsonValueKind.Null, profile.GetProperty("CpuTurboTimeLimitSeconds").ValueKind);
        Assert.AreEqual(140, profile.GetProperty("GpuRuntimeLimitWatts").GetInt32());
        Assert.AreEqual(JsonValueKind.Null, profile.GetProperty("GpuNominalPowerWatts").ValueKind);
        Assert.IsFalse(profile.TryGetProperty("TauSeconds", out _));
    }

    [TestMethod]
    public void Save_UsesStableGroupedLayoutAndPreservesExtraTuningFields()
    {
        File.WriteAllText(_path, """
            {"SchemaVersion":2,"ExtraRoot":{"Keep":true},"Tuning":{"ExtraTuning":[1,2],
              "Balanced":{"ExtraProfile":"keep","PlatformPowerOffsetWatts":15},
              "FactoryDefaults":{
                "Turbo":{"CpuPl1Watts":85,"CpuPl2Watts":140,"CpuTurboTimeLimitSeconds":56},
                "Performance":{"CpuPl1Watts":75,"CpuPl2Watts":140,"CpuTurboTimeLimitSeconds":56},
                "Balanced":{"CpuPl1Watts":65,"CpuPl2Watts":140,"CpuTurboTimeLimitSeconds":56},
                "Eco":{"CpuPl1Watts":35,"CpuPl2Watts":65,"CpuTurboTimeLimitSeconds":56},
                "Quiet":{"CpuPl1Watts":45,"CpuPl2Watts":65,"CpuTurboTimeLimitSeconds":56}}}}
            """);
        var service = new ConfigurationService(_path);
        service.Update(config => config.General.LightTheme = true);
        string text = File.ReadAllText(_path);
        Assert.Contains("\n  \"SchemaVersion\": 2", text);
        using var saved = JsonDocument.Parse(text);
        Assert.IsTrue(saved.RootElement.GetProperty("ExtraRoot").GetProperty("Keep").GetBoolean());
        var tuning = saved.RootElement.GetProperty("Tuning");
        Assert.AreEqual(2, tuning.GetProperty("ExtraTuning").GetArrayLength());
        Assert.AreEqual("keep", tuning.GetProperty("Balanced").GetProperty("ExtraProfile").GetString());
        Assert.AreEqual(15, tuning.GetProperty("Balanced").GetProperty("PlatformPowerOffsetWatts").GetInt32());
        CollectionAssert.AreEqual(new[] { "Quiet", "Eco", "Balanced", "Performance", "Turbo" },
            tuning.GetProperty("FactoryDefaults").EnumerateObject().Select(p => p.Name).ToArray());
        var fields = tuning.GetProperty("Balanced").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains("CpuPl1Watts", fields);
        service.Save(service.GetCurrent());
        Assert.AreEqual(text, File.ReadAllText(_path), "Repeated saves must not reorder or rewrite values.");
    }

    [TestMethod]
    public void NullFactoryMap_SavesAsAnEmptyMap()
    {
        var service = new ConfigurationService(_path);
        var config = service.GetCurrent();
        config.Tuning.FactoryDefaults = null!;
        service.Save(config);
        Assert.IsEmpty(service.Reload().Tuning.FactoryDefaults);
    }

    [TestMethod]
    [DataRow("{\"SchemaVersion\":1,\"Tuning\":{\"Important\":true}}")]
    [DataRow("{\"SchemaVersion\":3,\"FutureSection\":{\"Important\":true}}")]
    [DataRow("{\"Tuning\":{\"TauSeconds\":56}}")]
    public void UnsupportedOrMissingVersion_IsRejectedWithoutChangingTheFile(string json)
    {
        File.WriteAllText(_path, json);
        Assert.Throws<InvalidOperationException>(() => new ConfigurationService(_path));
        Assert.AreEqual(json, File.ReadAllText(_path));
    }

    [TestMethod]
    public void Save_RejectsOldVersionWithoutOverwritingExistingFile()
    {
        var service = new ConfigurationService(_path);
        service.Save(service.GetCurrent());
        string original = File.ReadAllText(_path);
        var old = service.GetCurrent();
        old.SchemaVersion = 1;
        Assert.Throws<ArgumentOutOfRangeException>(() => service.Save(old));
        Assert.AreEqual(original, File.ReadAllText(_path));
    }

    [TestMethod]
    public void Schema_DescribesTheCanonicalNamesAndNominalPower()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "preysense.schema.json")));
        var mode = schema.RootElement.GetProperty("definitions").GetProperty("ModeTuning").GetProperty("properties");
        Assert.IsTrue(mode.TryGetProperty("CpuTurboTimeLimitSeconds", out _));
        Assert.IsTrue(mode.TryGetProperty("GpuRuntimeLimitWatts", out _));
        Assert.Contains("not an enforced", mode.GetProperty("GpuNominalPowerWatts").GetProperty("description").GetString()!);
        Assert.AreEqual(2, schema.RootElement.GetProperty("properties").GetProperty("SchemaVersion").GetProperty("const").GetInt32());
    }
}
