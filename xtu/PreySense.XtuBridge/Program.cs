using System;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Threading.Tasks;

namespace PreySense.XtuBridge
{
    internal static class Program
    {
        private const string XtuClientDirectory = @"C:\Program Files\Intel\Intel(R) Extreme Tuning Utility\Client";
        private const uint XtuControlPl2 = 47;
        private const uint XtuControlPl1 = 48;

        private static int Main(string[] args)
        {
            if (args.Length < 2 || !int.TryParse(args[0], out int pl1Watts) || !int.TryParse(args[1], out int pl2Watts))
            {
                Console.Error.WriteLine("Usage: PreySense.XtuBridge.exe <pl1Watts> <pl2Watts>");
                return 2;
            }

            try
            {
                if (!IsAdministrator())
                {
                    Console.Error.WriteLine("XTU bridge requires administrator privileges.");
                    return 13;
                }

                AppDomain.CurrentDomain.AssemblyResolve += ResolveXtuAssembly;
                Task<int> worker = Task.Run(() => RunXtu(pl1Watts, pl2Watts));
                if (!worker.Wait(TimeSpan.FromSeconds(12)))
                {
                    Console.Error.WriteLine("XTU bridge timed out while waiting for the Intel SDK.");
                    return 12;
                }

                return worker.Result;
            }
            catch (AggregateException ex)
            {
                Exception inner = ex.Flatten().InnerException ?? ex;
                Console.Error.WriteLine(inner.Message);
                return 11;
            }
        }

        private static bool IsAdministrator()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        private static int RunXtu(int pl1Watts, int pl2Watts)
        {
            try
            {
                Console.Out.WriteLine("XTU bridge: loading tuning SDK.");

                string sdkPath = Path.Combine(XtuClientDirectory, "IntelOverclockingSDK.dll");
                if (!File.Exists(sdkPath))
                {
                    Console.Error.WriteLine("XTU SDK DLL not found.");
                    return 3;
                }

                Assembly sdkAssembly = Assembly.LoadFrom(sdkPath);
                Type tuningType = sdkAssembly.GetType("Intel.Overclocking.SDK.Tuning.TuningLibrary", throwOnError: false);
                if (tuningType == null)
                {
                    Console.Error.WriteLine("XTU TuningLibrary type not found.");
                    return 4;
                }

                Console.Out.WriteLine("XTU bridge: resolving tuning singleton.");
                object tuning = tuningType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null, null);
                if (tuning == null)
                {
                    Console.Error.WriteLine("XTU TuningLibrary instance not available.");
                    return 5;
                }

                Console.Out.WriteLine("XTU bridge: initializing tuning.");
                MethodInfo initializeTuning = tuning.GetType().GetMethod("Initialize", Type.EmptyTypes);
                if (initializeTuning != null && initializeTuning.ReturnType == typeof(bool))
                {
                    object initResult = initializeTuning.Invoke(tuning, null);
                    if (initResult is bool ok && !ok)
                    {
                        Console.Error.WriteLine("XTU tuning initialization returned false.");
                        return 8;
                    }
                }

                Console.Out.WriteLine("XTU bridge: tuning PL1/PL2.");
                bool pl1Ok = Tune(tuning, XtuControlPl1, pl1Watts);
                bool pl2Ok = Tune(tuning, XtuControlPl2, pl2Watts);
                Console.Out.WriteLine($"XTU Tune PL1={pl1Ok}, PL2={pl2Ok} ({pl1Watts}W/{pl2Watts}W).");
                return pl1Ok && pl2Ok ? 0 : 9;
            }
            catch (TargetInvocationException ex)
            {
                Console.Error.WriteLine(ex.InnerException?.Message ?? ex.Message);
                return 10;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 11;
            }
        }

        private static bool Tune(object tuning, uint controlId, decimal value)
        {
            MethodInfo tune = tuning.GetType().GetMethod("Tune", new[] { typeof(uint), typeof(decimal), typeof(bool) });
            if (tune == null)
            {
                Console.Error.WriteLine($"XTU Tune(uint, decimal, bool) not found for control {controlId}.");
                return false;
            }

            object result = tune.Invoke(tuning, new object[] { controlId, value, true });
            string resultText = result?.ToString() ?? "null";
            bool ok = resultText.IndexOf("Fail", StringComparison.OrdinalIgnoreCase) < 0 &&
                      resultText.IndexOf("Error", StringComparison.OrdinalIgnoreCase) < 0;
            if (!ok)
                Console.Error.WriteLine($"XTU control {controlId} returned {resultText}.");

            return ok;
        }

        private static Assembly ResolveXtuAssembly(object sender, ResolveEventArgs args)
        {
            try
            {
                string assemblyFile = new AssemblyName(args.Name).Name + ".dll";
                string xtuAssemblyPath = Path.Combine(XtuClientDirectory, assemblyFile);
                if (File.Exists(xtuAssemblyPath))
                    return Assembly.LoadFrom(xtuAssemblyPath);
            }
            catch
            {
            }

            return null;
        }
    }
}
