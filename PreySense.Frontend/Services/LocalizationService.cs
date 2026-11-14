using PreySense.Core.Configuration;
using PreySense.Frontend.Controls;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace PreySense.Frontend.Services;

public static class LocalizationService
{
    private static readonly string[] TitlePrefixes = ["PreySense • ", "PreySense — ", " • ", " — "];
    private static readonly ConditionalWeakTable<DependencyObject, Dictionary<DependencyProperty, string>> Originals = new();

    public static string CurrentLanguage { get; private set; } = "en-US";
    public static bool IsChinese => CurrentLanguage.Equals("zh-CN", StringComparison.OrdinalIgnoreCase);
    public static int LanguageIndex => Math.Max(0, Array.IndexOf(UiText.Languages, CurrentLanguage));

    public static string Normalize(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return "en-US";
        if (language.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return "zh-CN";
        if (language.StartsWith("hi", StringComparison.OrdinalIgnoreCase)) return "hi-IN";
        if (language.StartsWith("es", StringComparison.OrdinalIgnoreCase)) return "es-ES";
        if (language.StartsWith("fr", StringComparison.OrdinalIgnoreCase)) return "fr-FR";
        if (language.StartsWith("tr", StringComparison.OrdinalIgnoreCase)) return "tr-TR";
        return "en-US";
    }

    public static void Initialize()
    {
        string saved = ConfigurationService.Instance.GetCurrent().General.Language;
        if (string.IsNullOrWhiteSpace(saved))
            saved = CultureInfo.CurrentUICulture.Name;
        CurrentLanguage = Normalize(saved);
        ApplyCulture();
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => LocalizeWindow((Window)sender)));
    }

    public static void SetLanguage(string language, bool persist = true)
    {
        string normalized = Normalize(language);
        if (normalized == CurrentLanguage)
            return;
        CurrentLanguage = normalized;
        ApplyCulture();
        if (!persist)
            return;
        ConfigurationService.Instance.Update(cfg => cfg.General.Language = CurrentLanguage);
        if (Application.Current == null)
            return;
        var viewModels = new HashSet<ViewModels.MainViewModel>();
        foreach (Window window in Application.Current.Windows)
        {
            LocalizeWindow(window);
            if (window.DataContext is ViewModels.MainViewModel vm)
                viewModels.Add(vm);
        }
        foreach (var vm in viewModels) vm.RefreshLocalizedText();
    }

    public static string Translate(string? text)
    {
        if (string.IsNullOrEmpty(text) || CurrentLanguage == "en-US")
            return text ?? string.Empty;

        IReadOnlyDictionary<string, string>? table = UiText.For(CurrentLanguage);
        if (table == null)
            return text;
        if (table.TryGetValue(text, out string? translated))
            return translated;

        foreach (string prefix in TitlePrefixes)
        {
            if (!text.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            string rest = text[prefix.Length..];
            if (table.TryGetValue(rest, out string? page))
                return prefix + page;
        }

        return text;
    }

    public static string Format(string template, params object?[] args)
        => string.Format(CultureInfo.CurrentUICulture, Translate(template), args);

    public static void LocalizeWindow(Window window)
    {
        window.Language = System.Windows.Markup.XmlLanguage.GetLanguage(CurrentLanguage);
        Apply(window, Window.TitleProperty);
        if (window.Content is DependencyObject content)
            LocalizeTree(content);

        var combos = new List<ComboBox>();
        if (window.Content is DependencyObject root)
            CollectCombos(root, combos);
        foreach (ComboBox combo in combos)
        {
            if (combo.ItemsSource != null && combo.Items.Count > 0)
                combo.Items.Refresh();
        }
    }

    private static void ApplyCulture()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(CurrentLanguage);
    }

    private static void CollectCombos(DependencyObject element, List<ComboBox> combos)
    {
        if (element is ComboBox combo)
            combos.Add(combo);
        int children = VisualTreeHelper.GetChildrenCount(element);
        for (int i = 0; i < children; i++)
            CollectCombos(VisualTreeHelper.GetChild(element, i), combos);
    }

    private static void LocalizeTree(DependencyObject? element)
    {
        if (element == null) return;
        Apply(element, System.Windows.Automation.AutomationProperties.NameProperty);

        switch (element)
        {
            case SettingSwitchRow row:
                Apply(row, SettingSwitchRow.TitleProperty);
                Apply(row, SettingSwitchRow.OnTextProperty); Apply(row, SettingSwitchRow.OffTextProperty); return;
            case SettingSliderRow slider:
                Apply(slider, SettingSliderRow.TitleProperty); break;
            case SettingDropdownRow row:
                Apply(row, SettingDropdownRow.TitleProperty); break;
            case SettingActionRow row:
                Apply(row, SettingActionRow.TitleProperty); Apply(row, SettingActionRow.ActionTextProperty); return;
            case SettingValueRow row:
                Apply(row, SettingValueRow.TitleProperty); return;
            case WindowTitleBar bar:
                Apply(bar, WindowTitleBar.TitleProperty);
                Apply(bar, WindowTitleBar.SubtitleProperty);
                if (bar.Actions is DependencyObject actions)
                    LocalizeTree(actions);
                return;
            case LabeledTabSwitcher tabs:
                Apply(tabs, LabeledTabSwitcher.LeftTextProperty); Apply(tabs, LabeledTabSwitcher.RightTextProperty); return;
            case TextBlock text:
                if (text.Inlines.Count > 1)
                    LocalizeInlines(text.Inlines);
                else if (!BindingOperations.IsDataBound(text, TextBlock.TextProperty))
                    Apply(text, TextBlock.TextProperty);
                break;
            case HeaderedContentControl header when !BindingOperations.IsDataBound(header, HeaderedContentControl.HeaderProperty):
                Apply(header, HeaderedContentControl.HeaderProperty); break;
            case ContentControl content when !BindingOperations.IsDataBound(content, ContentControl.ContentProperty):
                Apply(content, ContentControl.ContentProperty); break;
        }

        int children = VisualTreeHelper.GetChildrenCount(element);
        for (int i = 0; i < children; i++)
            LocalizeTree(VisualTreeHelper.GetChild(element, i));
    }

    private static void LocalizeInlines(InlineCollection inlines)
    {
        // Setting Run.Text bumps the inline generation. Snapshot first so the enumerator stays valid.
        var snapshot = new Inline[inlines.Count];
        inlines.CopyTo(snapshot, 0);
        foreach (Inline inline in snapshot)
        {
            if (inline is Run run && !BindingOperations.IsDataBound(run, Run.TextProperty))
                Apply(run, Run.TextProperty);
            else if (inline is Span span)
                LocalizeInlines(span.Inlines);
        }
    }

    private static void Apply(DependencyObject target, DependencyProperty property)
    {
        if (BindingOperations.IsDataBound(target, property) || target.GetValue(property) is not string current || string.IsNullOrWhiteSpace(current))
            return;
        var properties = Originals.GetOrCreateValue(target);
        if (!properties.TryGetValue(property, out string? original))
        {
            original = current;
            properties[property] = original;
        }
        target.SetCurrentValue(property, Translate(original));
    }
}
