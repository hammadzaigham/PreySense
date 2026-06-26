using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace PreySense.Overlay
{
    public static class ProcessMemoryHelper
    {
        public static void TrimAfter(Task? prerequisite = null, TimeSpan? timeout = null)
        {
            Task.Run(async () =>
            {
                if (prerequisite != null)
                {
                    try
                    {
                        await prerequisite.WaitAsync(timeout ?? TimeSpan.FromSeconds(3));
                    }
                    catch { }
                }

                Trim();
            });
        }

        private static void Trim()
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized);

            try
            {
                using var p = Process.GetCurrentProcess();
                NativeMethods.SetProcessWorkingSetSize(p.Handle, -1, -1);
            }
            catch { }
        }
    }
}
