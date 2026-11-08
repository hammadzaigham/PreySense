using System;
using System.Drawing;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using PreySense.Helpers;

namespace PreySense.Rgb
{
    public enum CustomEffectMode
    {
        None = 0,
        RainbowCycle = 8,
        // Retired. Values stay so saved mode indexes do not shift.
        CpuHeatmap = 9,
        BatteryFuelGauge = 10,
        PoliceStrobe = 11,
        FireEmber = 12,
        MatrixCascade = 13,
        CylonScanner = 14,
        AuroraDrift = 15,
        // Retired. Values stay so saved mode indexes do not shift.
        OceanTide = 16,
        Heartbeat = 17,
        LightningStorm = 18,
        Starlight = 19,
        // Retired. Values stay so saved mode indexes do not shift.
        IceCrystal = 20,
        NeonPulse = 21,
        PlasmaOrb = 22,
        GlitchStorm = 23,
        BioHazard = 24,
        SolarFlare = 25
    }

    /// <summary>
    /// Real-time custom software lighting and interactive effects engine.
    /// Drives dynamic, reactive 4-zone lighting across the physical ENEK5130 keyboard
    /// and GUI visualizers with low latency and zero external service dependencies.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class CustomLightingEffectsEngine : IDisposable
    {
        private readonly WmiController _wmi;
        private CancellationTokenSource? _cts;
        private Task? _animationTask;
        private CustomEffectMode _currentMode = CustomEffectMode.None;
        private byte _brightness = 100;
        private int _speed = 50;
        private Color _activeBrushColor = Color.FromArgb(0, 240, 255); // Default Predator
        private readonly Color[] _zoneColors = new Color[4];
        private readonly Random _rng = new();
        private readonly object _lock = new();
        private bool _isDisposed;
        private int _idleTimeoutSeconds;
        private bool _idleDimmed;
        private readonly PreySense.Core.Services.AsyncChangeSignal _idleWake = new();
        private bool _hardwareTimeoutSuppressed;
        private bool _forceHwDispatch;
        private bool _hasLastFrame;
        private readonly Color[] _lastFrame = new Color[4];
        private static readonly Color[] IdleOffFrame =
        {
            Color.Black, Color.Black, Color.Black, Color.Black
        };

        /// <summary>
        /// Fired every frame (~33 FPS) with the current 4-zone colors for UI visualizer preview.
        /// </summary>
        public event Action<Color[]>? FrameUpdated;

        public CustomEffectMode CurrentMode => _currentMode;
        public bool IsRunning => _currentMode != CustomEffectMode.None && _cts != null && !_cts.IsCancellationRequested;

        public Color ActiveBrushColor
        {
            get => _activeBrushColor;
            set => _activeBrushColor = value;
        }

        public CustomLightingEffectsEngine(WmiController wmi)
        {
            _wmi = wmi;
            for (int i = 0; i < 4; i++)
            {
                _zoneColors[i] = _wmi.ZoneColors[i];
            }
        }

        public void SetParameters(byte brightness, int speed, Color activeBrush)
        {
            lock (_lock)
            {
                _brightness = Math.Clamp(brightness, (byte)0, (byte)100);
                _speed = Math.Clamp(speed, 0, 100);
                if (!activeBrush.IsEmpty)
                {
                    _activeBrushColor = activeBrush;
                }
            }
        }

        public void SetSpeed(byte speed)
        {
            lock (_lock)
            {
                _speed = Math.Clamp((int)speed, 0, 100);
            }
        }

        /// <summary>
        /// Starts a custom dynamic software effect.
        /// </summary>
        public void StartEffect(CustomEffectMode mode, byte brightness, int speed)
        {
            Stop();

            if (mode is CustomEffectMode.None or CustomEffectMode.CpuHeatmap or CustomEffectMode.BatteryFuelGauge
                or CustomEffectMode.OceanTide or CustomEffectMode.IceCrystal)
                return;

            lock (_lock)
            {
                _currentMode = mode;
                _brightness = Math.Clamp(brightness, (byte)0, (byte)100);
                _speed = Math.Clamp(speed, 0, 100);
                _idleDimmed = false;
                _forceHwDispatch = true;
                _hasLastFrame = false;
                _cts = new CancellationTokenSource();
            }

            KeyboardActivity.NoteKeyDown();

            _animationTask = Task.Run(() => AnimationLoopAsync(_cts.Token));
            AppLogger.Log($"CustomLightingEffectsEngine: Started effect {mode} (speed={speed}, brightness={brightness}).");
        }

        /// <summary>
        /// Updates the brightness of the active effect.
        /// </summary>
        public void SetBrightness(byte brightness)
        {
            byte scaled = Math.Clamp(brightness, (byte)0, (byte)100);
            Color[] frame;
            bool writeNow;
            lock (_lock)
            {
                _brightness = scaled;
                _forceHwDispatch = !_idleDimmed;
                writeNow = !_idleDimmed && IsRunning && _hasLastFrame;
                frame = writeNow ? (Color[])_lastFrame.Clone() : Array.Empty<Color>();
            }

            if (writeNow)
                _wmi.SetZoneColors(frame, scaled);
        }

        public bool TryCopyLastFrame(Color[] destination)
        {
            if (destination is not { Length: >= 4 })
                return false;

            lock (_lock)
            {
                if (!_hasLastFrame)
                    return false;
                Array.Copy(_lastFrame, destination, 4);
                return true;
            }
        }

        /// <summary>
        /// Seconds without a keypress before the software effect turns the backlight off.
        /// Mouse movement and clicks do not count. The EC timeout stays at 0 while an effect is running so animation frames cannot reset it.
        /// </summary>
        public void SetIdleTimeoutSeconds(int seconds)
        {
            lock (_lock)
            {
                if (_idleTimeoutSeconds == Math.Max(0, seconds)) return;
                _idleTimeoutSeconds = Math.Max(0, seconds);
                if (_idleTimeoutSeconds == 0)
                    _idleDimmed = false;
            }

            _idleWake.Pulse();

            if (IsRunning && seconds > 0)
                SuppressHardwareTimeout();
        }

        /// <summary>
        /// Stops any currently active software lighting effect.
        /// </summary>
        public void Stop()
        {
            lock (_lock)
            {
                _currentMode = CustomEffectMode.None;
                if (_cts != null)
                {
                    try
                    {
                        _cts.Cancel();
                        _cts.Dispose();
                    }
                    catch { }
                    _cts = null;
                }
            }

            _animationTask = null;
            RestoreHardwareTimeout();
        }

        private void WakeIdle() => _idleWake.Pulse();

        private void SuppressHardwareTimeout()
        {
            bool suppress;
            lock (_lock)
            {
                suppress = _currentMode != CustomEffectMode.None
                    && _idleTimeoutSeconds > 0
                    && !_hardwareTimeoutSuppressed;
                if (suppress)
                    _hardwareTimeoutSuppressed = true;
            }

            if (suppress)
                _wmi.SetLedTimeoutSeconds(0);
        }

        private void RestoreHardwareTimeout()
        {
            int seconds;
            bool restore;
            lock (_lock)
            {
                restore = _hardwareTimeoutSuppressed;
                _hardwareTimeoutSuppressed = false;
                seconds = _idleTimeoutSeconds;
            }

            if (restore)
                _wmi.SetLedTimeoutSeconds(seconds);
        }

        private async Task AnimationLoopAsync(CancellationToken ct)
        {
            double step = 0;
            int frameCount = 0;
            long lastHwDispatch = 0;
            Color[] frame = new Color[4];

            while (!ct.IsCancellationRequested && !_isDisposed)
            {
                try
                {
                    // Software-effect loop only (hardware DYN modes do not use this engine).
                    const int frameIntervalMs = 28; // ~35 FPS for preview + zone color generation
                    double speedStep = 0.03 + (_speed / 100.0) * 0.32;
                    step += speedStep;
                    frameCount++;

                    int hwThrottleMs = Math.Max(70, 180 - (int)(_speed * 1.1));

                    switch (_currentMode)
                    {
                        case CustomEffectMode.RainbowCycle:
                            GenerateRainbowCycleFrame(frame, step);
                            break;

                        case CustomEffectMode.PoliceStrobe:
                            GeneratePoliceStrobeFrame(frame, frameCount);
                            hwThrottleMs = Math.Max(80, 220 - (int)(_speed * 1.4));
                            break;

                        case CustomEffectMode.FireEmber:
                            GenerateFireEmberFrame(frame, step);
                            break;

                        case CustomEffectMode.MatrixCascade:
                            GenerateMatrixCascadeFrame(frame, frameCount);
                            break;

                        case CustomEffectMode.CylonScanner:
                            GenerateCylonScannerFrame(frame, frameCount);
                            break;

                        case CustomEffectMode.AuroraDrift:
                            GenerateAuroraDriftFrame(frame, step);
                            break;

                        case CustomEffectMode.Heartbeat:
                            GenerateHeartbeatFrame(frame, step);
                            break;

                        case CustomEffectMode.LightningStorm:
                            GenerateLightningStormFrame(frame, frameCount);
                            hwThrottleMs = Math.Max(50, 140 - (int)(_speed * 0.8));
                            break;

                        case CustomEffectMode.Starlight:
                            GenerateStarlightFrame(frame, step);
                            break;

                        case CustomEffectMode.NeonPulse:
                            GenerateNeonPulseFrame(frame, step);
                            break;

                        case CustomEffectMode.PlasmaOrb:
                            GeneratePlasmaOrbFrame(frame, step);
                            break;

                        case CustomEffectMode.GlitchStorm:
                            GenerateGlitchStormFrame(frame, frameCount);
                            hwThrottleMs = Math.Max(40, 120 - (int)(_speed * 0.8));
                            break;

                        case CustomEffectMode.BioHazard:
                            GenerateBioHazardFrame(frame, step);
                            break;

                        case CustomEffectMode.SolarFlare:
                            GenerateSolarFlareFrame(frame, frameCount);
                            hwThrottleMs = Math.Max(55, 160 - (int)(_speed * 1.0));
                            break;

                        default:
                            for (int i = 0; i < 4; i++) frame[i] = _zoneColors[i];
                            break;
                    }

                    int timeoutSeconds;
                    lock (_lock)
                    {
                        timeoutSeconds = _idleTimeoutSeconds;
                    }

                    if (timeoutSeconds > 0)
                    {
                        SuppressHardwareTimeout();
                        uint lastInputTick = KeyboardActivity.LastKeyTick;
                        if (GetIdleSeconds(lastInputTick) >= timeoutSeconds)
                        {
                            lock (_lock)
                            {
                                _idleDimmed = true;
                            }

                            _wmi.SetZoneColors(IdleOffFrame, 0);
                            FrameUpdated?.Invoke(IdleOffFrame);
                            AppLogger.Log($"CustomLightingEffectsEngine: backlight off after {timeoutSeconds}s idle. Animation stopped.");

                            KeyboardActivity.KeyDown += WakeIdle;
                            try
                            {
                                while (!ct.IsCancellationRequested && !_isDisposed)
                                {
                                    // Observe first, then inspect state: a key or settings change
                                    // between the check and await cannot be lost.
                                    Task changed = _idleWake.WaitAsync(ct);
                                    int stillTimeout;
                                    lock (_lock) { stillTimeout = _idleTimeoutSeconds; }
                                    bool keyPressed = KeyboardActivity.LastKeyTick != lastInputTick;
                                    if (stillTimeout <= 0 || keyPressed)
                                    {
                                        lock (_lock) { _idleDimmed = false; }
                                        lastHwDispatch = 0;
                                        if (keyPressed)
                                            AppLogger.Log("CustomLightingEffectsEngine: backlight restored after input.");
                                        break;
                                    }
                                    await changed.ConfigureAwait(false);
                                }
                            }
                            finally { KeyboardActivity.KeyDown -= WakeIdle; }
                            continue;
                        }
                    }

                    // 1. UI / cover-sync preview only when someone is listening
                    FrameUpdated?.Invoke(frame);

                    byte brightness;
                    bool forceHw;
                    lock (_lock)
                    {
                        brightness = _brightness;
                        forceHw = _forceHwDispatch;
                        _forceHwDispatch = false;
                        Array.Copy(frame, _lastFrame, 4);
                        _hasLastFrame = true;
                    }

                    // 2. Throttled dispatch to physical keyboard via direct ENEK5130 HID / WMI
                    long now = Stopwatch.GetTimestamp();
                    if (forceHw || lastHwDispatch == 0 || Stopwatch.GetElapsedTime(lastHwDispatch, now).TotalMilliseconds >= hwThrottleMs)
                    {
                        lastHwDispatch = now;
                        _wmi.SetZoneColors(frame, brightness);
                    }

                    await Task.Delay(frameIntervalMs, ct);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"CustomLightingEffectsEngine loop error: {ex.Message}");
                    await Task.Delay(100, ct);
                }
            }
        }

