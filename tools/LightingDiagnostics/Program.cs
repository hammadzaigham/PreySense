using System.Text.Json;
using PreySense.Rgb;

// Read-only discovery: no ApplyColors, ApplyMode, or lighting service startup.
using var controller = new OemLightingController();
var targets = AcerHidLightingCapabilities.GetSupportedTargets(refresh: true).Select(t => $"0x{t:X2}").ToArray();
var devices = controller.Discover();
Console.WriteLine(JsonSerializer.Serialize(new { HidTargets = targets, Devices = devices }, new JsonSerializerOptions { WriteIndented = true }));
