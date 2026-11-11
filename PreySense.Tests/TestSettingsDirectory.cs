namespace PreySense.Tests;

/// <summary>Keep singleton app settings out of build output and user configuration files.</summary>
[TestClass]
public sealed class TestSettingsDirectory
{
    private static string _originalDirectory = null!;
    private static string _settingsDirectory = null!;

    [AssemblyInitialize]
    public static void Initialize(TestContext context)
    {
        _originalDirectory = Directory.GetCurrentDirectory();
        _settingsDirectory = Path.Combine(Path.GetTempPath(), "PreySenseTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_settingsDirectory);
        PreySense.Core.Configuration.ConfigurationService.SingletonConfigPath =
            Path.Combine(_settingsDirectory, PreySense.Core.Configuration.ConfigurationService.ConfigFileName);
        Directory.SetCurrentDirectory(_settingsDirectory);
    }

    [AssemblyCleanup]
    public static void Cleanup()
    {
        Directory.SetCurrentDirectory(_originalDirectory);
        // Only remove this run's explicitly created directory, never an app settings folder.
        string tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string settingsPath = Path.GetFullPath(_settingsDirectory);
        if (settingsPath.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(settingsPath).StartsWith("PreySenseTests-", StringComparison.Ordinal) &&
            Directory.Exists(settingsPath))
            Directory.Delete(settingsPath, recursive: true);
    }
}