        #region Effect Generators

        private void GenerateRainbowCycleFrame(Color[] frame, double step)
        {
            for (int i = 0; i < 4; i++)
            {
                float hue = (float)((step * 48.0 + i * 90.0) % 360.0);
                frame[i] = HsvToRgb(hue, 1f, 1f);
            }
        }

        private void GeneratePoliceStrobeFrame(Color[] frame, int frameCount)
        {
            bool isBluePhase = ((frameCount / 4) % 2) == 0;
            Color blue = Color.FromArgb(0, 85, 255);
            Color red = Color.FromArgb(255, 0, 34);
            Color off = Color.FromArgb(5, 5, 10);

            frame[0] = isBluePhase ? blue : off;
            frame[1] = isBluePhase ? blue : off;
            frame[2] = isBluePhase ? off : red;
            frame[3] = isBluePhase ? off : red;
        }

        private void GenerateFireEmberFrame(Color[] frame, double step)
        {
            double f1 = Math.Sin(step * 1.8) * 0.15 + Math.Sin(step * 3.7) * 0.1;
            double f2 = Math.Sin(step * 1.8 + 1.2) * 0.18 + Math.Sin(step * 4.1 + 0.5) * 0.12;
            double f3 = Math.Sin(step * 1.8 + 2.4) * 0.16 + Math.Sin(step * 3.3 + 1.1) * 0.11;
            double f4 = Math.Sin(step * 1.8 + 3.6) * 0.19 + Math.Sin(step * 4.5 + 2.0) * 0.13;

            frame[0] = EmberColor(0.85 + f1);
            frame[1] = EmberColor(0.95 + f2);
            frame[2] = EmberColor(0.90 + f3);
            frame[3] = EmberColor(0.80 + f4);
        }

