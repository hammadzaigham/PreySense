using System.Drawing;
using System.Runtime.Versioning;
using PreySense.Helpers;
using PreySense.UI;

namespace PreySense.Display;

[SupportedOSPlatform("windows")]
public partial class ColorForm : RForm
{
    private readonly MainForm _mainForm;
    private readonly int _refreshRate;
    private bool _isLoading;

    public ColorForm(MainForm mainForm, int refreshRate)
    {
        _mainForm = mainForm;
        _refreshRate = refreshRate;

        InitializeComponent();
        InitTheme(true);
        _blueLightCheck.CheckedChanged += (s, e) => { if (!_isLoading) RequestApply(); };
        LoadProfile();
    }

    private void LoadProfile()
    {
        _isLoading = true;
        try
        {
            var profile = DisplayManager.LoadProfile(_refreshRate);

            _brightnessSlider.Value = ToUiPercent(profile.BrightnessR);
            _contrastSlider.Value = ToUiPercent(profile.ContrastR);
            _gammaSlider.Value = ToUiGamma(profile.GammaR);
            _saturationSlider.Value = profile.Saturation;
            _hueSlider.Value = profile.Hue;
            _blueLightCheck.Checked = profile.BlueLight;

            _brightnessValue.Value = ToUiPercent(profile.BrightnessR);
            _contrastValue.Value = ToUiPercent(profile.ContrastR);
            _gammaValue.Value = ClampGamma(profile.GammaR);
            _saturationValue.Value = profile.Saturation;
            _hueValue.Value = profile.Hue;
        }
        finally
        {
            _isLoading = false;
            RequestApply();
        }
    }

    private void ResetProfile()
    {
        _brightnessSlider.Value = 50;
        _contrastSlider.Value = 50;
        _gammaSlider.Value = 100;
        _saturationSlider.Value = 50;
        _hueSlider.Value = 0;
        _blueLightCheck.Checked = false;

        _brightnessValue.Value = 50;
        _contrastValue.Value = 50;
        _gammaValue.Value = 1.00m;
        _saturationValue.Value = 50;
        _hueValue.Value = 0;
        RequestApply();
    }

    private void ApplyProfile()
    {
        if (_isLoading) return;
        _pendingApply = false;

        var profile = new DisplayColorProfile
        {
            BrightnessR = ToProfilePercent(_brightnessSlider.Value),
            BrightnessG = ToProfilePercent(_brightnessSlider.Value),
            BrightnessB = ToProfilePercent(_brightnessSlider.Value),
            ContrastR = ToProfilePercent(_contrastSlider.Value),
            ContrastG = ToProfilePercent(_contrastSlider.Value),
            ContrastB = ToProfilePercent(_contrastSlider.Value),
            GammaR = (double)_gammaValue.Value,
            GammaG = (double)_gammaValue.Value,
            GammaB = (double)_gammaValue.Value,
            Saturation = _saturationSlider.Value,
            Hue = _hueSlider.Value,
            BlueLight = _blueLightCheck.Checked
        };

        DisplayManager.SaveProfile(_refreshRate, profile);
        DisplayManager.ApplyProfile(profile);
    }

    private void SliderChanged(object? sender, EventArgs e)
    {
        if (_isLoading) return;

        _isLoading = true;
        try
        {
            SyncNumericFromSliders();
        }
        finally
        {
            _isLoading = false;
        }

        RequestApply();
    }

    private void NumericChanged(object? sender, EventArgs e)
    {
        if (_isLoading) return;

        _isLoading = true;
        try
        {
            SyncSlidersFromNumeric();
        }
        finally
        {
            _isLoading = false;
        }

        RequestApply();
    }
    private void ResetClicked(object? sender, EventArgs e) => ResetProfile();

    private void ApplyTimerTick(object? sender, EventArgs e)
    {
        if (_pendingApply && !_isLoading)
            ApplyProfile();
    }

    private void RequestApply()
    {
        if (_isLoading) return;
        _pendingApply = true;
    }

    private static int ToUiPercent(int profileValue) => Math.Clamp(profileValue / 2, 0, 100);

    private static int ToProfilePercent(int uiValue) => Math.Clamp(uiValue * 2, 0, 200);

    private static int ToUiGamma(double profileGamma) => (int)Math.Clamp(Math.Round(profileGamma * 100.0), 0, 500);

    private static decimal ClampGamma(double profileGamma) => Math.Clamp((decimal)profileGamma, 0.00m, 5.00m);

    private void SyncNumericFromSliders()
    {
        _brightnessValue.Value = _brightnessSlider.Value;
        _contrastValue.Value = _contrastSlider.Value;
        _gammaValue.Value = _gammaSlider.Value / 100m;
        _saturationValue.Value = _saturationSlider.Value;
        _hueValue.Value = _hueSlider.Value;
    }

    private void SyncSlidersFromNumeric()
    {
        _brightnessSlider.Value = (int)_brightnessValue.Value;
        _contrastSlider.Value = (int)_contrastValue.Value;
        _gammaSlider.Value = (int)Math.Round(_gammaValue.Value * 100m);
        _saturationSlider.Value = (int)_saturationValue.Value;
        _hueSlider.Value = (int)_hueValue.Value;
    }
}
