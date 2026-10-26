# Custom fans

The Fans button in the main-window footer opens the themed fan editor. Eco,
Quiet, Balanced, Performance and Turbo each save one independent choice: Auto,
Max or Custom. Auto uses firmware policy; Max directly selects Acer's hardware
Max mode; Custom uses percentage curves and Fan Control Delay. Max is not a
custom-curve setting. The curve editor appears only for Custom, and switching
to Auto or Max preserves its saved points. New modes default to Auto.

Save persists drafts; Apply also activates the selected performance mode. Fan
settings follow performance-mode changes in the tray through the shared
one-second polling loop. Opening Fans again activates the existing window.
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
Both fan curves use the hotter of the CPU and GPU temperatures. Each fan retains
its own percentage targets, but either sensor can raise both fans. The graph's
live marker follows this shared temperature.
The horizontal dashed line shows the displayed fan's last successfully applied
percentage (100% in per-mode Max). It is a command indication, not measured PWM
feedback; the line is hidden in firmware Auto or when no applied value is known.

Cooling uses a 5°C hysteresis to prevent chatter at a boundary. Fan Control Delay
waits 0–5 seconds before accepting either a higher or lower target (default 2
seconds). At zero, changes apply on the next one-second evaluation without an
additional time delay. Old separate cooldown values are ignored; the existing
ramp-up setting now controls both directions and values above 5 are clamped.
Further changes in the same direction do not restart the clock; the
latest demand is used when it expires. Returning to the applied demand cancels
the pending change. These are delays, not gradual percentage ramps.
High-temperature cooling bypasses bands and delays (CPU 95°C or GPU 87°C forces
both fans to 100%). Emergency cooling stays latched until CPU is at most 90°C
and GPU is at most 82°C with valid sensors; recovery then observes Fan Control
Delay. That prevents oscillation near the emergency thresholds.

The existing 1 Hz telemetry loop remains active in the tray to follow mode
changes, including from Auto to a mode configured as Max or Custom. It evaluates
even identical samples so a pending response delay can finish.
Missing temperature readings or a rejected write return control to firmware
Auto; exiting the app also releases a software curve to Auto. There is no
periodic fan-PWM keepalive.

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

Existing per-mode CpuCurve_i_Y / GpuCurve_i_Y points and FanRampUp are reused under
HKCU\SOFTWARE\PreySense\Profiles\{mode}. FanRampUp is retained as the storage key
for Fan Control Delay and controls increases and decreases. FanControlMode stores
Auto (0), Max (1), or Custom (2). FanCurveEnabled and FanMax are read only for
legacy migration. Legacy global curve points are read as a
fallback. The old editor's points above 100°C are not needed by the new curve.
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

## Factory EC tables

Factory tables were found by offline analysis of the cached Acer BIOS V1.26
package, which contains the ZBJ-KQ EC 1.08 payload built 2025-12-29. The running
PHN16S-71 reports BIOS V1.28 and EC 1.08 through SMBIOS. The EC version matches;
the running EC flash was not dumped, so byte-for-byte identity is not established.

See [the extraction evidence](factory_fan_tables_phn16s71.md) for firmware
addresses, row semantics, mode selection, and the reason Acer's getter fails.
`tools/extract_factory_fans.py` exports JSON and CSV from this verified layout
without hardware access. It rejects unknown signatures. These tables remain
separate from user curves and the immutable factory power calibration.

## Diagnostic history

The existing DSDT dump declares EFN1 / EFN2 at EC offsets 0x49 / 0x4A, ETP1 / ETP2
at 0x51 / 0x52, and FANM at 0x54. Those are active fan/sensor/mode fields, not a
complete temperature-to-duty table. Existing Acer WMI code exposes sensor reads,
fan behavior, and per-fan percentage writes. The inspected source does not expose
a verified factory-curve read command.

### Live diagnostic results

An elevated diagnostic batch opened the installed PawnIO driver and loaded the
official LpcACPIEC module from PawnIO.Modules 0.2.11. The standard 0x80 EC read
transaction timed out waiting for output; the batch stopped that read path.

Acer WMI temperature, RPM, and behavior reads succeeded. GetGamingFanTable
(method 19) returned 0x1: status 1, with no usable table selection. That getter
returns one UInt64, not a set of temperature/duty points; its presence alone does
not establish that firmware curves can be exported.

A fresh elevated WMI-only capture on 2026-10-26 confirmed administrator access
and successful live sensor reads. GetGamingFanTable returned the same status 1
in all three samples. The report is `elevated-wmi.json` in the local diagnostic
output folder. No fan targets, operating modes, or EC memory were written.

The DSDT maps ERAM to 0xFE800500 and ERM6 to 0xFE800600, both 256-byte regions.
This explains why mapped-memory access is a separate route from legacy EC ports;
it does not prove that ERM6 contains fan curves. The available RWEverything 1.7
crashed during the mapped-memory attempt and exported no register pages.

Subsequent direct reads through the already-running inpoutx64 driver successfully
captured ERM4, ERAM and ERM6. ERM6 contains power-profile values, not the fan
tables. The EC firmware loads its fan table into private XRAM at 0x0800;
reading host address 0xFE800800 returned only FF bytes. This is not a usable
live fan-table mapping. The factory tables were instead recovered from the
firmware payload described above.

Firmware Auto remains the way to execute the exact coupled factory policy.
The custom editor uses the derived percentage presets or user edits; exact
factory RPM feedback is still handled by Auto. Diagnostics did not change fan
targets or operating modes.