        private void GenerateMatrixCascadeFrame(Color[] frame, int frameCount)
        {
            int headPos = (frameCount / 3) % 6;
            for (int i = 0; i < 4; i++)
            {
                if (i == headPos)
                    frame[i] = Color.FromArgb(57, 255, 20);      // Bright electric green head
                else if (i == headPos - 1)
                    frame[i] = Color.FromArgb(0, 150, 25);      // Trailing phosphor
                else if (i == headPos - 2)
                    frame[i] = Color.FromArgb(0, 55, 12);       // Fading trail
                else
                    frame[i] = Color.FromArgb(0, 16, 4);        // Ambient matrix dark
            }
        }

        private void GenerateCylonScannerFrame(Color[] frame, int frameCount)
        {
            int[] seq = { 0, 1, 2, 3, 2, 1 };
            int active = seq[(frameCount / 3) % seq.Length];
            Color beam = _activeBrushColor.IsEmpty ? Color.FromArgb(255, 0, 50) : _activeBrushColor;
            Color dim = Color.FromArgb(12, 0, 6);

            for (int i = 0; i < 4; i++)
            {
                frame[i] = (i == active) ? beam : dim;
            }
        }

        private void GenerateAuroraDriftFrame(Color[] frame, double step)
        {
            double a1 = (Math.Sin(step * 0.45) + 1.0) / 2.0;
            double a2 = (Math.Sin(step * 0.45 + 0.85) + 1.0) / 2.0;
            double a3 = (Math.Sin(step * 0.45 + 1.70) + 1.0) / 2.0;
            double a4 = (Math.Sin(step * 0.45 + 2.55) + 1.0) / 2.0;

            Color teal = Color.FromArgb(0, 245, 212);
            Color violet = Color.FromArgb(123, 44, 191);

            frame[0] = LerpColor(teal, violet, (float)a1);
            frame[1] = LerpColor(teal, violet, (float)a2);
            frame[2] = LerpColor(teal, violet, (float)a3);
            frame[3] = LerpColor(teal, violet, (float)a4);
        }

