using System.Globalization;
using PreySense.Localization;

static void AssertEqual(string expected, string actual, string message)
{
    if (!StringComparer.Ordinal.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{message}: expected '{expected}', got '{actual}'.");
    }
}

AssertEqual("Quiet", LocalizedText.Translate("Quiet", new CultureInfo("en-US")), "English fallback");
AssertEqual("静音", LocalizedText.Translate("Silent", new CultureInfo("zh-CN")), "Chinese UI translation");
AssertEqual("电池电量: --%", LocalizedText.BatteryChargeUnavailable(new CultureInfo("zh-CN")), "Chinese formatted battery status");
AssertEqual("Performance Mode: Turbo", LocalizedText.PerformanceMode("Turbo", false, new CultureInfo("en-US")), "English formatted performance mode");
AssertEqual("性能模式: Turbo（电池）", LocalizedText.PerformanceMode("Turbo", true, new CultureInfo("zh-CN")), "Chinese battery performance mode");
AssertEqual("列 3:", LocalizedText.ColumnLabel(3, new CultureInfo("zh-CN")), "Chinese metric column label");

CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
try
{
    CultureInfo.CurrentUICulture = new CultureInfo("zh-CN");
    LocalizedText.SetLanguage(LocalizedText.EnglishLanguageCode);
    AssertEqual("Silent", LocalizedText.Translate("Silent"), "English language override");

    CultureInfo.CurrentUICulture = new CultureInfo("en-US");
    LocalizedText.SetLanguage(LocalizedText.ChineseLanguageCode);
    AssertEqual("静音", LocalizedText.Translate("Silent"), "Chinese language override");

    AssertEqual(LocalizedText.SystemLanguageCode, LocalizedText.NormalizeLanguageCode(""), "Empty language preference uses system language");
    AssertEqual(LocalizedText.ChineseLanguageCode, LocalizedText.NormalizeLanguageCode("zh"), "Short Chinese language code normalizes to zh-CN");
}
finally
{
    CultureInfo.CurrentUICulture = originalUiCulture;
    LocalizedText.SetLanguage(LocalizedText.SystemLanguageCode);
}

Console.WriteLine("Localization tests passed.");
