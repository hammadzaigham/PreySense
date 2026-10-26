using System.IO;
using PreySense.Frontend.Services;
using static PawnIO.PawnIOWrapper;

namespace PreySense.Tests;

[TestClass]
public class PawnIoInstallationTests
{
    [TestMethod]
    public void OnlyMissingDriverOffersInstallation()
    {
        Assert.IsTrue(PawnIoInstallService.ShouldOfferInstallation(ConnectResult.NotInstalled, false));
        Assert.IsFalse(PawnIoInstallService.ShouldOfferInstallation(ConnectResult.NotInstalled, true));
        Assert.IsFalse(PawnIoInstallService.ShouldOfferInstallation(ConnectResult.AccessDenied, false));
        Assert.IsFalse(PawnIoInstallService.ShouldOfferInstallation(ConnectResult.OtherError, false));
        Assert.IsFalse(PawnIoInstallService.ShouldOfferInstallation(ConnectResult.OK, true));
    }

    [TestMethod]
    public void InstallerRestartCodesAreSuccessfulButRequireRestart()
    {
        Assert.IsTrue(PawnIoInstallService.IsSuccessfulExitCode(0));
        Assert.IsFalse(PawnIoInstallService.RequiresRestart(0));
        foreach (int code in new[] { 3010, 1641 })
        {
            Assert.IsTrue(PawnIoInstallService.IsSuccessfulExitCode(code));
            Assert.IsTrue(PawnIoInstallService.RequiresRestart(code));
        }
        foreach (int code in new[] { -1, 1, 5, 1603 })
            Assert.IsFalse(PawnIoInstallService.IsSuccessfulExitCode(code));
    }

    [TestMethod]
    public void UsesOfficialSetupAndRunsBeforeStartupPowerOperations()
    {
        Assert.AreEqual("https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe", PawnIoInstallService.InstallerUrl);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "PreySense.slnx"))) root = root.Parent;
        Assert.IsNotNull(root);
        string session = File.ReadAllText(Path.Combine(root.FullName, "PreySense.Frontend", "ViewModels", "MainViewModel.Session.cs"));
        int start = session.IndexOf("private async Task InitializeSessionAsync()", StringComparison.Ordinal);
        int window = session.IndexOf("await _startupWindowReady.Task", start, StringComparison.Ordinal);
        int installer = session.IndexOf("PawnIoInstallService.EnsureInstalledAsync", start, StringComparison.Ordinal);
        int restore = session.IndexOf("RestoreSessionSettingsAsync(\"startup\")", start, StringComparison.Ordinal);
        Assert.IsTrue(window < installer && installer < restore);
        string source = File.ReadAllText(Path.Combine(root.FullName, "PreySense.Frontend", "Services", "PawnIoInstallService.cs"));
        Assert.Contains("-install -silent", source);
        Assert.Contains("Get-AuthenticodeSignature", source);
        Assert.Contains("VerifySignatureAsync(installer)", source);
        Assert.Contains("Inspect(out string detail)", source);
        Assert.DoesNotContain("WriteMsr", source);
    }
}
