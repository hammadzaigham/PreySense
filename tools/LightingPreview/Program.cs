using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PreySense.Frontend;
using PreySense.Frontend.Views;
using PreySense.Rgb;
using PreySense.Frontend.ViewModels;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var app = new App();
        app.InitializeComponent(); // Load resources only. Never Run or start hardware services.
        var window = new KeyboardWindow { DataContext = new PreviewModel(), Width = 1040, Height = 1000, SizeToContent = SizeToContent.Manual };
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(1040, 1000));
        content.Arrange(new Rect(0, 0, 1040, 1000));
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1040, 1000, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        string output = Path.GetFullPath(args.Length > 0 ? args[0] : "lighting-preview.png");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using var file = File.Create(output);
        encoder.Save(file);
        Console.WriteLine(output);
    }
}

public sealed class PreviewModel
{
    public bool HasCoverLighting => false;
    public bool ShowLegacyLightingControls => false;
    public bool IsKeyboardTabSelected => false;
    public bool IsCoverTabSelected => false;
    public bool HasLightingDevices => true;
    public string LightingDeviceStatus => "3 lighting devices detected.";
    public IReadOnlyList<LightingDeviceInfo> LightingDevices { get; }
    public LightingDeviceInfo SelectedLightingDevice { get; set; }
    public IReadOnlyList<LightingModeInfo> LightingDeviceModes => SelectedLightingDevice.Modes;
    public LightingModeInfo SelectedLightingDeviceMode { get; set; }
    public string DeviceLightingColorHex { get; set; } = "#00E5FF";
    public int DeviceLightingBrightness { get; set; } = 75;
    public int DeviceLightingSpeed { get; set; } = 50;
    public bool DeviceLightingIndividualColors { get; set; } = true;
    public bool IsDirectLightingDeviceMode => true;
    public bool ShowIndividualLightingEditors => true;
    public bool ShowKeyboardLayout => true;
    public bool ShowIndividualLedList => false;
    public bool HasDeviceLightingSpeed => false;
    public bool HasDeviceLightingRandomColors => false;
    public bool HasSelectedLightingKey => true;
    public IReadOnlyList<LightingLedEditor> LightingLedEditors { get; }
    public LightingLedEditor? SelectedLightingKey { get; set; }
    public IList<LightingLedEditor> SelectedLightingKeys { get; }
    public PreviewModel()
    {
        var keys = OemUsbLightingProtocol.GetKeys(OemUsbKeyboardFamily.Acer, 0x0117);
        LightingDevices = [new("preview", "Acer RGB keyboard", "Keyboard", keys.Select(k => new LightingLedInfo(k.Index, k.Name)).ToArray(),
            [new(-1, "Custom colors"), new(0, "Static"), new(3, "Wave", true)], "Named keys • individual colors and hardware effects"),
            new("preview-logo", "Cover Logo", "Logo", [new(0, "LED 1")], [new(0, "Static")], "Cover lighting"),
            new("preview-key", "Mode Key", "Hotkey", [new(0, "LED 1")],
                [new(-2, "Mode"), new(0, "Static"), new(4, "Breathing"), new(5, "Neon")], "Mode-key lighting")];
        SelectedLightingDevice = LightingDevices[0];
        SelectedLightingDeviceMode = LightingDeviceModes[0];
        LightingLedEditors = keys.Select((k, i) => new LightingLedEditor(new(k.Index, k.Name), i % 2 == 0 ? "#00E5FF" : "#FF4081")).ToArray();
        SelectedLightingKey = LightingLedEditors.Single(k => k.Name == "A");
        SelectedLightingKeys = LightingLedEditors.Where(k => k.Name is "W" or "A" or "S" or "D").ToList();
    }
}
