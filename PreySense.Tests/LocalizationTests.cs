using System.Xml.Linq;
using PreySense.Frontend.Services;

namespace PreySense.Tests;

[TestClass]
[DoNotParallelize]
public class LocalizationTests
{
    [TestMethod]
    public void CurrentXamlAndTelemetryLabels_HaveTranslationsInEveryLanguage()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PreySense.Frontend"));
        var labels = new HashSet<string>();
        foreach (string file in Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
                file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) continue;
            var document = System.Xml.Linq.XDocument.Load(file);
            foreach (var attribute in document.Descendants().Attributes())
            {
                if (attribute.Name.LocalName is not ("Title" or "Text" or "Content" or "Header" or "ToolTip" or "Description" or "ActionText" or "LeftText" or "RightText" or "Subtitle")) continue;
                string value = attribute.Value;
                if (value.StartsWith('{') || value.StartsWith("PreySense") || !System.Text.RegularExpressions.Regex.IsMatch(value, "[a-zA-Z]{2}")) continue;
                if (value is "CPU" or "GPU" or "N/A" or "MHz" or "RPM" or "W" or "V" or "GB" or "MT/s") continue;
                labels.Add(value);
            }
        }
        var window = typeof(PreySense.Frontend.Views.MetricsWindow);
        foreach (string field in new[] { "CpuChoices", "GpuChoices" })
        {
            var choices = (System.Collections.IEnumerable)window.GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
            foreach (object choice in choices)
                labels.Add((string)choice.GetType().GetProperty("Label")!.GetValue(choice)!);
        }
        labels.UnionWith(new[] { "Run on Startup", "Slot #{0}", "Include PROCHOT offset", "Applied and readback successfully.", "Apply failed." });
        foreach (string language in new[] { "zh-CN", "hi-IN", "es-ES", "fr-FR" })
        {
            var table = UiText.For(language)!;
            foreach (string label in labels)
                Assert.IsTrue(table.ContainsKey(label), $"{language}: missing {label}");
            foreach (var entry in table)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(entry.Value), $"{language}: empty {entry.Key}");
                var placeholders = System.Text.RegularExpressions.Regex.Matches(entry.Key, @"\{\d+[^}]*\}").Select(m => m.Value).Order().ToArray();
                var translated = System.Text.RegularExpressions.Regex.Matches(entry.Value, @"\{\d+[^}]*\}").Select(m => m.Value).Order().ToArray();
                CollectionAssert.AreEqual(placeholders, translated, $"{language}: placeholders {entry.Key}");
            }
        }
    }

    [TestMethod]
    public void Test_Five_Languages_Translate_Labels_And_Dialogs()
    {
        string previous = LocalizationService.CurrentLanguage;
        try
        {
            LocalizationService.SetLanguage("en-US", persist: false);
            Assert.AreEqual("Quit", LocalizationService.Translate("Quit"));
            Assert.AreEqual("PreySense • Lighting", LocalizationService.Translate("PreySense • Lighting"));
            Assert.AreEqual(0, LocalizationService.LanguageIndex);

            LocalizationService.SetLanguage("zh-CN", persist: false);
            Assert.AreEqual("退出", LocalizationService.Translate("Quit"));
            Assert.AreEqual("电池充电限制", LocalizationService.Translate("Battery Limit"));
            Assert.AreEqual("混合", LocalizationService.Translate("Hybrid"));
            Assert.AreEqual("关闭", LocalizationService.Translate("Off"));
            Assert.AreEqual("无", LocalizationService.Translate("None"));
            Assert.AreEqual("PreySense • 灯光", LocalizationService.Translate("PreySense • Lighting"));
            Assert.AreEqual(" • 硬件显示切换", LocalizationService.Translate(" • Hardware Display MUX"));
            Assert.AreEqual("正在读取节能…", LocalizationService.Format("Reading {0}...", LocalizationService.Translate("Eco")));
            Assert.AreEqual(1, LocalizationService.LanguageIndex);

            LocalizationService.SetLanguage("hi-IN", persist: false);
            Assert.AreEqual("बाहर निकलें", LocalizationService.Translate("Quit"));
            Assert.AreNotEqual("Quit", LocalizationService.Translate("Battery Limit"));
            Assert.AreEqual(2, LocalizationService.LanguageIndex);

            LocalizationService.SetLanguage("es-ES", persist: false);
            Assert.AreEqual("Salir", LocalizationService.Translate("Quit"));
            Assert.AreEqual("Límite de batería", LocalizationService.Translate("Battery Limit"));
            Assert.AreEqual("¿Restablecer Equilibrado a los ajustes de fábrica y aplicarlos ahora?",
                LocalizationService.Format("Reset {0} to factory power settings and apply them now?", LocalizationService.Translate("Balanced")));
            Assert.AreEqual(3, LocalizationService.LanguageIndex);

            LocalizationService.SetLanguage("fr-FR", persist: false);
            Assert.AreEqual("Quitter", LocalizationService.Translate("Quit"));
            Assert.AreEqual("Limite de batterie", LocalizationService.Translate("Battery Limit"));
            Assert.AreEqual("PreySense • Éclairage", LocalizationService.Translate("PreySense • Lighting"));
            Assert.AreEqual(4, LocalizationService.LanguageIndex);

            Assert.AreEqual("en-US", LocalizationService.Normalize("en-GB"));
            Assert.AreEqual("zh-CN", LocalizationService.Normalize("zh-CN"));
            Assert.AreEqual("hi-IN", LocalizationService.Normalize("hi"));
            Assert.AreEqual("es-ES", LocalizationService.Normalize("es-MX"));
            Assert.AreEqual("fr-FR", LocalizationService.Normalize("fr-CA"));
        }
        finally
        {
            LocalizationService.SetLanguage(previous, persist: false);
        }
    }
}
