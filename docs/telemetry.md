# Telemetry loop

One one-shot, one-second timer in `TelemetryCoordinator` owns the cycle. It
restarts after the previous cycle completes, so slow hardware cannot overlap
the next cycle. Demand changes and settings application do not schedule extra
hardware work. Explicit poll requests share the same rate limit.

## Requested metrics

`RequestedMetrics` is the union of the currently active consumers:

| Consumer | Request |
| --- | --- |
| Visible main window | CPU/GPU temperatures, both fan RPMs, and the primary display adapter |
| Visible overlay | Only the CPU/GPU metric selected in each column |
| Active Custom fan profile | CPU/GPU temperatures required by the curve |
| Fan editor | None; edits saved curves without live sensor markers |
| Hidden Auto/Max fan profiles | None |
| Visible FPS selection | FPS, average frame time and target PID; no hardware sensors |

The overlay publishes its selection when configuration changes. Hidden overlay
selections are ignored. None slots and disabled graphs add no reads. Duplicate
selections share one source read. A selected numeric value still needs its source
when its graph is disabled.

`TelemetryCollector` calls only the requested sources. CPU usage, frequencies, and memory information share their underlying OS queries.
The EC reader can read only the selected fan. The NVIDIA batch checks state once
and queries only selected GPU fields. NVML is released when no selected field
needs it. RAM speed is read once, on first selection, because it is static module
metadata. Power-source automation uses existing Acer events, not telemetry polling.

## Delivery

The shared timer runs lightweight overlay/FPS housekeeping, samples the requested
sources, then publishes one completed snapshot. Active Custom fan control and
the visible main window and overlay consume it without further reads. The main
window subscribes only while visible and removes its temperature/RPM demand when
hidden. It displays N/A until a fresh sample arrives. Display refresh-rate status
is not polled. The active display-GPU label comes from Windows display-adapter enumeration in the same cycle; it does not open a vendor GPU client. The fan editor requests no telemetry.

Before accessing the NVIDIA client, the sampler checks Windows-maintained device
power data. An asleep adapter is not queried through NVAPI/NVML. Custom fans use
CPU temperature alone while GPU temperature is unavailable; with both available,
both curves follow the hotter sensor. Custom fan demand contains no RPM, GPU
usage, power, clock, or memory reads. Explicit overlay selections can still request
these readings, sharing the same cycle.

Graphs record only enabled, selected series, once per metric. Fan graphs use EC
RPM directly without depending on GPU activity. Repainting happens once per
snapshot; a separate hash of a subset of metrics no longer suppresses updates.
FPS, average frame time and target PID are optional fields in that same immutable
snapshot. The selected/visible FPS consumer flushes its ETW buffers and samples
its accumulated frame counters once through the shared producer. Because ETW
buffer delivery is asynchronous, the last completed batch remains usable for at
most two shared intervals; older frame data becomes N/A. FPS-only
selection skips the hardware collector entirely. The overlay renders snapshot
values rather than reading FPS independently.

The timer runs only when actual sensor metrics or visible FPS are requested. An
empty overlay with FPS off does not keep it alive. Changing metric selections
starts/stops the existing timer even when the consumer demand bit is unchanged.
Hiding the main window/overlay removes their requests; Custom fan control can
retain temperature-only demand. With all three consumers idle, the timer stops.

Removed paths include separate display/overlay/fan-editor timers, independent
fan-temperature requests, legacy overlay sampling and warmup, extra post-switch
polling, fallback samplers, repeated GPU probes, duplicate battery reads, and
duplicate UI metric copies. Unexpected unavailable readings
remain N/A. ETW frame arrival is event-driven; its former independent 200 ms
flush timer has been removed. Frame-source failure yields N/A for FPS without
discarding successfully collected hardware temperatures.

## Event-driven work

Foreground changes update overlay visibility and FPS targeting through a Windows
foreground notification, with a one-time refresh on resume. Completed HID reads arrive through asynchronous I/O
instead of periodically waiting on each reader. Keyboard idle lighting waits for
keyboard activity or timeout-setting changes instead of waking every 250 ms while
idle. Startup lighting is scheduled once after application ownership is ready.

The one-second sensor clock remains necessary because these hardware sources do
not publish temperature-change events. Animation frames, timed idle transitions,
UI fade/timeout behavior, ETW capture, and bounded hardware-readiness waits remain
where time is part of the feature.

