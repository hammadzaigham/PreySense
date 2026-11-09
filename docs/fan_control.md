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

Custom mode follows EC-style **stages** (12 gears), not a 5°C percentage grid.
Each stage has CPU/GPU rise and fall thresholds (°C) and CPU/GPU target RPM.
Rising on either sensor advances to the next stage; falling retreats when both
sensors clear their fall thresholds (`127` disables that trip). Rise/fall is the
only hysteresis; there is no extra stage-change delay. Duties sent to WMI are
`RPM / 6400` for both fans (measured Max ≈ 6250/6380). If the GPU is asleep, its temperature
is treated as 0°C for trips. Opening the editor requests no telemetry.
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
`preysense.json` as `ControlMode`, `RampUpSeconds`, and `Stages` of
`{ CpuTempC, GpuTempC, CpuDutyPercent, GpuDutyPercent }` (stored per sensor; both fans apply the higher duty).
`ControlMode` stores Auto (0), Max (1), or Custom (2).
Reset supplies the selected mode's app preset. Existing saved curves take priority.

Custom presets start from recovered ZBJ-KQ EC 1.08 stage tables. Quiet and Eco share
the same table. Duties are approximate: Acer Custom accepts percent, not RPM feedback.
Firmware Auto remains the authentic EC policy.

## Factory Auto vs Custom

Firmware **Auto** remains the way to run Acer's coupled factory fan policy.
Custom curves use derived percentage presets or user edits; they are not a
byte-for-byte recreation of the EC table. Acer's live fan-table getter does not
export usable temperature/duty points on this chassis.
