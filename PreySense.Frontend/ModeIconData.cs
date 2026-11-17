using System.Collections.Generic;
using System.Windows.Media;

namespace PreySense.Frontend;

public static class ModeIconData
{
    public const string Eco = "M19 3C11 3 5 7 5 13C5 17 8 20 12 20C18 20 21 12 21 3C20 5 16 5 12 6C7 7 4 11 4 15C4 17 5 19 7 20C8 13 12 8 19 3Z";
    public const string Quiet = "M20 15C18 16 16 17 14 17C9 17 6 13 6 8C6 6 7 4 8 2C4 4 2 8 2 12C2 18 7 22 13 22C17 22 20 19 22 16C21 16 21 15 20 15Z";
    public const string Balanced = "M3 5H14V7H3ZM18 3H20V9H18ZM3 11H8V13H3ZM12 9H14V15H12ZM3 17H16V19H3ZM20 15H22V21H20Z";
    public const string Performance = "M13 2L3 14H11L10 22L21 9H13L13 2Z";
    public const string Turbo = "M13 2C14 7 20 9 20 15C20 20 16 22 12 22C7 22 4 19 4 15C4 11 7 8 10 5C10 8 11 10 13 11C15 9 15 6 13 2ZM12 13C10 15 9 16 9 18C9 20 10 21 12 21C14 21 16 20 16 18C16 16 14 14 12 13Z";
    public const string HybridGpu = "M3 4H21V17H3ZM1 20H23M8 9H17M14 6L17 9L14 12M16 13H7M10 10L7 13L10 16";

    private static readonly Dictionary<string, Geometry> Cache = new();

    public static Geometry EcoGeometry => GetOrParse(Eco);
    public static Geometry QuietGeometry => GetOrParse(Quiet);
    public static Geometry BalancedGeometry => GetOrParse(Balanced);
    public static Geometry PerformanceGeometry => GetOrParse(Performance);
    public static Geometry TurboGeometry => GetOrParse(Turbo);
    public static Geometry HybridGpuGeometry => GetOrParse(HybridGpu);

    private static Geometry GetOrParse(string path)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(path, out var geometry))
            {
                geometry = Geometry.Parse(path);
                geometry.Freeze();
                Cache[path] = geometry;
            }
            return geometry;
        }
    }
}
