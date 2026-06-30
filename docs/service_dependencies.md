# Acer Service Dependencies & Resource Footprint

This guide documents the relationship between PreySense and the original Acer services, including their memory footprints, CPU usage, and battery life impact.

## Required Services & Drivers for PreySense

PreySense replaces the heavy PredatorSense application but still relies on these lightweight background Acer components:

### User-Mode Services
| Service Name | Executable | Purpose | Required? |
| --- | --- | --- | --- |
| **AcerLightingService** | `AcerLightingService.exe` | Coordinates keyboard RGB and logo lighting. | **Yes** (For keyboard backlight/RGB controls) |
| **AcerServiceSvc** | `AcerServiceWrapper.exe` | General Acer service orchestrator. | **Yes** (Required by `AcerLightingService` to communicate with the hardware) |
| **AcerQAAgentSvis** | `AcerQAAgent.exe` | Runs a local WebSocket server (`wss://localhost:5141`) to notify of hardware buttons. | **Optional** (Only needed if you use the physical Mode/Turbo key. If disabled, you can use PreySense UI or custom shortcuts instead.) |

### Kernel-Mode Drivers
| Driver Name | Driver File | Purpose | Required? |
| --- | --- | --- | --- |
| **AcerApplicationBaseDriver_Device** | `AcerApplicationBaseDriver.sys` | Bridges user-mode software with physical motherboard chips, Embedded Controller (EC), and ACPI. | **Yes** (Essential for all hardware commands, fan adjustments, and telemetry) |

#### What does `AcerApplicationBaseDriver_Device` do?
* **Hardware-to-Software Bridge**: It acts as the direct translator between user-mode applications (like PreySense, Acer Care Center, or Quick Access) and the physical motherboard chips, firmware, and BIOS.
* **Enables WMI Controls**: The WMI classes and methods that PreySense calls to get CPU/GPU temperatures, adjust fan speeds, limit the battery charge, and switch the GPU MUX require this base driver to actually write to and read from the motherboard registers.
* **Proprietary Acer APIs**: It exposes the custom APIs that Acer's user-mode services use to control keyboard firmware and advanced power modes. Without it, WMI controls for fans, battery limits, and profiles would fail.

---

## Resource Footprint & System Impact

Keeping *only* the two required services running while uninstalling/disabling the rest of the PredatorSense suite results in significant resource savings:

### 1. Memory Footprint (RAM)
* **AcerLightingService**: ~5 MB to 15 MB.
* **AcerServiceSvc**: ~10 MB to 20 MB.
* **PreySense App**: ~15 MB to 30 MB.
* **Total Active Footprint**: **~30 MB to 65 MB** (down from ~40 MB to 85 MB when `AcerQAAgent` is running).
* *Comparison*: The full PredatorSense suite (including UI, agent, central service, and hardware service helper processes) frequently consumes **150 MB to 300+ MB** of RAM.

### 2. CPU Usage
* **AcerLightingService & AcerServiceSvc**: **0% at idle**. They only wake up briefly (less than 1% CPU for a fraction of a second) when you apply a new RGB profile, color, or brightness.
* *Comparison*: PredatorSense background services actively poll hardware telemetry (temperatures, fan speeds, system usage) multiple times per second, leading to constant CPU thread wakeups.

### 3. Battery Life Impact
* **Acer Services & PreySense**: **Virtually Zero**. Because they stay in a suspended/idle state and do not poll, they do not trigger CPU C-state wakeups (which prevent the CPU from entering low-power states).
* *Comparison*: PredatorSense's active background polling prevents the CPU from downclocking to its deepest sleep states (C7/C8/C10), causing a noticeable drain on battery life when running unplugged. Using PreySense with just these minimal services keeps your laptop running cooler and extends battery runtime.


