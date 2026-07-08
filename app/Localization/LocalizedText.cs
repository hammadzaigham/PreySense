using System.Globalization;
using System.Windows.Forms;

namespace PreySense.Localization
{
    public static class LocalizedText
    {
        public const string SystemLanguageCode = "system";
        public const string EnglishLanguageCode = "en";
        public const string ChineseLanguageCode = "zh-CN";

        private static string _languageCode = SystemLanguageCode;

        private static readonly IReadOnlyDictionary<string, string> Zh = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Apply"] = "应用",
            ["Apply && Restart Now"] = "应用并立即重启",
            ["Apply Custom Fan Curve"] = "应用自定义风扇曲线",
            ["Apply Limits"] = "应用限制",
            ["Apply Overclock"] = "应用超频",
            ["Apply Power Limits"] = "应用功耗限制",
            ["Auto"] = "自动",
            ["Balanced"] = "平衡",
            ["Battery Charge Limit"] = "电池充电上限",
            ["Best performance"] = "最佳性能",
            ["Best power efficiency"] = "最佳能效",
            ["Blue"] = "蓝色",
            ["Brightness"] = "亮度",
            ["Cancel"] = "取消",
            ["Charge: --%"] = "电池电量: --%",
            ["Color Profiles"] = "颜色配置",
            ["Contrast"] = "对比度",
            ["CPU Boost"] = "CPU 加速",
            ["CPU Fan"] = "CPU 风扇",
            ["Direction"] = "方向",
            ["Disabled"] = "禁用",
            ["Disco"] = "闪烁",
            ["Display"] = "显示",
            ["Display Profile"] = "显示配置",
            ["Eco"] = "节能",
            ["Effect"] = "效果",
            ["Enabled"] = "启用",
            ["Endurance"] = "续航",
            ["Factory Defaults"] = "恢复出厂默认",
            ["Fan Speed"] = "风扇转速",
            ["Fans + Power"] = "风扇和功耗",
            ["GPU Fan"] = "GPU 风扇",
            ["GPU Mode"] = "GPU 模式",
            ["GPU Settings"] = "GPU 设置",
            ["Graph"] = "图表",
            ["Green"] = "绿色",
            ["High (36% Reduction)"] = "高（降低 36%）",
            ["Hue"] = "色相",
            ["iGPU on Battery"] = "电池供电时使用 iGPU",
            ["Keyboard"] = "键盘",
            ["Keyboard Lighting"] = "键盘灯光",
            ["Language"] = "语言",
            ["Laptop Screen"] = "笔记本屏幕",
            ["Left"] = "左",
            ["Low (18% Reduction)"] = "低（降低 18%）",
            ["Max Fans"] = "风扇全速",
            ["Memory Speed"] = "显存/内存速度",
            ["Metrics"] = "监控",
            ["Metrics Overlay Settings"] = "监控悬浮窗设置",
            ["None"] = "无",
            ["None / Custom"] = "无 / 自定义",
            ["Off"] = "关闭",
            ["Overlay Settings"] = "悬浮窗设置",
            ["Performance"] = "性能",
            ["Performance Mode"] = "性能模式",
            ["Plugged in"] = "已接通电源",
            ["Power Limits"] = "功耗限制",
            ["Preset"] = "预设",
            ["Profile"] = "配置",
            ["Quit"] = "退出",
            ["Ramp Up"] = "升速延迟",
            ["Red"] = "红色",
            ["Reset"] = "重置",
            ["Right"] = "右",
            ["Run on startup"] = "开机启动",
            ["Saturation"] = "饱和度",
            ["Save Settings"] = "保存设置",
            ["Show FPS"] = "显示 FPS",
            ["Silent"] = "静音",
            ["Simplified Chinese"] = "简体中文",
            ["Snake"] = "流星",
            ["Speed"] = "速度",
            ["Standard"] = "标准",
            ["Static"] = "静态",
            ["Sync"] = "同步",
            ["System default"] = "跟随系统",
            ["Turbo"] = "Turbo",
            ["Ultimate"] = "独显直连",
            ["Wave"] = "波浪",
            ["Windows Power Mode"] = "Windows 电源模式",
            ["Zones"] = "区域",
            ["Zoom"] = "缩放",
            ["30s Timeout"] = "30 秒超时",
            ["Acer Services (Recommended)"] = "Acer 服务（推荐）",
            ["Aggressive"] = "积极",
            ["Aggressive at Guaranteed"] = "保证频率时积极",
            ["All"] = "全部",
            ["Bluelight Shield"] = "蓝光护盾",
            ["Breathe"] = "呼吸",
            ["Breathing"] = "呼吸",
            ["Clock"] = "频率",
            ["Col"] = "列",
            ["Core Offset (MHz)"] = "核心偏移 (MHz)",
            ["Cyberpunk"] = "赛博朋克",
            ["Deep Space"] = "深空",
            ["Download"] = "下载",
            ["Efficient Aggressive"] = "高效积极",
            ["Efficient Aggressive at Guaranteed"] = "保证频率时高效积极",
            ["Efficient Enabled"] = "高效启用",
            ["Gamma"] = "伽马",
            ["Ice Cold"] = "冰冷",
            ["Later"] = "稍后",
            ["Hardware Control Mode"] = "硬件控制模式",
            ["Magma Flow"] = "熔岩流",
            ["Memory Offset (MHz)"] = "显存偏移 (MHz)",
            ["Memory"] = "内存",
            ["Neon"] = "霓虹",
            ["Nordic Aurora"] = "北境极光",
            ["PawnIO Required"] = "需要 PawnIO",
            ["Power"] = "功耗",
            ["Quiet"] = "Quiet",
            ["Rainbow"] = "彩虹",
            ["Ripple"] = "涟漪",
            ["Installing PawnIO"] = "正在安装 PawnIO",
            ["PawnIO Installed"] = "PawnIO 已安装",
            ["Installation Failed"] = "安装失败",
            ["Restart Required"] = "需要重启",
            ["Strobe"] = "频闪",
            ["Sunset Glow"] = "日落辉光",
            ["Temperature"] = "温度",
            ["Usage"] = "占用率",
            ["Volcano"] = "火山",
            ["Voltage"] = "电压",
            ["WMI Interface"] = "WMI 接口",
            ["Yes"] = "是",
            ["No"] = "否",
            ["Prey Sense can communicate with your Acer notebook using WMI calls or the default Acer background services.\n\nAcer Services (Recommended): Maximum compatibility, relies on default Acer background services.\nWMI Interface (Faster): Direct WMI calls. Faster execution but may not function on all notebook models."] =
                "Prey Sense 可以通过 WMI 调用或默认 Acer 后台服务与你的 Acer 笔记本通信。\n\nAcer 服务（推荐）: 兼容性最高，依赖默认 Acer 后台服务。\nWMI 接口（更快）: 直接 WMI 调用，执行更快，但可能无法在所有笔记本型号上工作。",
            ["PawnIO is not installed. It is required to apply CPU power limits.\n\nWould you like to install it now?"] =
                "未安装 PawnIO。应用 CPU 功耗限制需要它。\n\n现在安装吗？",
            ["Downloading the latest PawnIO driver installer."] = "正在下载最新版 PawnIO 驱动安装程序。",
            ["PawnIO has been successfully installed!"] = "PawnIO 已成功安装。",
            ["PawnIO automatic installation did not complete successfully.\n\nWould you like to open the website to download and install it manually?"] =
                "PawnIO 自动安装未成功完成。\n\n是否打开网站手动下载并安装？",
            ["Switching to Ultimate Mode requires a restart.\nBefore restarting, save any open files and close all programs."] =
                "切换到独显直连模式需要重启。\n重启前请保存所有打开的文件并关闭程序。",
            ["Switching out of Ultimate Mode requires a restart.\nBefore restarting, save any open files and close all programs."] =
                "退出独显直连模式需要重启。\n重启前请保存所有打开的文件并关闭程序。",
            ["Reset fan curves, power limits, and Windows power mode to factory defaults?"] =
                "将风扇曲线、功耗限制和 Windows 电源模式恢复为出厂默认值？",
            ["Would you like to open the GitHub repository to download it?"] =
                "是否打开 GitHub 仓库下载？",
        };

