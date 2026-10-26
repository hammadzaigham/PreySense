using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using PreySense.Helpers;

namespace PreySense.Frontend.Services
{
    public static class UpdateCheckService
    {
        public const string CurrentVersion = "2.4.0";
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

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var dialog = new Views.CustomModalDialog
                    {
                        Owner = Application.Current.MainWindow,
                        Title = "PreySense — Update Available",
                        HeaderSubtitle = " — Update Available",
                        Message = LocalizationService.Format("Version {0} of PreySense is available.\n\nOpen GitHub to download it?", latestTag),
                        ConfirmText = "Open GitHub",
                        CancelText = "Cancel",
                        ShowCancelButton = true
                    };

                    if (dialog.ShowDialog() != true)
                        return;

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = ReleasesUrl,
                        UseShellExecute = true
                    });
                });
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Update check failed: {ex.Message}");
            }
        }
    }
}
