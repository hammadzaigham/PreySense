# GPU power limits and independent NVPCF offsets

## Calibrated baseline and shared results

Calibration reads NVIDIA's default power management limit through `nvmlDeviceGetPowerManagementDefaultLimit`, converts milliwatts to the UI's whole watts, and saves `GpuBasePowerWatts` and its source under the factory-profile registry key. It never substitutes instantaneous GPU draw for the baseline. Saved baselines are reused for mode estimates, legacy game-total conversion, and calibration totals calculated from raw CT/DB offsets. The embedded legacy ACPI method 6 still uses its historical 85 W reference; calibrated application code uses raw method-12 offsets instead.

Existing locked factory profiles may gain only missing baseline metadata on the next validated startup; their mode defaults and calibration lock are not overwritten. If detection fails, no invented baseline is saved, and the result explicitly reports that the legacy arithmetic fallback remains in use. On this laptop the read-only detection probe returned 85 W.

Calibration and the read-only Factory Defaults view share one summary formatter. Values identical across the captured profiles (including Tau, PL2, thermal trip and raw offsets) appear once in shared rows. Values that actually vary remain attached to the corresponding profile. Baseline is always a separate shared row.

Mode tuning exposes Delta and Dynamic Boost. The platform offset is fixed internally at **255 W**, the maximum representable by the Acer mode-profile byte. None of these offsets is a measurement or a guarantee of sustained GPU watts. The table below describes the underlying interface; platform offset is no longer editable.

| UI value | Active object | Acer profile byte | NVIDIA interpretation |
| --- | --- | --- | --- |
| Delta | `\\_SB.NPCF.ACBT` | `MxCT` | Configurable TGP offset |
| Dynamic Boost | `\\_SB.NPCF.AMAT` | `MxDB` | Maximum controller output offset / boost allowance |
| Platform power offset | `\\_SB.NPCF.ATPP` | `MxCP` | Target total processing power offset (CPU + GPU budget) |

The original one-target setter derived all three: for targets above 100 W, CT = target - 85 - 15, DB = 15, CP = target - 85. Delta and Dynamic Boost are now independently editable. Platform offset is owned entirely by ACPI: initialization sets every mode's CP byte to 255 and ATPP to 2040 (255 W in 125 mW units); GPU applies reassert those constants. The app no longer stores, sends, or reads a platform-offset parameter. Existing factory registry data is left untouched.

This policy requires interface 0x0201 and matching WMI schema. Reinstall the updated EFI payload and reboot. Methods 11 and 12 now transport only Delta and Dynamic Boost; the app verifies those two active readbacks. Opening, saving, or reading the editor does not apply hardware values. A generous platform offset does not disable other power/thermal policies or guarantee more than the observed sustained ~152.3 W.

ATPP is a real, separate combined CPU/GPU policy input. It is an **offset**, not an absolute platform wattage. The ACPI dump does not establish the driver's underlying TPP reference, so a UI that calls ATPP “200 W CPU + GPU limit” would be misleading. The baseline + Delta + Boost display is only arithmetic based on the calibrated GPU baseline; it does not establish how NVIDIA combines the controls.

## Firmware evidence

Local decompilations under `docs/acpi_tables` were examined:

- DSDT, ERM4 field declarations near lines 39370–39421: GPU DB/CT/CP are separate 8-bit fields in each mode record. ERM4 begins at 0xFE800400; profile entries begin at offsets A0/A8/B0/B8/C0. GPU fields are at +4/+5/+6.
- DSDT `HIDG`, near lines 40105–40155, copies mode DB/CT/CP to AMAT/ACBT/ATPP with a multiplier of 8. CAUM 0/1/2/3/4 corresponds to Turbo/Performance/Balanced/Quiet/Eco.
- SSDJ repeats this profile transfer near lines 4614–4654. Firmware can therefore reapply profile values after an event; changing only active objects would not persist across that event.
- SSDO `NPCF`, near lines 2557–2592, declares the active controls. Its subfunction 2, near lines 2937–2970, exports ACBT in TGPA (buffer offset 0x05), ATPP in TPPA (0x19), AMAT in MAGA (0x1D), AMIT in MIGA (0x21). Transport values are 125 mW units.
- No 50 W saturation was found in that export/profile-copy path. A one-byte profile can represent 0–255 W; larger values would wrap if written without validation. This representation limit is not a verified hardware operating limit.

NVIDIA's public source independently decodes `ctgpOffsetmW`, `targetTppOffsetmW`, and `maxOutputOffsetmW` as separate fields. Its header identifies TPP as including CPU and GPU, with a separately specified TPP offset. This supports the mapping; it does not prove the exact behavior of the installed Windows driver:

