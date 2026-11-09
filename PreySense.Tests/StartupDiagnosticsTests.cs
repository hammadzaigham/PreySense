using PreySense.Core.Configuration;
using PreySense.Frontend.Services;

namespace PreySense.Tests;

[TestClass]
public class StartupDiagnosticsTests
{
    [TestMethod]
    public void OldConfigurationFailure_IsReportableWithoutChangingTheFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), "PreySenseStartup-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "preysense.json");
            const string original = "{\"SchemaVersion\":1,\"Important\":true}";
            File.WriteAllText(path, original);
            var exception = Assert.Throws<InvalidOperationException>(() => new ConfigurationService(path));
            string report = StartupDiagnostics.CreateReport(exception);
            Assert.Contains(path, report);
            Assert.Contains("SchemaVersion", report);
            Assert.Contains(AppLogger.LogPath, report);
            Assert.Contains(exception.ToString(), ReadDebugLog());
            Assert.AreEqual(original, File.ReadAllText(path));
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void StartupErrors_AppendToTheExistingDebugLog()
    {
        string first = "First startup error " + Guid.NewGuid();
        string second = "Second startup error " + Guid.NewGuid();
        StartupDiagnostics.CreateReport(new Exception(first));
        StartupDiagnostics.CreateReport(new Exception(second));
        string log = ReadDebugLog();
        Assert.Contains(first, log);
        Assert.Contains(second, log);
        Assert.AreEqual("debug.log", Path.GetFileName(AppLogger.LogPath));
    }
    private static string ReadDebugLog()
    {
        using var stream = new FileStream(AppLogger.LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