        private void GenerateHeartbeatFrame(Color[] frame, double step)
        {
            Color c = _activeBrushColor.IsEmpty ? Color.FromArgb(255, 24, 64) : _activeBrushColor;
            double t = (step * 0.42) % 1.0;
            double pulse;
            if (t < 0.10) pulse = t / 0.10;
            else if (t < 0.20) pulse = 1.0 - ((t - 0.10) / 0.10);
            else if (t < 0.30) pulse = ((t - 0.20) / 0.10) * 0.62;
            else if (t < 0.40) pulse = 0.62 - ((t - 0.30) / 0.10) * 0.62;
            else pulse = 0.08;

            Color lit = ScaleColor(c, (float)Math.Clamp(pulse, 0.08, 1.0));
            for (int i = 0; i < 4; i++)
                frame[i] = lit;
        }

        private void GenerateLightningStormFrame(Color[] frame, int frameCount)
        {
            Color sky = Color.FromArgb(8, 10, 28);
            for (int i = 0; i < 4; i++)
                frame[i] = sky;

            int gap = Math.Max(5, 22 - _speed / 6);
            bool strike = (frameCount % gap) == 0 || _rng.NextDouble() < 0.06;
            if (!strike)
                return;

            int z = _rng.Next(4);
            Color bolt = _rng.NextDouble() < 0.45
                ? Color.FromArgb(235, 245, 255)
                : Color.FromArgb(110, 170, 255);
            frame[z] = bolt;
            if (z > 0) frame[z - 1] = ScaleColor(bolt, 0.32f);
            if (z < 3) frame[z + 1] = ScaleColor(bolt, 0.32f);
        }

