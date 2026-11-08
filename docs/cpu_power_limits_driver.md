# CPU power limits through the bundled driver

The active tuning path uses `CpuDriverRuntime` and `CpuPowerLimitController` to read and write Intel package MSR limits and MCHBAR MMIO limits. It does not install a bootloader or inject an ACPI/WMI interface.

GPU profile activation can reload CPU limits, so each combined tuning operation activates the selected Acer mode and writes CPU limits last. The CPU controller snapshots register state, validates access and lock bits, writes limits, verifies readback, and attempts rollback on failure. A partial GPU/CPU result is reported as a failure rather than complete success.

The CPU backend discovers the supported host bridge and MCHBAR; no laptop-model or BIOS-version name check is used. Driver access and register layout checks still determine support.

First-enable calibration records the dynamic CPU mode limits and time window, GPU offsets, readable CPU boost state, and the OEM Intel **PCCC** platform ceiling without writing custom limits. Factory defaults and optional user profiles are separate JSON records. Unset user values stay null; automatic application uses only complete saved profiles.

See [driver tuning](driver_tuning.md) for enablement, calibration, persistence, and application order.

---

## The 85 W PCCC / IPF clamp

`PCCC()` in the OEM SSDT advertises a capability ceiling. On the 47 W SKU this chassis uses, that dword is **85,000 mW**. Intel IPF (`ipf_cpu.sys`, `ipfsvc`) treats it as the maximum sustained PL1 and can pull Dynamic PL1 back to 85 W even if RAPL was set higher.

Calibration reads that factory ceiling through `IntelPlatformPowerLimit` (Windows ACPI SSDT cache / live firmware table) and stores it as `Tuning.FactoryPcccWatts`. The verified factory value is **85 W**.

### Services that enforce the clamp

Only these installed services are toggled from **Settings → Intel IPF / DTT**:

| Service | Display name |
| :--- | :--- |
| `ipfsvc` | Intel(R) Innovation Platform Framework Service |
| `dptftcs` | Intel(R) Dynamic Tuning Technology Telemetry Service |

Disable them when you want PreySense driver PL1 values above the OEM PCCC ceiling to stick. Acer OEM services and Intel XTU are left alone. Re-enable the switch to restore the OEM services.
