using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using Microsoft.Win32;
using PreySense.Helpers;

namespace PreySense.Core.Services
{
    public class ServiceStatusInfo
    {
        public string ServiceName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public bool IsInstalled { get; set; }
        public bool IsRunning { get; set; }
        public ServiceStartMode StartType { get; set; } = ServiceStartMode.Manual;
    }

    public static class AcerServiceDebloater
    {
        public static readonly string[] ManagedServices = new[]
        {
            "AASSvc",
            "AcerCCAgentSvis",
            "AcerDeviceEnablingServiceV2",
            "AcerDIAgentSvis",
            "AcerLightingService",
            "AcerQAAgentSvis",
            "AcerServiceSvc",
            "AICO2_svc",
            "AICO_svc",
            "ASMSvc",
            "AcerSysHardwareService",
            "AcerHardwareService",
            "PredatorSenseService",
            "AcerCentralService"
        };

        private static readonly string[] ExtraProcessNames =
        {
            "PredatorSense",
            "AcerService",
            "AcerQuickAccess",
            "AcerAgentService",
            "AcerCentralService",
            "AcerHardwareService",
            "AcerQAAgent",
            "AcerCCAgent",
            "AcerDIAgent",
            "ADESv2Svc",
            "AcerServiceWrapper",
            "AcerSystemCentralService",
            "AICO_svc",
            "AICO2_svc"
        };

        private static readonly HashSet<string> ExcludedServiceNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "PreySense",
            "Psched",
            "IntcAzAudAddService",
            "DtsApo4Service",
            "AcerApplicationBaseDriver_Device"
        };

