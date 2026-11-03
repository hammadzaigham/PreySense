# Window and function audit — 2026-11-03

Reviewed window constructors, loading, event handlers, editing and save/apply
paths, shared services they invoke, and closing/subscription cleanup. This is a
source audit and automated validation; it does not certify device support on
every Acer model.

| Window | Review and result |
| --- | --- |
| Main | Reviewed visibility, tray/restore, navigation, dialog creation and startup. Removed temperature/RPM and refresh-rate/display-GPU status labels, display telemetry properties, snapshot subscriptions, and visibility-based sampling. The window is controls only. Removed duplicate mode-editor initialization. |
| Mode settings | Reviewed profile selection, CPU/GPU limit editors, boost, reset, save, apply and close. Shared Save/Apply editor collection and trip-point storage. Removed repeated confirmation when the mode is already confirmed and duplicate boost application after switching. Firmware checks run off the UI thread. Property-descriptor listeners are removed on close. |
| Fan editor | Reviewed profile loading, tabs, curve editing, response delay, mode toggles, save/apply/reset and close. Removed live markers and all telemetry subscriptions. The graph contains a static grid, curve and editable points. Mode activation/confirmation shares one helper; saved drafts are cleared. |
| Metrics | Reviewed initial settings, all column/graph controls, FPS, scale, opacity, position, enable/disable and subscription cleanup. Selection feeds the request mask. Removed redundant registry wrappers and duplicate saves caused by slider snapping. |
| Keyboard | Reviewed load/bind/unbind, tabs, modes, animation, zones, presets, color wheels and hex inputs. VM-to-UI preset synchronization no longer executes the preset hardware command again. Existing color-input recursion guards and close cleanup remain. |
| Advanced settings | Reviewed language initialization and audio navigation. Existing equality guard avoids rewriting an unchanged language. Shared localization now refreshes each VM once, even when several windows share it. |
| Advanced/ACPI | Reviewed visible-window status refresh and navigation. Status refresh is needed for the displayed OpenCore state; no polling timer was found. Explicit maintenance operations remain explicit. |
| Games | Reviewed construction and bindings to the game/profile commands. No duplicate window polling or startup work was found. Foreground game tracking remains event driven. |
| DTS audio | Reviewed load/refresh, selected-endpoint customization, equalizer and factory reset. Enumeration occurs on opening/explicit refresh; it is not part of telemetry. |
| Custom audio | Reviewed construction, VM ownership and close callback. No metric polling or duplicate window work was found. |
| DTS equalizer | Reviewed construction, VM ownership and close callback. No metric polling or duplicate window work was found. |
| Confirmation/progress dialog | Reviewed modal result handling, allow-close guard, text/button properties and all result buttons. The handlers represent distinct user choices; no polling or duplicate work was found. |
| Mode toast | Reviewed display, fade/timeout, monitor positioning, close and shutdown. Its UI hide timer is unrelated to hardware telemetry and remains necessary. |
| Hardware overlay | Reviewed start/stop, settings reload, sampling subscription, FPS/visibility housekeeping, scalar rendering and graphs. Uses selected sources and the shared timer. Foreground notifications update visibility/FPS targets, including once on resume. Removed duplicate scalar copies and paint-hash suppression; records only selected graph series. RPM-only graphs do not require GPU activity reads. |

Legacy driver cleanup now runs once at app startup rather than being coupled to
overlay startup. Starting an overlay no longer warms up CPU-usage sampling when
usage was not selected.

No new hardware compatibility fallbacks or retry loops were added. Supported
sources are used directly; unavailable values stay unavailable. Command-success
and mode-confirmation checks remain because their results determine whether an
operation actually worked.

Custom fan control alone subscribes to completed temperature samples and writes
changed targets. Auto and Max do not subscribe. Custom control requests no RPM
or ancillary metrics. Windows-maintained power data gates NVIDIA access, so a
sleeping GPU is skipped and the CPU temperature drives both curves. A read-only
probe on this laptop reported NVIDIA D3 in approximately 4 ms without loading
the NVAPI assembly. This verifies the power-state check, not real-world audio
latency or every model's GPU power behavior.

Idle keyboard lighting now waits for key/settings events. HID readers use
cancelable asynchronous I/O completion instead of periodic completion checks and
dedicated reader threads. Foreground game tracking coalesces events and skips
process queries when there are no enabled profiles. Startup RGB application is
scheduled once after application readiness. Time-based animation, fades, sensor
sampling and bounded hardware readiness checks remain where required.

Validation includes the complete test suite, fake-source call counts for every
metric, visibility/selection unions, FPS-only zero-read behavior, graph RPM
independence, CPU-only Custom control with sleeping GPU, event wake/cancellation,
and WPF drawing validation of the static grid, curve and points without live markers.
See `telemetry.md` for the resulting collection/delivery flow.

Power-source state is initialized explicitly at startup/resume and
updated by events, so removing telemetry status reads does not leave battery
mode represented as AC. A cross-reference of private window methods against
code and XAML found no orphaned handlers.

Final automated validation: 251 tests passed, 1 skipped; the test build completed
without errors. Native HID button behavior and audio latency have not been
verified in a running build of these changes. Source changes have not been deployed.

## Fractional DPI rendering

The main window alone previously enabled UseLayoutRounding, SnapsToDevicePixels
and Display text formatting. Other windows retained false/false/Ideal defaults;
owner windows do not supply visual-tree inheritance to a separate window. All
13 WPF window roots now explicitly use one AppWindowRenderingStyle containing
the original main-window settings. The manifest already declares PerMonitorV2.

An isolated resource/control rendering probe at 125% and 150% confirmed that
child labels inherit the three settings and align to whole device pixels. Its
shared-style output matches the original main-window settings pixel for pixel
at both scales. This is offscreen rendering validation, not a live multi-monitor
or monitor-change test. See Microsoft's
[layout rounding documentation](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/layout#sub-pixel-rendering-and-layout-rounding)
for the fractional-pixel rendering behavior.
