using System.Diagnostics;
using System.Reflection;
using PreySense.Mode;

namespace PreySense.Tests;

[TestClass]
public class PowerCommandFailureTests
{
    private static (bool Success, string Error) Run(string executable, string arguments, int timeout = 5000)
    {
        object?[] args = { executable, arguments, timeout, null };
        bool result = (bool)typeof(PowerLimitController).GetMethod("RunCommand", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args)!;
        return (result, (string)args[3]!);
    }

    [TestMethod]
    public void FailedExitCodeIsNotReportedAsSuccess()
    {
        var result = Run(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d /c exit 7");
        Assert.IsFalse(result.Success);
        Assert.Contains("code 7", result.Error);
    }

    [TestMethod]
    public void SuccessfulCommandHasNoError()
    {
        var result = Run(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d /c exit 0");
        Assert.IsTrue(result.Success);
        Assert.AreEqual(string.Empty, result.Error);
    }

    [TestMethod]
    public void MissingExecutableIsHandled()
    {
        var result = Run(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".exe"), "");
        Assert.IsFalse(result.Success);
        Assert.IsFalse(string.IsNullOrEmpty(result.Error));
    }

    [TestMethod]
    public void HungCommandTimesOut()
    {
        var timer = Stopwatch.StartNew();
        var result = Run(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
            "-NoProfile -NonInteractive -Command Start-Sleep -Seconds 5", 100);
        Assert.IsFalse(result.Success);
        Assert.Contains("timed out", result.Error);
        Assert.IsTrue(timer.Elapsed < TimeSpan.FromSeconds(4));
    }
}
