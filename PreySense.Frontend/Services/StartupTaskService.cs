using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;
namespace PreySense.Frontend.Services
{
    public static class StartupTaskService
    {
        public const string TaskName = "PreySense";
        private const string TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        public static bool IsStartupEnabled()
        {
            return QueryTaskExitCode() == 0;
        }

        public static bool SetStartupEnabled(bool enable)
        {
            if (!enable)
            {
                int deleted = RunSchtasks($"/Delete /F /TN \"{TaskName}\"").ExitCode;
                return deleted == 0 || QueryTaskExitCode() != 0;
            }

            string? exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(exePath))
                return false;

            return RegisterLogonTask(exePath);
        }

        /// <summary>
        /// schtasks defaults a logon task to "start only on AC power" and "stop when
        /// switching to battery". Re-register an existing task so logon starts PreySense
        /// on battery as well, without changing the executable it already points at.
        /// </summary>
        public static void RepairLogonTask()
        {
            if (!TryReadTaskXml(out string xml))
                return;

            if (xml.Contains("<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>", StringComparison.Ordinal)
                && xml.Contains("<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>", StringComparison.Ordinal)
                && xml.Contains("<StartWhenAvailable>true</StartWhenAvailable>", StringComparison.Ordinal)
                && xml.Contains("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>", StringComparison.Ordinal)
                && xml.Contains("<WorkingDirectory>", StringComparison.Ordinal))
            {
                return;
            }

            if (!TryReadCommand(xml, out string command))
                return;

            RegisterLogonTask(command);
        }

        public static string BuildLogonTaskXml(string exePath, string userId)
        {
            string command = SecurityElement.Escape(exePath) ?? exePath;
            string workingDirectory = SecurityElement.Escape(Path.GetDirectoryName(exePath)) ?? "";
            string user = SecurityElement.Escape(userId) ?? userId;
            return $"""
                <?xml version="1.0" encoding="UTF-16"?>
                <Task version="1.2" xmlns="{TaskNamespace}">
                  <RegistrationInfo>
                    <URI>\{TaskName}</URI>
                  </RegistrationInfo>
                  <Principals>
                    <Principal id="Author">
                      <UserId>{user}</UserId>
                      <LogonType>InteractiveToken</LogonType>
                      <RunLevel>HighestAvailable</RunLevel>
                    </Principal>
                  </Principals>
                  <Settings>
                    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                    <AllowHardTerminate>true</AllowHardTerminate>
                    <StartWhenAvailable>true</StartWhenAvailable>
                    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                    <AllowStartOnDemand>true</AllowStartOnDemand>
                    <Enabled>true</Enabled>
                    <Hidden>false</Hidden>
                    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                    <Priority>7</Priority>
                  </Settings>
                  <Triggers>
                    <LogonTrigger>
                      <Enabled>true</Enabled>
                      <UserId>{user}</UserId>
                    </LogonTrigger>
                  </Triggers>
                  <Actions Context="Author">
                    <Exec>
                      <Command>{command}</Command>
                      <Arguments>--minimized</Arguments>
                      <WorkingDirectory>{workingDirectory}</WorkingDirectory>
                    </Exec>
                  </Actions>
                </Task>
                """;
        }

        private static bool RegisterLogonTask(string exePath)
        {
            string? userId = WindowsIdentity.GetCurrent().User?.Value;
            if (string.IsNullOrWhiteSpace(userId))
                return false;

            string path = Path.Combine(Path.GetTempPath(), $"PreySense-startup-{Environment.ProcessId}.xml");
            try
            {
                File.WriteAllText(path, BuildLogonTaskXml(exePath, userId), Encoding.Unicode);
                return RunSchtasks($"/Create /F /TN \"{TaskName}\" /XML \"{path}\"").ExitCode == 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }

        private static int QueryTaskExitCode()
        {
            return RunSchtasks($"/Query /TN \"{TaskName}\"").ExitCode;
        }

        private static bool TryReadTaskXml(out string xml)
        {
            SchtasksResult result = RunSchtasks($"/Query /TN \"{TaskName}\" /XML");
            xml = result.Output;
            return result.ExitCode == 0 && xml.Contains("<Task", StringComparison.Ordinal);
        }

        private static bool TryReadCommand(string xml, out string command)
        {
            command = string.Empty;
            try
            {
                var document = XDocument.Parse(xml);
                XNamespace ns = TaskNamespace;
                string? value = document.Descendants(ns + "Command").FirstOrDefault()?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(value))
                    return false;
                if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                    value = value[1..^1];
                command = value;
                return command.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private readonly record struct SchtasksResult(int ExitCode, string Output);

        private static SchtasksResult RunSchtasks(string arguments)
        {
            try
            {
                using var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                if (proc == null)
                    return new SchtasksResult(-1, string.Empty);

                var stdout = proc.StandardOutput.ReadToEndAsync();
                var stderr = proc.StandardError.ReadToEndAsync();
                if (!proc.WaitForExit(15000))
                {
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    return new SchtasksResult(-1, string.Empty);
                }

                string output = stdout.GetAwaiter().GetResult();
                string error = stderr.GetAwaiter().GetResult();
                return new SchtasksResult(proc.ExitCode, string.IsNullOrWhiteSpace(output) ? error : output);
            }
            catch
            {
                return new SchtasksResult(-1, string.Empty);
            }
        }
    }
}
