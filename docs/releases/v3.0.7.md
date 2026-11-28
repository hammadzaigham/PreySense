# PreySense v3.0.7

## Lighting
- Automatic detection of supported Per-Key Keyboards, Zone Keyboards, Dynamic Bars, logos, and Mode/Turbo Keys.
- Full per-key keyboard editor with click, Ctrl-click, Shift-click, and drag selection.
- Device tabs, automatic color application, and individual LED colors for supported Dynamic Bars.
- Independent Mode Key effects, keyboard synchronization, and customizable performance-mode colors.
- Animation speed controls use device levels instead of percentages.

## GPU tuning
- Separate GPU Overclocking section with an enable switch, core-clock offset, and memory-clock offset controls.
- Expanded Intel host-bridge recognition for hardware tuning.

## Appearance and overlay
- New Glass app theme, with updated controls and subwindow styling.
- Cleaner selection styling and theme-aware mode notifications.
- Dark and White overlay themes, rounded corners, and independent background opacity.
- Text opacity also controls graph opacity.

## Reliability
- First mode change starts immediately; subsequent changes use a 2-second cooldown. RGB restoration remains at 1.55 seconds.
- Improved window show/hide behavior, driver startup recovery, and tuning error handling.
- Fixed lighting settings being overwritten by temporary animation frames or idle dimming.
- Fixed oversized-overlay positioning crashes and skipped session saves on shutdown.
- Removed iGPU Only mode; NVIDIA continues to manage GPU power state.

Lighting availability depends on the model and detected controller.
