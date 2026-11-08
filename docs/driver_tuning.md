# Driver tuning

## Global PROCHOT offset

PROCHOT Offset is a global, nullable value directly below Enable Tuning. Its unlabeled switch controls whether the offset is included alongside the selected mode's other tuning parameters. Off leaves the hardware offset untouched. On applies the global value after successful CPU/GPU tuning and verifies driver readback. Apply uses the editor value; Save also persists `Tuning.ApplyProchotOffset` and `Tuning.ProchotOffsetCelsius`. The value is never stored in individual mode profiles.

The supported control range is 0–63°C below TjMax. The driver checks programmable-offset support and BIOS locks, preserves unrelated temperature-target bits, and attempts to restore the original offset if write/readback fails. This follows [Intel's TCC activation-offset definition](https://edc.intel.com/content/www/us/en/design/products/platforms/details/raptor-lake-s/13th-generation-core-processors-datasheet-volume-1-of-2/adaptive-thermal-monitor/). Calibration saves the readable factory offset once as `Tuning.FactoryProchotOffsetCelsius`.

At startup, missing factory defaults are captured automatically into `Tuning.FactoryDefaults` in `preysense.json`, without a prompt. Calibration reads the stored firmware records immediately without switching modes, waiting, or writing limits. Existing complete defaults are preserved. Failed or incomplete captures are not saved. These records are reused when Reset restores a mode to its captured factory profile.

Unset user tuning values remain null in JSON; the tuning editor displays captured factory defaults as fallbacks. Calibration never populates user tuning profiles; only explicit Save persists user values. Automatic application skips incomplete user profiles.

Enable Tuning is persisted in `General.HardwareTuningEnabled` in `preysense.json`. With it enabled, mode selection, AC/battery profile changes, startup, and resume apply the selected mode's saved CPU PL1/PL2/Tau and GPU Delta/Dynamic Boost values. Automatic application reads the saved configuration, so unsaved editor changes do not become startup settings.

Manual Apply uses editor values. Save applies those values and updates the mode's JSON profile after the driver transaction succeeds. Disabling tuning leaves Acer mode switching active without custom CPU/GPU writes.

All manual, automatic, and session-restore operations share the mode-switch semaphore. GPU staging switches to an alternate Acer mode, writes and verifies the inactive target profile, waits for the EC, and reactivates the target mode. CPU static MSR and dynamic MMIO limits are written last because an EC transition can reload CPU limits. Intermediate mode notifications are ignored while an application is in progress.

The tuning path does not require a laptop-model or BIOS-version name match. It still requires driver initialization and discovery of a supported host bridge/MCHBAR layout (Raptor Lake IDs including `7D1C`, `4637`, `463B`). GPU profile addresses come from the live ERM4 base (DSDT `OperationRegion(ERM4)` or a signature probe at known bases such as `0xFE800400` / `0xFE708400`); the per-mode DB/CT/CP layout is unchanged.

GPU staging failures use the existing verified rollback. CPU transaction failures retain their existing rollback and explicitly report that GPU staging already completed. Startup restore reports partial failure rather than claiming all settings were applied. Driver errors are surfaced; the removed injected ACPI interface is not used as a fallback.

GPU thermal-trip editing and its WMI get/set calls have been removed. Factory defaults are held in JSON and contain no GPU thermal-trip control. Temperature telemetry and NVIDIA thermal-throttle reporting are unaffected.