        public static bool IsChinese(CultureInfo culture) =>
            culture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase);

        public static string CurrentLanguageCode => _languageCode;

        public static void SetLanguage(string? languageCode) =>
            _languageCode = NormalizeLanguageCode(languageCode);

        public static string NormalizeLanguageCode(string? languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode) ||
                languageCode.Equals(SystemLanguageCode, StringComparison.OrdinalIgnoreCase))
                return SystemLanguageCode;

            if (languageCode.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                return ChineseLanguageCode;

            if (languageCode.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                return EnglishLanguageCode;

            return SystemLanguageCode;
        }

        public static CultureInfo GetEffectiveCulture(CultureInfo? systemCulture = null)
        {
            string languageCode = CurrentLanguageCode;
            return languageCode == SystemLanguageCode
                ? systemCulture ?? CultureInfo.CurrentUICulture
                : new CultureInfo(languageCode);
        }

        public static string Translate(string text) =>
            Translate(text, GetEffectiveCulture());

        public static string Translate(string text, CultureInfo culture)
        {
            if (!IsChinese(culture) || string.IsNullOrWhiteSpace(text))
                return text;

            string key = text.Replace("&&", "&", StringComparison.Ordinal).Replace("&", "", StringComparison.Ordinal);
            return Zh.TryGetValue(key, out var translated) ? translated : text;
        }

        public static void ApplyTo(Control root)
        {
            root.Text = Translate(root.Text);
            if (root is ComboBox combo)
                TranslateComboItems(combo);

            foreach (Control child in root.Controls)
                ApplyTo(child);
        }

        public static void TranslateComboItems(ComboBox combo)
        {
            int selected = combo.SelectedIndex;
            for (int i = 0; i < combo.Items.Count; i++)
            {
                // Non-string items may carry internal keys. Their ToString() handles display.
                if (combo.Items[i] is string text)
                    combo.Items[i] = Translate(text);
            }
            if (selected >= 0 && selected < combo.Items.Count)
                combo.SelectedIndex = selected;
        }

        public static string ColumnLabel(int columnNumber, CultureInfo? culture = null) =>
            IsChinese(culture ?? GetEffectiveCulture())
                ? $"列 {columnNumber}:"
                : $"Col {columnNumber}:";

        public static string BatteryChargeLimit(int value, CultureInfo? culture = null) =>
            IsChinese(culture ?? GetEffectiveCulture())
                ? $"电池充电上限: {value}%"
                : $"Battery Charge Limit: {value}%";

        public static string BatteryChargeUnavailable(CultureInfo? culture = null) =>
            IsChinese(culture ?? GetEffectiveCulture()) ? "电池电量: --%" : "Charge: --%";

        public static string BatteryCharging(double watts, CultureInfo? culture = null) =>
            IsChinese(culture ?? GetEffectiveCulture()) ? $"充电中: {watts:F1}W" : $"Charging: {watts:F1}W";

        public static string BatteryDischarging(double watts, CultureInfo? culture = null) =>
            IsChinese(culture ?? GetEffectiveCulture()) ? $"放电中: {watts:F1}W" : $"Discharging: {watts:F1}W";

        public static string GpuMode(int mode, CultureInfo? culture = null)
        {
            if (!IsChinese(culture ?? GetEffectiveCulture()))
            {
                return mode switch
                {
                    0 => "GPU Mode: iGPU only",
                    1 => "GPU Mode: iGPU + dGPU",
                    2 => "GPU Mode: dGPU exclusive",
                    _ => "GPU Mode"
                };
            }

            return mode switch
            {
                0 => "GPU 模式: 仅 iGPU",
                1 => "GPU 模式: iGPU + dGPU",
                2 => "GPU 模式: 独显直连",
                _ => "GPU 模式"
            };
        }

        public static string LaptopScreen(int currentHz, bool overdrive, CultureInfo? culture = null)
        {
            string suffix = overdrive ? " + Overdrive" : "";
            return IsChinese(culture ?? GetEffectiveCulture())
                ? $"笔记本屏幕: {currentHz}Hz{suffix}"
                : $"Laptop Screen: {currentHz}Hz{suffix}";
        }

        public static string PerformanceMode(string modeName, bool batteryAutoEco = false, CultureInfo? culture = null)
        {
            if (!IsChinese(culture ?? GetEffectiveCulture()))
                return batteryAutoEco ? $"Performance Mode: {modeName} (Battery)" : $"Performance Mode: {modeName}";

            string localizedMode = Translate(modeName, culture ?? GetEffectiveCulture());
            return batteryAutoEco ? $"性能模式: {localizedMode}（电池）" : $"性能模式: {localizedMode}";
        }
    }
}
