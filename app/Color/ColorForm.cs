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
    private int _currentChannel = 0; // 0 = All, 1 = Red, 2 = Green, 3 = Blue
    private DisplayColorProfile _profile = null!;

    public ColorForm(MainForm mainForm, int refreshRate)
    {
        _mainForm = mainForm;
        _refreshRate = refreshRate;

        InitializeComponent();
        InitTheme(true);

        _blueLightCombo.Items.AddRange(new object[] {
            "Off",
            "Low (18% Reduction)",
            "High (36% Reduction)"
        });
        _blueLightCombo.SelectedIndexChanged += (s, e) => { if (!_isLoading) RequestApply(); };

        _btnAll.Click += (s, e) => SwitchChannel(0);
        _btnRed.Click += (s, e) => SwitchChannel(1);
        _btnGreen.Click += (s, e) => SwitchChannel(2);
        _btnBlue.Click += (s, e) => SwitchChannel(3);

        LoadProfile();
    }

    private void SwitchChannel(int channel)
    {
        _currentChannel = channel;
        UpdateChannelButtonOutlines();
        LoadChannelValuesToUi();
    }

    private void UpdateChannelButtonOutlines()
    {
        var outlineColor = Color.FromArgb(128, 128, 128);

        _btnAll.BorderColor = _currentChannel == 0 ? outlineColor : Color.Transparent;
        _btnAll.Activated = _currentChannel == 0;

        _btnRed.BorderColor = _currentChannel == 1 ? outlineColor : Color.Transparent;
        _btnRed.Activated = _currentChannel == 1;

        _btnGreen.BorderColor = _currentChannel == 2 ? outlineColor : Color.Transparent;
        _btnGreen.Activated = _currentChannel == 2;

        _btnBlue.BorderColor = _currentChannel == 3 ? outlineColor : Color.Transparent;
        _btnBlue.Activated = _currentChannel == 3;
    }

    private void LoadChannelValuesToUi()
    {
        _isLoading = true;
        try
        {
            int brightness = _currentChannel switch
            {
                1 => _profile.BrightnessR,
                2 => _profile.BrightnessG,
                3 => _profile.BrightnessB,
                _ => _profile.BrightnessR
            };
            int contrast = _currentChannel switch
            {
                1 => _profile.ContrastR,
                2 => _profile.ContrastG,
                3 => _profile.ContrastB,
                _ => _profile.ContrastR
            };
            double gamma = _currentChannel switch
            {
                1 => _profile.GammaR,
                2 => _profile.GammaG,
                3 => _profile.GammaB,
                _ => _profile.GammaR
            };

            _brightnessSlider.Value = ToUiPercent(brightness);
            _contrastSlider.Value = ToUiPercent(contrast);
            _gammaSlider.Value = ToUiGamma(gamma);

            _brightnessValue.Value = ToUiPercent(brightness);
            _contrastValue.Value = ToUiPercent(contrast);
            _gammaValue.Value = ClampGamma(gamma);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void LoadProfile()
    {
        _isLoading = true;
        try
        {
            _profile = DisplayManager.LoadProfile(_refreshRate);

            _saturationSlider.Value = _profile.Saturation;
            _hueSlider.Value = _profile.Hue;
            _blueLightCombo.SelectedIndex = Math.Clamp(_profile.BlueLight, 0, 2);

            _saturationValue.Value = _profile.Saturation;
            _hueValue.Value = _profile.Hue;
        }
        finally
        {
            _isLoading = false;
        }
        SwitchChannel(_currentChannel);
    }

    private void ResetProfile()
    {
        _profile.BrightnessR = 100;
        _profile.BrightnessG = 100;
        _profile.BrightnessB = 100;
        _profile.ContrastR = 100;
        _profile.ContrastG = 100;
        _profile.ContrastB = 100;
        _profile.GammaR = 1.0;
        _profile.GammaG = 1.0;
        _profile.GammaB = 1.0;
        _profile.Saturation = 50;
        _profile.Hue = 0;
        _profile.BlueLight = 0;

        _saturationSlider.Value = 50;
        _hueSlider.Value = 0;
        _blueLightCombo.SelectedIndex = 0;

        _brightnessValue.Value = 50;
        _contrastValue.Value = 50;
        _gammaValue.Value = 1.00m;
        _saturationValue.Value = 50;
        _hueValue.Value = 0;

        SwitchChannel(_currentChannel);
        RequestApply();
    }

    private void ApplyProfile()
    {
        if (_isLoading) return;
        _pendingApply = false;

        int uiBrightness = _brightnessSlider.Value;
        int uiContrast = _contrastSlider.Value;
        double uiGamma = (double)_gammaValue.Value;

        if (_currentChannel == 0) // All
        {
            _profile.BrightnessR = ToProfilePercent(uiBrightness);
            _profile.BrightnessG = ToProfilePercent(uiBrightness);
            _profile.BrightnessB = ToProfilePercent(uiBrightness);
            _profile.ContrastR = ToProfilePercent(uiContrast);
            _profile.ContrastG = ToProfilePercent(uiContrast);
            _profile.ContrastB = ToProfilePercent(uiContrast);
            _profile.GammaR = uiGamma;
            _profile.GammaG = uiGamma;
            _profile.GammaB = uiGamma;
        }
        else if (_currentChannel == 1) // Red
        {
            _profile.BrightnessR = ToProfilePercent(uiBrightness);
            _profile.ContrastR = ToProfilePercent(uiContrast);
            _profile.GammaR = uiGamma;
        }
        else if (_currentChannel == 2) // Green
        {
            _profile.BrightnessG = ToProfilePercent(uiBrightness);
            _profile.ContrastG = ToProfilePercent(uiContrast);
            _profile.GammaG = uiGamma;
        }
        else if (_currentChannel == 3) // Blue
        {
            _profile.BrightnessB = ToProfilePercent(uiBrightness);
            _profile.ContrastB = ToProfilePercent(uiContrast);
            _profile.GammaB = uiGamma;
        }

        _profile.Saturation = _saturationSlider.Value;
        _profile.Hue = _hueSlider.Value;
        _profile.BlueLight = _blueLightCombo.SelectedIndex;

        DisplayManager.SaveProfile(_refreshRate, _profile);
        DisplayManager.ApplyProfile(_profile);
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