## CPU power and clocks

CPU package power uses the bundled driver's RAPL reads: MSR `0x606` supplies energy units and `0x611` supplies cumulative package energy. Watts are the energy delta divided by elapsed time. The first sample establishes a baseline; failed reads and unavailable hardware remain N/A. The overlay no longer uses PDH power counters.

All CPU clock selections share one topology pass per cycle. Each logical processor is sampled with affinity using APERF, MPERF, and TSC. The overlay exposes CPU Active Clock and CPU Effective Clock. Active Clock shows the maximum active frequency across classes; Effective Clock shows the maximum wall-time effective frequency. These maxima can belong to different cores. CPU clocks require two samples and have no PDH fallback. No selected CPU clock means no clock pass.

CPU Voltage is labeled VID because it is requested voltage, not measured Vcore. RAM Data Rate is reported in MT/s using cached configured-module metadata; GPU memory clock remains in MHz.

The CPU/GPU headers are always displayed; the Hide Header setting has been removed. Older JSON files containing that property can still be loaded, and the obsolete field is ignored.

GPU power remains `nvmlDeviceGetPowerUsage`, converted from mW to W. GPU clock sources are described below. No fixed per-call latency or battery-cost claim is established by the implementation; these require runtime measurement.

## Metric and Throttle Reason Abbreviations

Overlay and telemetry metrics employ clear, standardized abbreviations to fit compact heads-up displays:

### Telemetry & Voltage Metrics
- **CPU Voltage**: Requested Core Voltage Identification (VID) in Volts (`V`), sampled directly via `IA32_PERF_STATUS` (MSR 0x198 bits [47:32]). Note: Measured motherboard Vcore is unsupported unless an actual motherboard sensor source is established; VID reflects the target requested by the CPU voltage regulator control loop.
- **GPU Voltage**: GPU Core Voltage in Volts (`V`), sampled via NVAPI/NVML.
- **Active Clock**: Active unhalted core clock frequency in MHz (`(ΔAPERF / ΔMPERF) * RefClock`).
- **Effective Clock**: Wall-time effective frequency in MHz taking C-state idle/sleep residency into account (`Active Clock * C0 residency`).
- **PL1 / PL2**: Long-duration and short-duration CPU power limits in Watts (`W`).

### GPU clock selection

GPU Clock uses the NVAPI current graphics-clock query (`NvAPI_GPU_GetAllClockFrequencies`, version 2, graphics domain 0), rounded from kHz to MHz. The NVML GPU Effective Clock option and its query path have been removed. Saved GPU Active Clock selections are renamed Clock; saved GPU Effective Clock selections become None. Saved Clock now explicitly selects NVAPI.

P/E usage, Max Core usage and separate CPU P/E clock selections have been removed. CPU Usage requests only aggregate Windows counters. Removed selections become None without sampling their sources. CPU Active Clock and CPU Effective Clock have no max suffix in the selector.

### GPU PerfCap Reason
GPU throttle reasons are derived from NVIDIA's NVML clocks event reasons bitmask (`nvmlDeviceGetCurrentClocksThrottleReasons`) and displayed with concise, slash-delimited tags (`/`):
- **Pwr**: Software power capping limit.
- **Thrm**: Software thermal slowdown.
- **HWThrm**: Hardware thermal slowdown (critical thermal protection).
- **Brake**: External power brake / hardware slowdown triggered by external power brake assertion.
- **Board**: Board power level throttling.
- **VRel**: Display label for the generic NVML reliability flag. This source does not separately establish voltage reliability versus operating-voltage limits.
- **HWSlw**: Hardware slowdown.
- **Clock**: Application clock setting limit.
- **Sync**: Multi-GPU or display sync limit.
- **Disp**: Display clock setting requirement.
- **Idle**: Clocks reduced by idle state.
- **None**: No throttling flags active.
- **Other**: Reserved or unknown vendor throttle reason flag.
- **-**: Telemetry or sensor unavailable.


## Missing readings

Failed reads remain unavailable. Package temperature is never substituted with core temperature. CPU active clocks are not substituted with another clock, and wall-time estimates are not substituted for unavailable TSC deltas. Unknown graphs contain gaps instead of zero samples. RAM collection failures invalidate cached readings immediately. Missing GPU baselines leave nominal wattage unavailable while independent, captured offsets remain usable.
