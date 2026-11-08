# GPU power limits and independent offsets

The current driver path uses `AcerProfileApplyTransaction`. It switches to an alternate Acer mode, stages the selected mode's ERM4 profile while inactive, verifies the bytes, then reactivates the selected mode. CPU limits are written after that transition.

The profile's first byte contains Dynamic Boost and its second byte contains Delta. The third platform-offset byte is preserved by the normal tuning path. User controls accept Delta in 5 W steps from 0 to 50 W and Dynamic Boost values 0, 10, 15, 20, and 25 W.

Nominal GPU watts are the detected NVIDIA default limit plus Delta and Dynamic Boost. This is a reference total; sustained draw and enforced limits remain subject to NVIDIA driver and firmware policy. GPU power telemetry remains on the existing NVML source.

The GPU transaction verifies mode and byte readbacks and attempts to restore the original profile and mode on failure. A successful profile readback does not prove a particular sustained power draw.

Factory profiles are captured through driver reads and stored separately under `Tuning.FactoryDefaults` in `preysense.json`. Optional user tuning values remain null until supplied and explicitly saved. GPU thermal-trip editing and injected ACPI controls are removed.

See [driver tuning](driver_tuning.md) and [CPU limits](cpu_power_limits_driver.md).
