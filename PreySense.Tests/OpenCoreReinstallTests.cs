using PreySense.Core.Acpi;
using PreySense.Frontend.ViewModels;

namespace PreySense.Tests;

[TestClass]
public class OpenCoreReinstallTests
{
    [TestMethod]
    public void OldLoadedTableAndCurrentFilesRequireRestart()
    {
        Assert.AreEqual(OpenCoreSecurityValidator.OpenCoreIntegrationState.RestartRequired,
            OpenCoreSecurityValidator.DecideIntegrationState(false, false, true, true));
    }

    [TestMethod]
    public void OldInstalledFilesRequireUpdateEvenIfCurrentTableIsLoaded()
    {
        Assert.AreEqual(OpenCoreSecurityValidator.OpenCoreIntegrationState.UpdateRequired,
            OpenCoreSecurityValidator.DecideIntegrationState(true, true, false, true, installedPackageKnownOutdated: true));
    }

    [TestMethod]
    public void UnknownInstalledFilesDoNotOverrideVerifiedCurrentBoot()
    {
        Assert.AreEqual(OpenCoreSecurityValidator.OpenCoreIntegrationState.Active,
            OpenCoreSecurityValidator.DecideIntegrationState(true, true, false, true));
        Assert.AreEqual(OpenCoreSecurityValidator.OpenCoreIntegrationState.UpdateRequired,
            OpenCoreSecurityValidator.DecideIntegrationState(false, false, false, true));
    }

    [TestMethod]
    public void ReinstallCommandAndDedicatedScriptArePackaged()
    {
        Assert.IsNotNull(typeof(MainViewModel).GetProperty("ReinstallOpenCoreCommand"));
        using var script = typeof(MainViewModel).Assembly.GetManifestResourceStream("PreySense.OpenCore.Reinstall-To-ESP.ps1");
        Assert.IsNotNull(script);
        using var reader = new System.IO.StreamReader(script);
        string text = reader.ReadToEnd();
        Assert.Contains("Copy-Item -LiteralPath $entry.Source", text);
        Assert.DoesNotContain("Format-Volume", text);
        Assert.DoesNotContain("Remove-Partition ", text);
    }
}
