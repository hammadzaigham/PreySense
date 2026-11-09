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

At startup, missing factory defaults are captured automatically into `Tuning.FactoryDefaults` without a prompt. Calibration also reads global static CPU PL1/PL2 from MSR 0x610 into `FactoryStaticCpuPl1Watts` / `FactoryStaticCpuPl2Watts`. Existing complete defaults (including static floors) are preserved. Failed or incomplete captures are not saved.

When an Acer ERM4 (`"Predator"`) profile table is present, calibration reads all five mode CPU and GPU offsets from that table without switching modes. On SKUs without ERM4 (for example PHN16-72), calibration switches each Acer mode briefly, settles, and reads live MCHBAR MMIO CPU PL1/PL2/Tau only. GPU Delta/Dynamic Boost/platform fields are not captured, and GPU profile apply stays disabled. Each selected mode is checked after the 2-second settling interval. The original mode is restored, allowed to settle, and read back before any captured defaults can be saved; restoration failure is reported, including the original capture error if both operations fail. Read-only locked MMIO limits can be captured, but later writes still honor the lock.

When applying CPU limits at or below those calibrated static floors, PreySense writes MMIO only and leaves MSR 0x610 unchanged. Raises above the floors still write both MSR and MMIO. Reset restores MSR 0x610 to the calibrated static floors when live MSR is above them, then writes the mode’s factory MMIO limits.

## Global PROCHOT offset

PROCHOT Offset sits under Enable Tuning as a global nullable value (0-63 °C below Tj-Max). A label row above the offset shows PROCHOT activation on the left and `Tj-Max` on the right (`Tj-Max − offset`). Its switch controls whether the offset is applied. Off leaves the hardware offset alone. On applies the global value (after CPU/GPU tuning when those are enabled, or on its own when both limit switches are off) and verifies readback. Apply uses the editor value; Save also persists `Tuning.ApplyProchotOffset` and `Tuning.ProchotOffsetCelsius`. The value is never stored in individual mode profiles.

The driver checks programmable-offset support and BIOS locks, preserves unrelated temperature-target bits, and attempts to restore the original offset if write/readback fails. Calibration stores the readable factory offset once as `Tuning.FactoryProchotOffsetCelsius`.

## Application order

All manual, automatic, and session-restore operations share the mode-switch semaphore.

1. Stage the GPU profile on an alternate Acer mode (ERM4 Dynamic Boost / Delta bytes; platform-offset byte preserved). Manual Apply/Save with GPU limits off skips this and does not change Acer mode.
2. Verify the inactive profile, wait 2 s for the EC, then reactivate the target mode. CPU-only applies do not wait.
3. Write CPU package MSR and MCHBAR MMIO limits last, because an EC transition can reload CPU limits. Mode restores still switch Acer mode before CPU/PROCHOT-only writes.

CPU writes snapshot register state, validate finite time windows and power ranges before conversion, check lock bits, verify readback, and attempt rollback on failure. A driver write returning failure is treated as potentially partial and also triggers verified rollback. Combined transaction results carry rollback facts directly instead of inferring success from diagnostic wording. GPU staging failures use verified rollback. A CPU failure after GPU staging reports that GPU staging already completed. Startup restore reports partial failure rather than claiming full success.

User controls accept Delta in 5 W steps from 0 to 100 W and Dynamic Boost in 5 W steps from 0 to 25 W (including 5 W). Nominal GPU watts are the detected NVIDIA default plus those offsets; sustained draw remains subject to NVIDIA driver and firmware policy. On a given SKU, Delta or Dynamic Boost above the VBIOS/TGP ceiling may write successfully but not raise measured power.

## Hardware discovery

No laptop-model or BIOS-version name check is required. Tuning still needs:

- Driver initialization
- A supported Raptor Lake host bridge / MCHBAR layout (including `7D1C`, `4637`, `463B`)
- Either live ERM4 discovery (`OperationRegion(ERM4)` / `"Predator"` signature at known bases such as `0xFE800400` / `0xFE708400`) **or** Acer mode switching plus readable MCHBAR package limits for CPU-only factory capture

CPU and PROCHOT tuning work without ERM4. GPU profile apply requires ERM4 GPU bytes; without them the GPU limits switch stays off.

GPU thermal-trip editing and injected ACPI controls are removed. Temperature telemetry and NVIDIA thermal-throttle reporting are unchanged.

## Audit references and limits

The package-limit register at MCHBAR + `0x59A0` is documented by [Intel's 13th-generation register reference](https://edc.intel.com/content/www/us/en/design/publications/13th-generation-core-processors-datasheet-volume-2-of-2/package-power-limit-package-rapl-limit-0-0-0-mchbar-pcu-offset-59a0/). Its lock makes the settings read-only; it does not prohibit reading them. The 7-bit time window follows `2^Y × (1 + F/4) × TimeUnit`, including encoded zero. Live calibration decodes zero rather than substituting a 56-second default; the editor then rounds/snaps the captured seconds to its supported selections. The apply API's `tauEncoded == 0` convention (preserve the previous window) is separate from hardware decoding.

The audit used mocked writes and mode controls. It did not exercise live tuning or calibration. EC settling, Acer-specific profile bytes, static-floor policy, and behavior on additional SKUs still require physical-machine verification; Intel's register documentation alone does not establish those Acer policies.

## Calibration orchestration

Startup requests one capture when factory profiles or static CPU limits are incomplete. Concurrent callers share that operation. The mode-switch gate and busy state cover the complete hardware capture; pending mode requests are cancelled before capture begins. The prior busy state and gate are restored on every exit.

A successful capture replaces the complete factory-profile set and its global metadata in one validated JSON update. It does not merge old optional readings: an unavailable PROCHOT offset, lock or TjMax remains null. Invalid or interrupted captures do not commit partial results. User tuning controls remain separate from captured factory defaults.

Enabling tuning waits for any active capture and requires both complete mode defaults and valid static CPU limits. It does not initiate calibration. Startup session restoration does not hide a calibration failure behind a generic success message. No calibration prompt, progress UI or retry control is added.
