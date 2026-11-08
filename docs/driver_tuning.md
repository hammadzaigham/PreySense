# Driver tuning

PreySense applies per-mode CPU and GPU power limits through the bundled **PreySenseDriver**. There is no bootloader, ACPI overlay, or alternate WMI injection path.

## Enablement and persistence

Enable Tuning is stored as `General.HardwareTuningEnabled` in `preysense.json`. When it is on, mode changes, AC/battery source profiles, startup, and resume apply that mode's saved CPU PL1/PL2/Tau and GPU Delta/Dynamic Boost values from JSON. Unsaved editor values are not used for automatic restores.

- **Apply** writes the current editor values to hardware.
- **Save** applies those values and persists the mode profile after a successful driver transaction.
- **Reset** restores the mode to captured factory defaults.
- Disabling tuning leaves Acer mode switching active without custom CPU/GPU writes.

Unset user tuning fields stay null in JSON. The editor shows captured factory defaults as display fallbacks. Calibration never writes user profiles; only explicit Save does. Incomplete user profiles are skipped by automatic application.

## Factory calibration

At startup, missing factory defaults are captured automatically into `Tuning.FactoryDefaults` without a prompt. Calibration also reads global static CPU PL1/PL2 from MSR 0x610 into `FactoryStaticCpuPl1Watts` / `FactoryStaticCpuPl2Watts`. Reads happen immediately without switching modes or writing limits. Existing complete defaults (including static floors) are preserved. Failed or incomplete captures are not saved.

When applying CPU limits at or below those calibrated static floors, PreySense writes MMIO only and leaves MSR 0x610 unchanged. Raises above the floors still write both MSR and MMIO. Reset restores MSR 0x610 to the calibrated static floors when live MSR is above them, then writes the mode’s factory MMIO limits.

## Global PROCHOT offset

PROCHOT Offset sits under Enable Tuning as a global nullable value (0-63 °C below TjMax). A label row above the offset shows PROCHOT activation on the left and `TjMax` on the right (`TjMax − offset`). Its switch controls whether the offset is applied. Off leaves the hardware offset alone. On applies the global value (after CPU/GPU tuning when those are enabled, or on its own when both limit switches are off) and verifies readback. Apply uses the editor value; Save also persists `Tuning.ApplyProchotOffset` and `Tuning.ProchotOffsetCelsius`. The value is never stored in individual mode profiles.

The driver checks programmable-offset support and BIOS locks, preserves unrelated temperature-target bits, and attempts to restore the original offset if write/readback fails. Calibration stores the readable factory offset once as `Tuning.FactoryProchotOffsetCelsius`.

## Application order

All manual, automatic, and session-restore operations share the mode-switch semaphore.

1. Stage the GPU profile on an alternate Acer mode (ERM4 Dynamic Boost / Delta bytes; platform-offset byte preserved). Manual Apply/Save with GPU limits off skips this and does not change Acer mode.
2. Verify the inactive profile, wait 2 s for the EC, then reactivate the target mode. CPU-only applies do not wait.
3. Write CPU package MSR and MCHBAR MMIO limits last, because an EC transition can reload CPU limits. Mode restores still switch Acer mode before CPU/PROCHOT-only writes.

CPU writes snapshot register state, validate access and lock bits, verify readback, and attempt rollback on failure. GPU staging failures use verified rollback. A CPU failure after GPU staging reports that GPU staging already completed. Startup restore reports partial failure rather than claiming full success.

User controls accept Delta in 5 W steps from 0 to 100 W and Dynamic Boost in 5 W steps from 0 to 25 W (including 5 W). Nominal GPU watts are the detected NVIDIA default plus those offsets; sustained draw remains subject to NVIDIA driver and firmware policy. On a given SKU, Delta or Dynamic Boost above the VBIOS/TGP ceiling may write successfully but not raise measured power.

## Hardware discovery

No laptop-model or BIOS-version name check is required. Tuning still needs:

- Driver initialization
- A supported Raptor Lake host bridge / MCHBAR layout (including `7D1C`, `4637`, `463B`)
- Live ERM4 base discovery from DSDT `OperationRegion(ERM4)` or a signature probe at known bases such as `0xFE800400` / `0xFE708400`

GPU thermal-trip editing and injected ACPI controls are removed. Temperature telemetry and NVIDIA thermal-throttle reporting are unchanged.