        private void GenerateStarlightFrame(Color[] frame, double step)
        {
            Color night = Color.FromArgb(6, 8, 20);
            Color star = Color.FromArgb(210, 225, 255);
            for (int i = 0; i < 4; i++)
            {
                double twinkle = (Math.Sin(step * 1.35 + i * 1.73) + 1.0) / 2.0;
                twinkle = Math.Pow(twinkle, 5.0);
                frame[i] = LerpColor(night, star, (float)twinkle);
            }
        }

        /// <summary>
        /// NeonPulse – User brush color breathing with a sharp attack/slow decay giving a neon sign flicker character.
        /// All zones throb in sync. At the peak a bright near-white flash fires before decaying slowly.
        /// Uses <paramref name="step"/> (continuous time).
        /// </summary>
        private void GenerateNeonPulseFrame(Color[] frame, double step)
        {
            Color base_ = _activeBrushColor.IsEmpty ? Color.FromArgb(255, 0, 255) : _activeBrushColor;

            // Normalise step to [0,1) cycle. Speed influences cycle length via speedStep accumulation.
            double t = (step * 0.28) % 1.0;

            // Sharp attack (0→peak in 8% of cycle) then slow exponential decay back to dim.
            double pulse;
            if (t < 0.08)
            {
                // Fast linear ramp up
                pulse = t / 0.08;
            }
            else
            {
                // Slow exponential decay
                double decay = (t - 0.08) / 0.92;
                pulse = Math.Exp(-decay * 3.5);
            }
            pulse = Math.Clamp(pulse, 0.04, 1.0);

            // At peak, flash toward white (neon over-drive)
            Color lit;
            if (pulse > 0.88)
            {
                float blend = (float)((pulse - 0.88) / 0.12); // 0→1 as pulse→1
                Color overdriven = LerpColor(base_, Color.White, blend * 0.65f);
                lit = ScaleColor(overdriven, (float)pulse);
            }
            else
            {
                lit = ScaleColor(base_, (float)pulse);
            }

            for (int i = 0; i < 4; i++)
                frame[i] = lit;
        }

