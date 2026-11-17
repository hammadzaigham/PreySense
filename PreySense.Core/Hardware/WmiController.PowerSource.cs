using PreySense.Core.Services;

namespace PreySense.Helpers;

public partial class WmiController
{
    public static bool TryParseAcerPowerEvent(byte[]? detail, out PowerSourceKind source)
    {
        source = default;
        if (detail == null || detail.Length < 2 || detail[0] != 8)
            return false;

        switch (detail[1])
        {
            case 0:
                source = PowerSourceKind.Battery;
                return true;
            case 1:
                source = PowerSourceKind.Ac;
                return true;
            case 4:
                source = PowerSourceKind.Ac;
                return true;
            default:
                return false;
        }
    }
}