        /// <summary>
        /// Returns all known and dynamically discovered Acer/Predator services on this machine.
        /// </summary>
        public static List<string> GetAllAcerServiceNames()
        {
            var services = new HashSet<string>(ManagedServices, StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var sc in ServiceController.GetServices())
                {
                    using (sc)
                    {
                        string sName = sc.ServiceName;
                        string dName = sc.DisplayName ?? "";

                        if (ExcludedServiceNames.Contains(sName))
                            continue;

                        bool isMatch = sName.StartsWith("Acer", StringComparison.OrdinalIgnoreCase)
                            || sName.StartsWith("Predator", StringComparison.OrdinalIgnoreCase)
                            || sName.Equals("AASSvc", StringComparison.OrdinalIgnoreCase)
                            || sName.Equals("ASMSvc", StringComparison.OrdinalIgnoreCase)
                            || sName.StartsWith("AICO", StringComparison.OrdinalIgnoreCase)
                            || dName.Contains("Acer", StringComparison.OrdinalIgnoreCase)
                            || dName.Contains("Predator", StringComparison.OrdinalIgnoreCase);

                        if (isMatch)
                        {
                            services.Add(sName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"AcerServiceDebloater: Error enumerating system services: {ex.Message}");
            }

            return services.ToList();
        }

        public static List<ServiceStatusInfo> GetServicesStatus()
        {
            var list = new List<ServiceStatusInfo>();
            var allNames = GetAllAcerServiceNames();
            foreach (var name in allNames)
            {
                try
                {
                    using var sc = new ServiceController(name);
                    list.Add(new ServiceStatusInfo
                    {
                        ServiceName = name,
                        DisplayName = sc.DisplayName,
                        IsInstalled = true,
                        IsRunning = sc.Status == ServiceControllerStatus.Running,
                        StartType = sc.StartType
                    });
                }
                catch
                {
                    list.Add(new ServiceStatusInfo
                    {
                        ServiceName = name,
                        DisplayName = name,
                        IsInstalled = false,
                        IsRunning = false
                    });
                }
            }
            return list;
        }

        public static bool AreAllServicesDisabled()
        {
            var allNames = GetAllAcerServiceNames();
            foreach (var name in allNames)
            {
                try
                {
                    using var sc = new ServiceController(name);
                    if (sc.Status == ServiceControllerStatus.Running || sc.StartType != ServiceStartMode.Disabled)
                    {
                        return false;
                    }
                }
                catch
                {
                    // If service not installed, ignore
                }
            }
            return true;
        }

        public static bool StopAndDisableAll()
        {
            bool allSucceeded = true;
            var services = GetAllAcerServiceNames();
            foreach (var name in services)
            {
                try
                {
                    SetServiceStartMode(name, "disabled");
                    using var sc = new ServiceController(name);
                    if (sc.Status == ServiceControllerStatus.Running || sc.Status == ServiceControllerStatus.StartPending)
                    {
                        sc.Stop();
                        try
                        {
                            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(3));
                        }
                        catch (System.ServiceProcess.TimeoutException)
                        {
                        }
                    }
                    AppLogger.Log($"AcerServiceDebloater: Stopped and disabled {name}");
                }
                catch (Exception ex)
                {
                    allSucceeded = false;
                    AppLogger.Log($"AcerServiceDebloater: Failed to stop/disable {name}: {ex.Message}");
                }
            }

            KillRunningAcerProcesses();
            if (!RemoveAcerScheduledTasks())
                allSucceeded = false;
            SaveState(isDebloated: true);
            return allSucceeded;
        }

        public static void KillRunningAcerProcesses()
        {
            int self = Environment.ProcessId;
            var managedServices = GetAllAcerServiceNames();

            foreach (Process process in Process.GetProcesses())
            {
                using (process)
                {
                    string name;
                    try
                    {
                        if (process.Id == self)
                            continue;
                        name = process.ProcessName;
                    }
                    catch
                    {
                        continue;
                    }

                    if (name.Equals("PreySense", StringComparison.OrdinalIgnoreCase))
                        continue;

                    bool isManagedService = managedServices.Any(service =>
                        service.Equals(name, StringComparison.OrdinalIgnoreCase));
                    bool isAcerProcess = name.StartsWith("Acer", StringComparison.OrdinalIgnoreCase);
                    bool isExtra = ExtraProcessNames.Any(extra =>
                        extra.Equals(name, StringComparison.OrdinalIgnoreCase));
                    bool isAcerOwnedBinary = IsAcerOwnedBinary(process);
                    if (!isManagedService && !isAcerProcess && !isExtra && !isAcerOwnedBinary)
                        continue;

                    try
                    {
                        process.Kill(entireProcessTree: true);
                        AppLogger.Log($"AcerServiceDebloater: Killed {name} (PID {process.Id})");
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Log($"AcerServiceDebloater: Failed to kill {name}: {ex.Message}");
                    }
                }
            }
        }

        private static bool IsAcerOwnedBinary(Process process)
        {
            try
            {
                string path = process.MainModule?.FileName ?? "";
                if (path.Contains(@"\Acer\", StringComparison.OrdinalIgnoreCase)
                    || path.Contains(@"\Predator", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("predatorservice.inf_", StringComparison.OrdinalIgnoreCase))
                    return true;

                string company = process.MainModule?.FileVersionInfo.CompanyName ?? "";
                return company.Contains("Acer", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool RemoveAcerScheduledTasks()
        {
            try
            {
                Type? schedulerType = Type.GetTypeFromProgID("Schedule.Service");
                if (schedulerType == null)
                    return false;

                dynamic scheduler = Activator.CreateInstance(schedulerType)!;
                scheduler.Connect();
                return RemoveAcerScheduledTasksFromFolder(scheduler.GetFolder(@"\"));
            }
            catch (Exception ex)
            {
                AppLogger.Log($"AcerServiceDebloater: Failed to inspect scheduled tasks: {ex.Message}");
                return false;
            }
        }

        private static bool RemoveAcerScheduledTasksFromFolder(dynamic folder)
        {
            bool allSucceeded = true;

            foreach (dynamic subFolder in folder.GetFolders(0))
            {
                if (!RemoveAcerScheduledTasksFromFolder(subFolder))
                    allSucceeded = false;
            }

            var tasksToDelete = new List<string>();
            foreach (dynamic task in folder.GetTasks(1))
            {
                if (IsAcerScheduledTask(task))
                    tasksToDelete.Add((string)task.Name);
            }

            foreach (string taskName in tasksToDelete)
            {
                try
                {
                    folder.DeleteTask(taskName, 0);
                    AppLogger.Log($"AcerServiceDebloater: Removed scheduled task {taskName}");
                }
                catch (Exception ex)
                {
                    allSucceeded = false;
                    AppLogger.Log($"AcerServiceDebloater: Failed to remove scheduled task {taskName}: {ex.Message}");
                }
            }

            return allSucceeded;
        }

        private static bool IsAcerScheduledTask(dynamic task)
        {
            string identity = $"{task.Name} {task.Path}";
            try
            {
                foreach (dynamic action in task.Definition.Actions)
                    identity += $" {action.Path} {action.Arguments} {action.WorkingDirectory}";
            }
            catch
            {
                // Some non-executable action types do not expose executable properties.
            }

            return identity.Contains("Acer", StringComparison.OrdinalIgnoreCase)
                || identity.Contains("Predator", StringComparison.OrdinalIgnoreCase)
                || identity.Contains("CareCenter", StringComparison.OrdinalIgnoreCase)
                || identity.Contains("QuickAccess", StringComparison.OrdinalIgnoreCase)
                || identity.Contains("DeviceInfo", StringComparison.OrdinalIgnoreCase);
        }

        public static bool EnableAndStartAll()
        {
            bool allSucceeded = true;
            var services = GetAllAcerServiceNames();
            foreach (var name in services)
            {
                try
                {
                    SetServiceStartMode(name, "auto");
                    using var sc = new ServiceController(name);
                    if (sc.Status != ServiceControllerStatus.Running)
                    {
                        sc.Start();
                        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(3));
                    }
                    AppLogger.Log($"AcerServiceDebloater: Enabled and started {name}");
                }
                catch (Exception ex)
                {
                    allSucceeded = false;
                    AppLogger.Log($"AcerServiceDebloater: Failed to start {name}: {ex.Message}");
                }
            }

            SaveState(isDebloated: false);
            return allSucceeded;
        }

        public static void SaveState(bool isDebloated)
        {
            try
            {
                Configuration.ConfigurationService.Instance.Update(cfg =>
                {
                    cfg.General.AcerServicesDebloated = isDebloated;
                });
            }
            catch (Exception ex)
            {
                AppLogger.Log($"AcerServiceDebloater: Failed to write configuration state: {ex.Message}");
            }
        }

        /// <summary>Reads the JSON preference. Returns false if config cannot be loaded.</summary>
        public static bool GetSavedState()
        {
            try
            {
                return Configuration.ConfigurationService.Instance.GetCurrent().General.AcerServicesDebloated;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"AcerServiceDebloater: Failed to read configuration state: {ex.Message}");
                return false;
            }
        }

        private static void SetServiceStartMode(string serviceName, string mode)
        {
            SetServiceStartModeRegistry(serviceName, mode.Equals("disabled", StringComparison.OrdinalIgnoreCase) ? 4 : 2);
        }

        private static void SetServiceStartModeRegistry(string serviceName, int startDword)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    $@"SYSTEM\CurrentControlSet\Services\{serviceName}", writable: true);
                key?.SetValue("Start", startDword, RegistryValueKind.DWord);
            }
            catch
            {
                // Non-fatal for services that are not installed.
            }
        }
    }
}
