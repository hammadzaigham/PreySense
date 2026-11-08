# Custom fans

The Fans button in the main-window footer opens the themed fan editor. Eco,
Quiet, Balanced, Performance and Turbo each save one independent choice: Auto,
Max or Custom. Auto uses firmware policy; Max directly selects Acer's hardware
Max mode; Custom uses percentage curves and Fan Control Delay. Max is not a
custom-curve setting. The curve editor appears only for Custom, and switching
to Auto or Max preserves its saved points. New modes default to Auto.

Save persists drafts; Apply also activates the selected performance mode. Fan
settings follow performance-mode change events. Custom control uses the shared
one-second temperature loop. Opening Fans again activates the existing window.
The old global FanMode preference no longer controls the runtime. Legacy
profile switches migrate to the new selection, respecting a saved global Auto
override; an explicit new per-mode selection takes priority over legacy keys.

Curve points are spaced 5°C apart from 40°C to 100°C. Output duty can be any whole
percentage from 0 to 100; it is not quantized into 5% steps. The controller sends
new targets when a temperature band changes and the output actually changes.
Points cannot be lower than the previous point or higher than the next point.
Clicking a point selects it without changing duty; empty graph clicks do nothing.
Dragging starts only after vertical movement exceeds the system drag threshold,
then locks the selected temperature and adjusts duty relative to its starting
value without a click-position jump. Sideways movement does not edit. Escape
cancels a drag and restores the original point. Arrow keys select neighboring
points or adjust the selected duty by 1% within its neighbors' bounds.
Loading old curves raises any descending points to preserve nondecreasing speed.
Both fan curves use the hotter of the CPU and GPU temperatures when both are
available. If the GPU is asleep, both use CPU temperature alone. Each fan retains
its own percentage targets. The editor draws curve segments and editable points
over a static grid with axis labels. Live horizontal/vertical markers remain
removed. Opening the editor requests no telemetry.

Cooling uses a 5°C hysteresis to prevent chatter at a boundary. Fan Control Delay
waits 0–5 seconds before accepting either a higher or lower target (default 2
seconds). At zero, changes apply on the next one-second evaluation without an
additional time delay. Old separate cooldown values are ignored; the existing
ramp-up setting now controls both directions and values above 5 are clamped.
Further changes in the same direction do not restart the clock; the
latest demand is used when it expires. Returning to the applied demand cancels
the pending change. These are delays, not gradual percentage ramps.
Custom mode follows the saved curve and response delay at all temperatures.
There is no separate immediate or emergency cooling override.

The main window requests no telemetry. The visible overlay requests only its
selected metrics. Custom mode reads only temperatures to follow the curve,
regardless of window visibility; Auto and Max do not request sensors.
Custom control never requests fan RPM or other metric reads. Before accessing
NVIDIA temperature APIs, the sampler checks Windows-maintained device power
data. It skips sleeping or unknown-power-state adapters without constructing
the GPU client.
Mode-change events and explicit profile application select the running profile;
fan evaluation does not query the firmware power mode on every sample.
Missing CPU temperature skips the update and preserves the last confirmed command.
Missing GPU temperature uses CPU temperature alone.
A rejected fan write pauses automatic writes until the profile is explicitly
applied again or the mode changes; it does not switch to Auto or retry every five
seconds. Exiting the app still releases software fan control to firmware Auto.
There is no periodic fan-PWM keepalive.

The editor uses percentages only. Estimated RPM editing and its scaling logic
have been removed. Actual RPM telemetry elsewhere in the app is unchanged.

## Audio latency considerations

The v1.4.0 WMI fan writer resends identical Custom targets after its five-second
suppression period, calling SetFanBehavior again and then writing both fan
speeds. The current writer skips unchanged targets indefinitely, changes behavior
only when entering a different fan mode, and writes only the fan whose target
changed. The single one-second telemetry loop is guarded against overlapping
polls; it runs on a worker rather than doing hardware calls on the UI thread.
There is no fine-grained percentage ramp or fan keepalive loop.

These choices reduce firmware calls, but do not establish the cause of reported
v1.4 audio problems. The Acer ACPI WMI path can invoke SMI firmware handlers;
running that call on a worker does not remove firmware latency. A comparable
audio workload in firmware Auto and Custom, with an interrupt/scheduling trace,
is needed on an affected machine before claiming an audio fix. See Microsoft's
[audio latency explanation](https://microsoft.github.io/MIDI/kb/dpc-latency/).

## v1.4 compatibility

Per-mode fan curves persist under `Fans.{Quiet|Eco|Balanced|Performance|Turbo}` in
`preysense.json` (`ControlMode`, `RampUpSeconds`, `CpuCurve`, `GpuCurve`).
`RampUpSeconds` is the Fan Control Delay and controls increases and decreases.
`ControlMode` stores Auto (0), Max (1), or Custom (2). Profile apply flags such as
`Tuning.*.FanRampUp` / `ApplyFanCurve` live beside the CPU/GPU tuning profiles.
The old editor's points above 100°C are not needed by the new curve.
Reset supplies the selected mode's app preset. New profiles use the same presets
on all laptops; there is no model restriction. Existing saved curves take priority.

The presets are derived from the recovered first-variant ZBJ-KQ EC 1.08 tables:
for each 5°C point, advance through the factory stages when either rising sensor
threshold is reached, then scale the CPU/GPU RPM targets by 6800/6400 RPM to
whole percentages. A 20% floor replaces the factory fan-stop region and the
95/100°C points request 100%. These are approximate starting presets, not a
calibrated percentage-to-RPM mapping or an exact recreation of the factory policy.
Quiet and Eco share the same extracted table. Each returned preset is independent
and can be edited without changing defaults for other modes or users.

## Factory Auto vs Custom

Firmware **Auto** remains the way to run Acer's coupled factory fan policy.
Custom curves use derived percentage presets or user edits; they are not a
byte-for-byte recreation of the EC table. Acer's live fan-table getter does not
export usable temperature/duty points on this chassis.
