using Microsoft.Win32;

namespace PreySense.Core.Hardware;

/// <summary>SMBIOS product name and the few model quirks that cannot be discovered at runtime.</summary>
public static class AcerModel
{
    private static readonly Lazy<string> Product = new(ReadProductName, true);

    /// <summary>SMBIOS system product name, e.g. "Predator PHN16-73"; empty when unavailable.</summary>
    public static string ProductName => Product.Value;

    /// <summary>
    /// Models whose EC keyboard-backlight timeout (APGe 0x88402) is stored but never turns off the
    /// ENEK5130-driven keyboard. Verified on PHN16-73 (Macan_ARX, BIOS V1.28): the EC reports the
    /// configured timeout, yet the backlight stays on indefinitely without Acer's lighting service.
    /// </summary>
    private static readonly string[] SoftwareKeyboardIdleTimeoutModels = ["PHN16-73"];

    public static bool RequiresSoftwareKeyboardIdleTimeout => RequiresSoftwareKeyboardIdleTimeoutFor(ProductName);

    internal static bool RequiresSoftwareKeyboardIdleTimeoutFor(string productName)
    {
        foreach (string token in productName.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (string model in SoftwareKeyboardIdleTimeoutModels)
                if (token.Equals(model, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string ReadProductName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            return (key?.GetValue("SystemProductName") as string)?.Trim() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
