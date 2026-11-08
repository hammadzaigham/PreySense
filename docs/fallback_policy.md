# PreySense fallback audit

Audit date: November 7, 2026. No publishing or live tuning writes were performed.

## Removed substitutions

| Area | Result after the change |
| --- | --- |
| CPU package power | Uses RAPL MSRs. Missing reads and initial baseline samples are N/A; there is no PDH substitute or fake zero. |
| CPU temperature | Package thermal status only. An individual core temperature is no longer reported as package temperature. |
| CPU clocks | No PDH substitute, P-core-clock substitute, or wall-time estimate when TSC is unavailable. Explicit per-core sampling requires successful affinity. Unavailable topology is not assumed to be all P-cores. |
| CPU usage | The app uses aggregate Windows CPU counters. Failed reads invalidate the displayed sample rather than reusing it. Legacy aggregate results do not pretend to be Max Core or P/E usage. |
| RAM | A failed OS memory query invalidates the prior sample. |
| Graphs | Missing readings are gaps, not zero. Genuine zero readings remain zero. |
| GPU baseline and nominal power | Removed the guessed 85 W baseline and compiled per-mode GPU wattages. Without a verified baseline, nominal total watts remain N/A; independent captured offsets remain available. |
| GPU thermal targeting | Removed an unused fallback that silently selected the first GPU when no explicit handle was supplied. |
| PROCHOT | An unreadable lock/state disables the switch and box; it is not treated as unlocked. |
| Refresh rate | Failed reads do not become 60 Hz. Failed capability enumeration does not invent 60/120/240 Hz options. |
| Display target | No automatic substitution of another attached display when the primary device cannot be identified. |
| Acer mode | Read failure is unavailable, not Balanced. Unsupported mode/profile indices are rejected instead of silently choosing Balanced. |
| GPU mode display | Starts as N/A until a live state is known, rather than presenting a saved preference as a confirmed hardware state. |
| Power source and battery status | Removed assumed AC power and invented 80% battery readings. Unknown power source pauses source-specific automation and reports N/A. |
| Firmware setting reads | Missing APGE responses are not zero. Unknown USB charging status is N/A; failed setting writes raise an error rather than logging success. Unreadable boot-sound/timeout getters report failure. |
| Driver storage | Missing ProgramData is an explicit initialization error; the driver is not silently relocated to a user profile. |
| Legacy factory paths | Removed unused compiled factory-profile application methods and an unused RGB-effect substitution helper. |
| FPS | Missing, stale or insufficient ETW presentation samples are unavailable, not invented zero FPS. Existing visibility rules can hide an inactive FPS column. |

The overlay now offers CPU Active Clock, CPU Effective Clock and GPU Clock (NVAPI). GPU Effective Clock, Max Core/P/E usage, and separate CPU P/E clocks were removed. Removed saved choices become None; saved GPU Active Clock is renamed Clock. Saved GPU Clock now selects NVAPI as requested. GPU power remains unchanged.

## Retained recovery and initialization

| Mechanism retained | Why it remains |
| --- | --- |
| CPU reference frequency: CPUID, then MSR PLATFORM_INFO | Both read processor metadata needed to interpret APERF/MPERF. The supported Intel MSR path derives frequency from the non-turbo ratio and 100 MHz base clock. If neither source is valid, clocks are unavailable. |
| Group affinity, then legacy affinity for group 0 | Both target the same requested processor. If neither succeeds, that processor sample is unavailable; it does not silently read another core. |
| Existing compatible driver service/device, then the owned bundled service | Avoids competing with an already-running driver. Service ownership is tracked so external services are not removed. Failure reports driver unavailability. |
| Driver service manager/direct service-control API | Alternate ways to start the same driver service, not alternate sensor values or guessed success. |
| Keyboard HID read/write, then write-only open | Lighting writes do not require read access; this allows writes when another program holds read access. Failed opens/writes still fail. |
| Available CPU/GPU temperature for Custom fans | A sleeping or unsupported dGPU may have no temperature. The valid remaining sensor can still drive the saved fan curve. Both missing means no new curve write; previous command state is not presented as a new sensor reading. No automatic change to fan mode was added. |
| Transaction rollback and bounded readiness/readback waits | Preserve or restore hardware state and verify actual writes. Failures remain visible and do not substitute success. Calibration itself has no mode switching or waits. |
| Short sampling caches and counter baselines | Repeated requests within the minimum sample interval reuse a valid sample to prevent unstable delta calculations. A failed refresh invalidates the relevant reading; the first energy/clock interval is N/A. |
| Verified saved GPU metadata and captured JSON factory profiles | These are explicitly saved defaults/metadata, not claims of current live telemetry. Editor defaults fill only unset draft fields, as requested earlier; custom JSON values remain null until Save. |
| Saved preference migration and initial UI settings | Language, theme, window position, lighting colors, Auto fan mode and other desired preferences need initial values from `preysense.json` (or built-in defaults on first run). These are not measured hardware data. Invalid tuning input no longer silently becomes a minimum value. |
| UI-only recovery | English/resource defaults, fonts/colors, screen placement, window activation and WPF visibility checks keep the interface usable. They do not fabricate sensor readings. |
| WMI type/parameter handling | Supports equivalent firmware response encodings and parameter names while reading the actual returned value. A failed request remains a failure. |

Software validation bounds and negative internal unavailable sentinels are not hardware readings. No replacement fallback was added to convert unavailable hardware data into plausible values.

## Verification

Release regression suite: 304 passed, 1 hardware-only test skipped. The final clean-build result is recorded in the accompanying chat. Tests use mocked hardware and do not apply live tuning limits.
