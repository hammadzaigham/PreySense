using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: AdminScriptRunner <script.ps1> <SHA256> <result.json> [script arguments...]");
    return 2;
}

string scriptPath = Path.GetFullPath(args[0]);
string resultPath = Path.GetFullPath(args[2]);
if (!Path.GetExtension(scriptPath).Equals(".ps1", StringComparison.OrdinalIgnoreCase)
    || !Path.GetExtension(resultPath).Equals(".json", StringComparison.OrdinalIgnoreCase)
    || File.Exists(resultPath))
{
    Console.Error.WriteLine("An existing .ps1 script and a new .json result path are required.");
    return 2;
}

try
{
    using var identity = WindowsIdentity.GetCurrent();
    if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        throw new InvalidOperationException("Launch with Windows Run as administrator to request UAC.");

    // Keep the script open without write/delete sharing through the entire execution.
    using var script = new FileStream(scriptPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    string actualHash = Convert.ToHexString(SHA256.HashData(script));
    if (!actualHash.Equals(args[1], StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Script hash changed; refusing to run it.");

    var start = new ProcessStartInfo
    {
        FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
        WorkingDirectory = Path.GetDirectoryName(scriptPath)!,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    foreach (string argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", scriptPath })
        start.ArgumentList.Add(argument);
    foreach (string argument in args.Skip(3)) start.ArgumentList.Add(argument);

    using var process = Process.Start(start) ?? throw new InvalidOperationException("PowerShell did not start.");
    var output = process.StandardOutput.ReadToEndAsync();
    var errors = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    var result = new { Script = scriptPath, Sha256 = actualHash, ExitCode = process.ExitCode,
        StandardOutput = await output, StandardError = await errors };
    using var resultFile = new FileStream(resultPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
    await JsonSerializer.SerializeAsync(resultFile, result, new JsonSerializerOptions { WriteIndented = true });
    return process.ExitCode;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    // Leave the same reviewable result on preflight failures, without overwriting a file.
    if (!File.Exists(resultPath))
    {
        using var resultFile = new FileStream(resultPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        await JsonSerializer.SerializeAsync(resultFile, new { Script = scriptPath, ExitCode = 1, Error = ex.Message });
    }
    return 1;
}
