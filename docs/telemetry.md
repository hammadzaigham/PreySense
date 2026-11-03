# Telemetry loop

One one-shot, one-second timer in `TelemetryCoordinator` owns the cycle. It
restarts after the previous cycle completes, so slow hardware cannot overlap
the next cycle. Demand changes and settings application do not schedule extra
hardware work. Explicit poll requests share the same rate limit.

## Requested metrics

`RequestedMetrics` is the union of the currently active consumers:

| Consumer | Request |
| --- | --- |
| Main window | None; controls only |
| Visible overlay | Only the CPU/GPU metric selected in each column |
| Active Custom fan profile | CPU/GPU temperatures required by the curve |
| Fan editor | None; edits saved curves without live sensor markers |
| Hidden Auto/Max fan profiles | None |
| FPS-only overlay | No hardware metrics |

The overlay publishes its selection when configuration changes. Hidden overlay
selections are ignored. None slots and disabled graphs add no reads. Duplicate
selections share one source read. A selected numeric value still needs its source
when its graph is disabled.

`TelemetryCollector` calls only the requested sources. CPU usage/core usage,
P/E frequencies, and memory information share their underlying OS queries.
The EC reader can read only the selected fan. The NVIDIA batch checks state once
and queries only selected GPU fields. NVML is released when no selected field
needs it. RAM speed is read once, on first selection, because it is static module
metadata. Power-source automation uses existing Acer events, not telemetry polling.

## Delivery

The shared timer runs lightweight overlay/FPS housekeeping, samples the requested
sources, then publishes one completed snapshot. Active Custom fan control and
the overlay consume it without further reads. Main-window CPU/GPU temperature,
RPM, refresh-rate status, and active display-GPU labels have been removed. The
main window and fan editor never request or subscribe to display telemetry.

Before accessing the NVIDIA client, the sampler checks Windows-maintained device
power data. An asleep adapter is not queried through NVAPI/NVML. Custom fans use
CPU temperature alone while GPU temperature is unavailable; with both available,
both curves follow the hotter sensor. Custom fan demand contains no RPM, GPU
usage, power, clock, or memory reads. Explicit overlay selections can still request
these readings, sharing the same cycle.

Graphs record only enabled, selected series, once per metric. Fan graphs use EC
RPM directly without depending on GPU activity. Repainting happens once per
snapshot; a separate hash of a subset of metrics no longer suppresses updates.
An FPS-only overlay repaints from the shared heartbeat without sampling hardware.

When no sensor consumer needs data, no metrics are collected. An enabled but
temporarily hidden overlay can still use the shared lightweight heartbeat.
When the overlay is stopped as well, the timer stops entirely.

Removed paths include separate display/overlay/fan-editor timers, independent
fan-temperature requests, legacy overlay sampling and warmup, extra post-switch
polling, fallback samplers, repeated GPU probes, duplicate battery reads, and
duplicate UI metric copies. Unexpected unavailable readings
remain N/A. FPS ETW capture/flush and its display thresholds are unchanged.

## Event-driven work

Foreground changes update overlay visibility and FPS targeting through a Windows
foreground notification, with a one-time refresh on resume. Game-profile evaluation
also uses foreground, process-exit, and resume events; it skips process queries when
there are no enabled profiles. Completed HID reads arrive through asynchronous I/O
instead of periodically waiting on each reader. Keyboard idle lighting waits for
keyboard activity or timeout-setting changes instead of waking every 250 ms while
idle. Startup lighting is scheduled once after application ownership is ready.

The one-second sensor clock remains necessary because these hardware sources do
not publish temperature-change events. Animation frames, timed idle transitions,
UI fade/timeout behavior, ETW capture, and bounded hardware-readiness waits remain
where time is part of the feature.
