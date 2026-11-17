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
        public static string CurrentVersion => typeof(UpdateCheckService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        public static string DisplayVersion => "v" + CurrentVersion;
        private const string LatestReleaseApi = "https://api.github.com/repos/hammadzaigham/PreySense/releases/latest";
        private const string ReleasesUrl = "https://github.com/hammadzaigham/PreySense/releases";
        private static readonly HttpClient SharedClient = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(8)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PreySense");
            return client;
        }

        public static async Task CheckForUpdatesAsync()
        {
            try
            {
                string json = await SharedClient.GetStringAsync(LatestReleaseApi);
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

                string releaseUrl = ReleasesUrl;
                if (doc.RootElement.TryGetProperty("html_url", out var htmlUrl) &&
                    htmlUrl.GetString() is { Length: > 0 } pageUrl &&
                    Uri.TryCreate(pageUrl, UriKind.Absolute, out var uri) &&
                    uri.Scheme == Uri.UriSchemeHttps &&
                    (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
                     uri.Host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase)))
                {
                    releaseUrl = uri.AbsoluteUri;
                }

                var app = Application.Current;
                if (app?.Dispatcher == null || app.Dispatcher.HasShutdownStarted)
                    return;

                bool openDownload = await app.Dispatcher.InvokeAsync(() =>
                {
                    if (app.MainWindow is not { IsVisible: true } owner)
                        return false;

                    var dialog = new Views.CustomModalDialog
                    {
                        Owner = owner,
                        Title = "PreySense — Update Available",
                        HeaderSubtitle = " — Update Available",
                        Message = LocalizationService.Format("Version {0} of PreySense is available.", latestTag),
                        ConfirmText = "Download",
                        CancelText = "Later",
                        ShowCancelButton = true
                    };

                    return dialog.ShowDialog() == true;
                });

                if (!openDownload)
                    return;

                Process.Start(new ProcessStartInfo { FileName = releaseUrl, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                string msg = (ex is HttpRequestException || ex is System.Net.Sockets.SocketException)
                    ? $"Update check skipped (network unreachable: {ex.Message})"
                    : $"Update check failed: {ex.Message}";
                AppLogger.Log(msg);
            }
        }
    }
}
