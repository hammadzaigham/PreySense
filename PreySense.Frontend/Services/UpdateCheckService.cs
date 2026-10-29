using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using PreySense.Helpers;

namespace PreySense.Frontend.Services
{
    public static class UpdateCheckService
    {
        public const string CurrentVersion = "2.4.8";
        private const string LatestReleaseApi = "https://api.github.com/repos/hammadzaigham/PreySense/releases/latest";
        private const string ReleasesUrl = "https://github.com/hammadzaigham/PreySense/releases";

        public static async Task CheckForUpdatesAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("PreySense-Updater");
                client.Timeout = TimeSpan.FromSeconds(8);

                string json = await client.GetStringAsync(LatestReleaseApi);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("tag_name", out var tagProperty))
                    return;

                string? latestTag = tagProperty.GetString();
                if (string.IsNullOrWhiteSpace(latestTag))
                    return;

                if (!Version.TryParse(latestTag.TrimStart('v', 'V'), out Version? latest) ||
                    !Version.TryParse(CurrentVersion, out Version? current) ||
                    latest <= current)
                {
                    return;
                }

                string? downloadUrl = null;
                if (doc.RootElement.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        if (asset.TryGetProperty("name", out var name) &&
                            string.Equals(name.GetString(), "PreySense.exe", StringComparison.OrdinalIgnoreCase) &&
                            asset.TryGetProperty("browser_download_url", out var url))
                        {
                            downloadUrl = url.GetString();
                            break;
                        }
                    }
                }

                bool shouldInstall = await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var dialog = new Views.CustomModalDialog
                    {
                        Owner = Application.Current.MainWindow,
                        Title = "PreySense — Update Available",
                        HeaderSubtitle = " — Update Available",
                        Message = LocalizationService.Format("Version {0} of PreySense is available.\n\nDownload and install it now?", latestTag),
                        ConfirmText = "Download and Install",
                        CancelText = "Cancel",
                        ShowCancelButton = true
                    };

                    return dialog.ShowDialog() == true;
                });

                if (!shouldInstall)
                    return;

                if (!string.IsNullOrWhiteSpace(downloadUrl))
                {
                    try
                    {
                        await DownloadAndInstallAsync(downloadUrl);
                        return;
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Log($"Automatic update failed: {ex.Message}");
                    }
                }

                Process.Start(new ProcessStartInfo { FileName = ReleasesUrl, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Update check failed: {ex.Message}");
            }
        }

        private static async Task DownloadAndInstallAsync(string downloadUrl)
        {
            string currentExe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? throw new InvalidOperationException("Could not determine the running executable path.");
            string tempExe = Path.Combine(Path.GetTempPath(), $"PreySense-update-{Guid.NewGuid():N}.exe");

            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PreySense-Updater");
            using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using (var source = await response.Content.ReadAsStreamAsync())
            await using (var destination = File.Create(tempExe))
            {
                await source.CopyToAsync(destination);
            }

            var info = new FileInfo(tempExe);
            if (!info.Exists || info.Length < 100_000)
                throw new InvalidDataException("GitHub returned an invalid update file.");

            int processId = Process.GetCurrentProcess().Id;
            string script = $"$ErrorActionPreference='Stop'; $processId={processId}; $source={QuoteForPowerShell(tempExe)}; $target={QuoteForPowerShell(currentExe)}; while (Get-Process -Id $processId -ErrorAction SilentlyContinue) {{ Start-Sleep -Milliseconds 250 }}; for ($attempt=0; $attempt -lt 80; $attempt++) {{ try {{ Move-Item -LiteralPath $source -Destination $target -Force; Start-Process -FilePath $target; exit 0 }} catch {{ if ($attempt -eq 79) {{ throw }}; Start-Sleep -Milliseconds 250 }} }}; throw 'Timed out replacing the application executable.'";
            string encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                UseShellExecute = false,
                CreateNoWindow = true
            });

            Application.Current.Shutdown();
        }

        private static string QuoteForPowerShell(string value) =>
            "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }
}
