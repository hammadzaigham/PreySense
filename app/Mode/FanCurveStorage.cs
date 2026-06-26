using System.Drawing;
using Microsoft.Win32;

namespace PreySense.Mode
{
    /// <summary>
    /// Persists CPU/GPU fan curves per performance mode under
    /// HKCU\SOFTWARE\PreySense\Profiles\{ModeName}. Falls back to the legacy
    /// global keys and migrates them on first read.
    /// </summary>
    public static class FanCurveStorage
    {
        private const int PointCount = 15;
        private const string LegacyBase = @"SOFTWARE\PreySense";
        private const string ProfilesBase = @"SOFTWARE\PreySense\Profiles";

        public static PointF[] DefaultCurve()
        {
            var curve = new PointF[PointCount];
            for (int i = 0; i < PointCount; i++)
                curve[i] = new PointF(40 + i * 5, 50f);
            return curve;
        }

        public static PointF[] LoadCpuCurve(byte mode) => LoadCurve(mode, "CpuCurve");

        public static PointF[] LoadGpuCurve(byte mode) => LoadCurve(mode, "GpuCurve");

        public static void SaveCpuCurve(byte mode, PointF[] curve) => SaveCurve(mode, "CpuCurve", curve);

        public static void SaveGpuCurve(byte mode, PointF[] curve) => SaveCurve(mode, "GpuCurve", curve);

        public static PointF[] LoadCurve(byte mode, string name)
        {
            var perMode = TryLoadFromKey($@"{ProfilesBase}\{ProfileManager.ModeToProfileName(mode)}", name);
            if (perMode != null)
                return perMode;

            var legacy = TryLoadFromKey(LegacyBase, name);
            if (legacy != null)
            {
                SaveCurve(mode, name, legacy);
                return legacy;
            }

            return DefaultCurve();
        }

        public static void SaveCurve(byte mode, string name, PointF[] curve)
        {
            if (curve == null || curve.Length == 0)
                return;

            try
            {
                string subKey = $@"{ProfilesBase}\{ProfileManager.ModeToProfileName(mode)}";
                using var key = Registry.CurrentUser.CreateSubKey(subKey);
                if (key == null)
                    return;

                for (int i = 0; i < Math.Min(curve.Length, PointCount); i++)
                    key.SetValue($"{name}_{i}_Y", (int)curve[i].Y);
            }
            catch { }
        }

        private static PointF[]? TryLoadFromKey(string subKeyPath, string name)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(subKeyPath);
                if (key == null)
                    return null;

                var points = new PointF[PointCount];
                for (int i = 0; i < PointCount; i++)
                {
                    object? yVal = key.GetValue($"{name}_{i}_Y");
                    if (yVal == null)
                        return null;
                    points[i] = new PointF(40 + i * 5, Convert.ToSingle(yVal));
                }
                return points;
            }
            catch
            {
                return null;
            }
        }
    }
}
