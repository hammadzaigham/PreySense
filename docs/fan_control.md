# Custom fans

The Fans button opens the fan editor. Eco, Quiet, Balanced, Performance and Turbo each save Auto, Max or Custom independently. Auto selects firmware policy; Max selects Acer's hardware Max mode. Custom evaluates a software temperature curve and sends percentage cooling requests through Acer WMI. New profiles default to Auto. Switching to Auto or Max preserves saved Custom points.

Percentages are requests, not raw PWM, a percentage of measured maximum RPM, or an absolute RPM target. Firmware determines the result. On the verified PHN16S-71 ZBJ-KQ EC 1.08 image, Custom adds a percentage of the remaining RPM headroom above the current factory stage target. A 0% request therefore does not promise stopped fans. This decoded behavior is specific to that image; other models and firmware require verification. Firmware can retain its own thermal policy in Custom.

The profile dropdown only selects a profile to edit and never changes the hardware mode. Reset saves zero boost defaults while preserving Auto/Max/Custom selection; Save persists drafts. Both refresh fan commands only for an already active profile. Inactive profiles only update configuration. The editor has no Apply button. Custom waits three seconds for a mode change to settle, then uses the shared one-second temperature producer. The editor displays the last successfully sent request separately from measured RPM, with no software-stage/status label. These are not EC ownership or enforcement readbacks. Opening the editor requests shared temperature/RPM telemetry while visible; it does not start a second hardware polling loop.

## Software curve behavior

A curve has at most 12 runtime stages. Each stage stores separate CPU/GPU exit temperatures and percentage requests. With Synchronize Fan Requests enabled, either sensor can advance the shared stage, both sensors must clear the previous stage's exit temperatures to retreat, and both fans receive the higher request. With synchronization disabled, each fan follows its own temperature stages; an unavailable GPU sensor falls back to CPU temperature. Rising thresholds increase the request immediately on the next temperature sample. Cooling lowers a stage only after the relevant temperature is at least 3°C below the threshold that entered it. Synchronized cooling requires both sensors to clear their thresholds by 3°C. There is no time-based fan delay. These software transitions do not reproduce factory fall thresholds or firmware filtering.

127 is the disabled-transition sentinel. A stage with both exit temperatures set to 127 is terminal: its request still applies, but no hotter software stage is reached. JSON stores reachable stages including the terminal plateau; loading expands them to twelve slots with disabled tail transitions. No EC stage array is uploaded.

The graph and editor use Cooling boost percentages. New and reset Custom curves start at 0% boost at every point; existing saved percentages are preserved. Raising a point also raises lower warmer points; lowering a point lowers higher cooler points so editing a flat zero curve works directly. RPM is read-only: the estimate uses factory-stage target + (reference maximum − factory-stage target) × boost / 100, rather than a linear fraction of maximum. The selected point temperature determines a rising-stage reference approximation for the selected fan; the other sensor, factory hysteresis and thermal protections can produce different actual speeds. Estimates always use the bundled PHN16S-71 EC 1.08 variant-0 curve; no user firmware is scanned or extracted. These estimates are not verified targets for other laptops. Existing percentage settings and legacy maximum-RPM fields remain compatible. Measure Fan RPM selects hardware Max immediately, observes RPM after the spin-up period, restores the previous fan policy and retains the existing scaling metadata; it does not calibrate a percentage-to-RPM curve or prove the fans' physical maximum.

Missing CPU temperature preserves the previous request and skips an update. Mode-change events and saving the active profile reload the policy; evaluation does not query the firmware operating mode each second. A rejected fan write pauses automatic writes until the active profile is saved again or the mode changes. Exiting returns software-controlled fans to firmware Auto. There is no separate emergency software override.

Unchanged requests are suppressed indefinitely. Fan behavior is sent on entry to a different cached mode, and only changed fan requests are written. There is no periodic keepalive or automatic ownership recovery. A successful WMI response is not proof the firmware retained the request. Another controller or firmware transition can change behavior without changing the app's cached last request.

## Factory reference

Factory reference data stays internal for estimates; it is not displayed in the editor. Firmware Auto runs the real coupled policy.

## Telemetry and latency

Custom requests temperatures while running, independently of window visibility. RPM is requested only by visible consumers or explicit measurement. Sleeping/unknown-power-state NVIDIA adapters are skipped before GPU-client construction. The one-second producer prevents overlapping polls and runs hardware work away from the UI thread.

Version 1.4.0 resent identical Custom requests after its five-second suppression window, re-entering Custom and writing both fans. Current suppression reduces calls. It does not establish an audio-latency fix: WMI can invoke firmware SMI handlers even from a background worker. A comparable Auto/Custom workload and interrupt/scheduling trace are needed to diagnose an affected machine.

## Storage

Per-mode settings remain under Fans.{Quiet|Eco|Balanced|Performance|Turbo} in preysense.json. ControlMode is Auto (0), Max (1), or Custom (2); stages retain CpuTempC, GpuTempC, CpuDutyPercent and GpuDutyPercent for compatibility. The legacy DutyPercent names mean requested percentage. Fixed 3°C cooldown hysteresis is used by the custom fan follower. Reset supplies the selected app preset; saved curves take priority. Only the current schema is loaded, with no v1 migration.

Successful Max-mode RPM measurement saves per-fan maxima and marks them measured. Subsequent estimates use those measured maxima as their interpolation endpoints; before a successful measurement, estimates use bundled CPU6800/GPU6400. The baseline remains the bundled curve, so this is still approximate. Earlier measurements without the explicit measured marker require measuring again.

Fan synchronization persistence, tuning saves and metrics settings run on serialized background workers or awaited worker tasks; UI values are captured before scheduling. Shutdown drains pending fan edits.