        /// <summary>
        /// PlasmaOrb – Swirling hue-rotation plasma across zones. Each zone has a phase-offset sine wave on
        /// the HSV hue that oscillates back-and-forth rather than cycling continuously (unlike RainbowCycle).
        /// Uses <paramref name="step"/> (continuous time).
        /// </summary>
        private void GeneratePlasmaOrbFrame(Color[] frame, double step)
        {
            // Hue oscillates ±120° around a rotating center so it feels "swirling" not just cycling.
            double centerHue = (step * 22.0) % 360.0;
            for (int i = 0; i < 4; i++)
            {
                double phase = i * (Math.PI / 2.0); // 90° apart per zone
                double swing = Math.Sin(step * 0.6 + phase) * 110.0; // ±110° hue swing
                float hue = (float)((centerHue + swing + 360.0) % 360.0);
                // Saturation pulses slightly for extra life
                float sat = (float)(0.80 + Math.Sin(step * 1.1 + phase) * 0.20);
                frame[i] = HsvToRgb(hue, Math.Clamp(sat, 0.6f, 1f), 1f);
            }
        }

        /// <summary>
        /// GlitchStorm – Cyberpunk RGB corruption. Normally all zones are very dark. Randomly triggers
        /// glitch bursts on 1–3 zones simultaneously with random saturated colors and very short duration.
        /// Uses <paramref name="frameCount"/> and <c>_rng</c>.
        /// </summary>
        private void GenerateGlitchStormFrame(Color[] frame, int frameCount)
        {
            Color dark = Color.FromArgb(4, 4, 8);
            for (int i = 0; i < 4; i++)
                frame[i] = dark;

            // Base glitch probability scales with speed.
            double glitchChance = 0.04 + (_speed / 100.0) * 0.22;

            // Periodic forced burst so it never feels completely dead.
            int burstPeriod = Math.Max(8, 35 - (int)(_speed * 0.27));
            bool forcedBurst = (frameCount % burstPeriod) == 0;

            if (!forcedBurst && _rng.NextDouble() > glitchChance)
                return;

            // Determine how many zones to glitch simultaneously (1–3).
            int glitchCount = forcedBurst ? _rng.Next(2, 4) : _rng.Next(1, 3);
            for (int g = 0; g < glitchCount; g++)
            {
                int z = _rng.Next(4);
                // Pick a fully saturated random hue.
                float hue = (float)(_rng.NextDouble() * 360.0);
                // Occasionally spike to near-white for a "data corruption" flash.
                Color glitchColor = _rng.NextDouble() < 0.18
                    ? Color.FromArgb(240, 240, 255)
                    : HsvToRgb(hue, 1f, 1f);
                frame[z] = glitchColor;
            }
        }

        /// <summary>
        /// BioHazard – Toxic green/yellow ripple. A bright lime/yellow-green pulse ripples
        /// through zones 0→1→2→3→2→1 (ping-pong) with a trailing glow. Uses smooth lerp.
        /// Uses <paramref name="step"/> (continuous time).
        /// </summary>
        private void GenerateBioHazardFrame(Color[] frame, double step)
        {
            Color ambient = Color.FromArgb(5, 28, 2);
            Color midGlow = Color.FromArgb(60, 180, 0);
            Color peak = Color.FromArgb(180, 255, 0); // Bright yellow-green

            // Ping-pong: position in [0, 6) maps zone 0→1→2→3→2→1 (length 6 steps)
            double pos = (step * 0.55) % 6.0;

            for (int i = 0; i < 4; i++)
            {
                // Mirror index for ping-pong
                double mirrorPos = pos <= 3.0 ? pos : 6.0 - pos;
                double dist = Math.Abs(mirrorPos - i);
                double t;
                if (dist < 0.5)
                    t = 1.0 - dist * 1.5; // Peak bright
                else if (dist < 1.5)
                    t = Math.Max(0.0, 0.55 - (dist - 0.5) * 0.55); // Trailing glow
                else
                    t = 0.0;

                // Blend: low → midGlow → peak
                Color zoneColor;
                if (t > 0.6)
                    zoneColor = LerpColor(midGlow, peak, (float)((t - 0.6) / 0.4));
                else if (t > 0.0)
                    zoneColor = LerpColor(ambient, midGlow, (float)(t / 0.6));
                else
                    zoneColor = ambient;

                frame[i] = zoneColor;
            }
        }

