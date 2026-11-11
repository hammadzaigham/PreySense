## Bug fixes

- Saving a fan curve keeps the active curve responding to temperature changes. If a save fails, the fan editor shows the error and keeps the draft for retry.
- Custom fan control releases its temperature polling request when the current power mode cannot be read.
- Refresh-rate changes check the live display rate, so a stale selection can no longer prevent a requested change.
- When NVIDIA reports an unknown display route in manual mode, the GPU selector shows N/A and clears the previous selection.
- Lighting restoration on startup and resume respects newer device edits. A completed lighting update also keeps its status message with the selected device.

## Improvements

- Updated the Glass theme for windows and dropdowns and refreshed their backdrops when restoring windows from the tray.
- Improved lighting editor selection and profile restoration for zone keyboards, logos, and other supported devices.
- Added Windows regression coverage for fan control, display readback, and lighting restoration. Each test process now uses its own temporary settings file.