- [NVIDIA parameter definitions](https://github.com/NVIDIA/open-gpu-kernel-modules/blob/main/src/common/sdk/nvidia/inc/ctrl/ctrl0000/ctrl0000system.h)
- [NVIDIA ACPI parameter decoding](https://github.com/NVIDIA/open-gpu-kernel-modules/blob/main/src/nvidia/src/kernel/rmapi/client_resource.c)

## Other power-related policy inputs

| Objects | What the inspected firmware establishes |
| --- | --- |
| DCBT, DTPP, DBDC | Battery-side CTGP/TPP and disable policy, separate from the AC values edited here |
| DBAC | AC Dynamic Boost disable bit: zero enables, one disables; not boost watts |
| AMIT | Minimum allowed output offset exported independently; not additional max boost |
| NVWM | OEM mode/policy signal, set differently in Quiet/Eco versus higher modes |
| WMG0–WMG8, WMC0–WMC7 | GPU/CPU thermal step tables exported to NVIDIA; WMG3 is one transition, not the entire thermal policy |
| GIDX / CIDX, GTBT / CTBT | GPU/CPU temperature state with hysteresis, reported through NPCF subfunction 5 |
| DROS, LTBL, STBL | Rest-of-system reserve and long/short battery policy overrides exported in subfunction 2 |
| AMAX, ARAT, DMAX, DRAT, TGPM | Values accepted from the driver in subfunction 7; not shown as a writable static platform cap in this method |
| NPCF subfunction 8 | Board/battery-dependent system power/current table; not automatically an AC GPU power knob |
| CPU PPL1/PPL2/PL1T | Separate live CPU power/time-window limits, which can influence a shared platform budget |

Other policy fields are documented, not changed by this feature. WMG3 in the saved SSDO begins at 79°C, while this project's later calibrated default is 81°C. WMG3 changes GIDX state; that alone does not prove it directly removes a fixed number of boost watts.

## The reported 143 W plateau and earlier 150 W result

The user reports that a 175 W VBIOS maximum still yields roughly 143 W, with a few earlier benchmarks sustaining about 150 W. Therefore a single current NVML maximum is not a diagnosis.

The earlier chat “Fix GPU power limit” records 150 W after the 0x0107 change. That implementation used full CT = requested target - 85, DB = 25 W, and CP = CT. At the recorded 175 W request, this reconstructs **CT 90 / DB 25 / CP 90 W**. This is a historical reconstruction from the implementation and reported request, not a saved simultaneous register trace. The reverted setter instead produces **75 / 15 / 90 W** for a 175 W request.

Possible constraints include the independent TPP budget, maximum output allowance, thermal steps, CPU contention, mode resets, or driver policy. The source does not establish which causes 143 W. Do not claim DB is always additive to CT, or that raising VBIOS alone removes the other policies.

To distinguish them, reproduce the historical tuple on the new interface, hold workload/CPU power/cooling constant, then change only one offset at a time. Record the Delta and Dynamic Boost ACPI readbacks before and after the load along with sustained GPU power, CPU power, temperature, clocks and performance-limit reasons. A change that does not increase workload demand cannot establish a power ceiling. No benchmark or new hardware power setting was applied during this implementation.

## Implementation and deployment

- PREYDEV interface **0x0201** uses new methods **11 SetGpuPowerOffsets** and **12 GetGpuPowerOffsets**. A new major version avoids confusion with earlier experimental 0x0104–0x010D interfaces and their method 9/10 layouts.
- Both editable offsets are validated as 0–255 W, applied in one serialized ACPI method, copied to the active mode's GPU profile fields (including Eco), and followed by NPCF notifications 0xC0/0xC1.
- Mode Apply, mode switching, and startup restore use the independent values. Games exposes the same separate Delta and Dynamic Boost boxes and a read-only nominal total. Old total-only game profiles are converted using the calibrated baseline and legacy 15 W split; new saves persist both offsets explicitly.
- Explicit offset writes require the exact loaded interface version and matching schema. Failure does not silently fall back to a different power model.
- Successful UI status requires readback of both active ACPI offsets. This confirms storage, not driver acceptance or actual watts.
- Save persists each mode separately under HKCU\\SOFTWARE\\PreySense\\GpuOffsets. Legacy mode totals are migrated using their old 15 W split, preserving prior requests rather than guessing new values.
- Manual calibration also records raw offsets when the new read method is available, so Reset can restore captured components. Older calibrations without raw offsets fall back to the legacy split.
- `tools/generate_ssdt.ps1` compiles MOF and AML and synchronizes the staging and EFI payload copies. Repeated generation with unchanged MOF is supported.
- Deploy the updated EFI payload and reboot through PreySense, then launch the rebuilt app to register the matching WMI schema. Building alone does not replace the currently loaded ACPI table.