        /// <summary>
        /// SolarFlare – Coronal heat simulation. Base is deep red/orange. Bright flares of yellow-white
        /// randomly erupt on individual zones and quickly decay. Similar to LightningStorm but warm.
        /// Uses <paramref name="frameCount"/> and <c>_rng</c>.
        /// </summary>
        private void GenerateSolarFlareFrame(Color[] frame, int frameCount)
        {
            // Warm corona base — different brightness per zone for depth.
            Color[] coronaBase =
            {
                Color.FromArgb(100, 20, 0),
                Color.FromArgb(130, 35, 0),
                Color.FromArgb(110, 25, 0),
                Color.FromArgb(90,  18, 0),
            };

            for (int i = 0; i < 4; i++)
                frame[i] = coronaBase[i];

            // Eruption chance scales with speed.
            double eruptChance = 0.05 + (_speed / 100.0) * 0.18;
            int period = Math.Max(6, 28 - (int)(_speed * 0.22));
            bool periodic = (frameCount % period) == 0;

            if (!periodic && _rng.NextDouble() > eruptChance)
                return;

            // Choose eruption zone.
            int z = _rng.Next(4);

            // Pick eruption color: yellow-white to orange-white spectrum.
            double flareRoll = _rng.NextDouble();
            Color flare;
            if (flareRoll < 0.35)
                flare = Color.FromArgb(255, 255, 220); // Near-white solar peak
            else if (flareRoll < 0.65)
                flare = Color.FromArgb(255, 230, 80);  // Bright yellow
            else
                flare = Color.FromArgb(255, 160, 20);  // Orange-yellow

            frame[z] = flare;
            // Warm halo on adjacent zones (slower decay than lightning).
            if (z > 0) frame[z - 1] = LerpColor(coronaBase[z - 1], flare, 0.45f);
            if (z < 3) frame[z + 1] = LerpColor(coronaBase[z + 1], flare, 0.45f);
        }

        #endregion

        #region Color Utilities

        private static Color ScaleColor(Color c, float factor)
        {
            factor = Math.Clamp(factor, 0f, 1f);
            return Color.FromArgb(
                (int)Math.Round(c.R * factor),
                (int)Math.Round(c.G * factor),
                (int)Math.Round(c.B * factor)
            );
        }

        private static Color LerpColor(Color a, Color b, float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return Color.FromArgb(
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t)
            );
        }

        private static Color EmberColor(double intensity)
        {
            intensity = Math.Clamp(intensity, 0.0, 1.2);
            int r = (int)Math.Min(255, 255 * intensity);
            int g = (int)Math.Min(255, (55 + (intensity * 120)) * 0.75);
            int b = (int)Math.Min(255, (intensity * 18));
            return Color.FromArgb(r, g, b);
        }

        private static Color HsvToRgb(float hue, float saturation, float value)
        {
            hue = ((hue % 360f) + 360f) % 360f;
            saturation = Math.Clamp(saturation, 0f, 1f);
            value = Math.Clamp(value, 0f, 1f);

            float c = value * saturation;
            float x = c * (1f - Math.Abs((hue / 60f) % 2f - 1f));
            float m = value - c;
            float r, g, b;
            if (hue < 60f) { r = c; g = x; b = 0f; }
            else if (hue < 120f) { r = x; g = c; b = 0f; }
            else if (hue < 180f) { r = 0f; g = c; b = x; }
            else if (hue < 240f) { r = 0f; g = x; b = c; }
            else if (hue < 300f) { r = x; g = 0f; b = c; }
            else { r = c; g = 0f; b = x; }

            return Color.FromArgb(
                (int)Math.Round((r + m) * 255f),
                (int)Math.Round((g + m) * 255f),
                (int)Math.Round((b + m) * 255f));
        }

        #endregion

        private static int GetIdleSeconds(uint lastInputTick)
        {
            if (lastInputTick == 0)
                return 0;

            uint idleMs = unchecked((uint)Environment.TickCount - lastInputTick);
            return (int)(idleMs / 1000);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            Stop();
        }
    }
}
