using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using PreySense.Frontend.Views;

namespace PreySense.Frontend.Services;

public static class PawnIoInstallService
{
    public const string InstallerUrl = "https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe";
    public const string WebsiteUrl = "https://pawnio.eu/";
    public static bool IsSuccessfulExitCode(int code) => code is 0 or 3010 or 1641;
    public static bool RequiresRestart(int code) => code is 3010 or 1641;
    public static bool ShouldOfferInstallation(PawnIO.PawnIOWrapper.ConnectResult result, bool servicePresent) =>
        !servicePresent && result == PawnIO.PawnIOWrapper.ConnectResult.NotInstalled;
    private enum Availability { Ready, Missing, Unavailable }

    private static Availability Inspect(out string detail)
    {
        detail = string.Empty;
        try
        {
            using var io = new PawnIO.PawnIOWrapper();
            var result = io.Connect();
            if (result == PawnIO.PawnIOWrapper.ConnectResult.OK) return Availability.Ready;
            if (result == PawnIO.PawnIOWrapper.ConnectResult.AccessDenied)
            { detail = "PawnIO is installed but access is denied. Run PreySense as administrator."; return Availability.Unavailable; }
            bool present;
            var services = ServiceController.GetServices();
            try { present = services.Any(s => s.ServiceName.Equals("PawnIO", StringComparison.OrdinalIgnoreCase)); }
            finally { foreach (var service in services) service.Dispose(); }
            if (!present)
            {
                if (ShouldOfferInstallation(result, present)) return Availability.Missing;
                detail = "PawnIO device access failed. The driver was not automatically reinstalled.";
                return Availability.Unavailable;
            }
            using var installed = new ServiceController("PawnIO");
            if (installed.Status == ServiceControllerStatus.Stopped) installed.Start();
            installed.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(5));
            if (io.Connect() == PawnIO.PawnIOWrapper.ConnectResult.OK) return Availability.Ready;
            detail = "PawnIO is installed but its device is unavailable. Restart Windows and try again.";
            return Availability.Unavailable;
        }
        catch (Exception ex)
        { detail = $"PawnIO could not be accessed: {ex.Message}"; return Availability.Unavailable; }
    }

    private static CustomModalDialog Dialog(Window owner, string message, string confirm, string cancel = "") => new()
    {
        Owner = owner, Title = "PreySense • PawnIO", HeaderSubtitle = " • PawnIO",
        Message = message, ConfirmText = confirm, CancelText = cancel, ShowCancelButton = !string.IsNullOrEmpty(cancel)
    };

    public static async Task<bool> EnsureInstalledAsync(Window owner, Action<string> report)
    {
        var inspection = await Task.Run(() => { var state = Inspect(out string detail); return (state, detail); });
        if (inspection.state == Availability.Ready) return true;
        if (inspection.state == Availability.Unavailable)
        { report(inspection.detail); return false; }

        bool previous = App.SuppressAutoHide;
        App.SuppressAutoHide = true;
        string? installer = null;
        CustomModalDialog? progress = null;
        try
        {
            if (Dialog(owner, "PawnIO is required for static CPU power limits and CPU VID. Install it now?", "Install PawnIO", "Continue").ShowDialog() != true)
            { report("PawnIO installation skipped. Static CPU limits and CPU VID are unavailable."); return false; }
            progress = Dialog(owner, "Downloading PawnIO...", "Please wait");
            progress.ConfirmEnabled = false;
            progress.Show();
            report("Downloading PawnIO...");
            string directory = Path.Combine(Path.GetTempPath(), "PreySense-PawnIO-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            installer = Path.Combine(directory, "PawnIO_setup.exe");
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) })
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("PreySense-PawnIO-Installer");
                byte[] payload = await client.GetByteArrayAsync(InstallerUrl);
                if (payload.Length < 2 || payload[0] != 'M' || payload[1] != 'Z')
                    throw new InvalidDataException("The PawnIO download was not an executable installer.");
                await File.WriteAllBytesAsync(installer, payload);
            }
            await VerifySignatureAsync(installer);
            progress.Message = "Installing PawnIO...";
            report("Installing PawnIO...");
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = installer, Arguments = "-install -silent", Verb = "runas", UseShellExecute = true
            }) ?? throw new InvalidOperationException("PawnIO installer did not start.");
            await process.WaitForExitAsync();
            if (!IsSuccessfulExitCode(process.ExitCode))
                throw new InvalidOperationException($"PawnIO installer returned exit code {process.ExitCode}.");
            var check = await Task.Run(() => { var state = Inspect(out string detail); return (state, detail); });
            if (RequiresRestart(process.ExitCode))
            {
                report("PawnIO installed. Restart Windows to finish setup.");
                progress.Close();
                progress = null;
                Dialog(owner, "PawnIO installed. Restart Windows to finish setup.", "OK").ShowDialog();
                return false;
            }
            if (check.state != Availability.Ready) throw new InvalidOperationException(check.detail);
            report("PawnIO installed successfully.");
            progress.Close();
            progress = null;
            Dialog(owner, "PawnIO installed successfully.", "OK").ShowDialog();
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"PawnIO automatic installation failed: {ex.Message}");
            report("PawnIO installation failed: " + ex.Message);
            progress?.Close();
            progress = null;
            if (Dialog(owner, "PawnIO installation did not complete. Open the official download page?", "Open Website", "Continue").ShowDialog() == true)
                Process.Start(new ProcessStartInfo { FileName = WebsiteUrl, UseShellExecute = true });
            return false;
        }
        finally
        {
            progress?.Close();
            App.SuppressAutoHide = previous;
            if (installer != null)
            {
                try { File.Delete(installer); Directory.Delete(Path.GetDirectoryName(installer)!); }
                catch { /* The installer may retain its file until a restart. */ }
            }
        }
    }

    private static async Task VerifySignatureAsync(string path)
    {
        string literal = path.Replace("'", "''");
        string script = $"$signature = Get-AuthenticodeSignature -LiteralPath '{literal}'; if ($signature.Status -ne 'Valid') {{ exit 1 }}";
        var info = new ProcessStartInfo
        {
            FileName = "powershell.exe", UseShellExecute = false, CreateNoWindow = true
        };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-EncodedCommand");
        info.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Installer signature verification could not start.");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidDataException("PawnIO installer signature verification failed.");
    }
}
