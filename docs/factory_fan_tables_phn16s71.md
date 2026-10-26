# PHN16S-71 factory fan table extraction

## Source and verification

Analysis date: 2026-10-26. Source: the existing local
`C:\Windows\Firmware\BIOS_V1.26.fd` (25,147,048 bytes), SHA256
`5ed20cd688e448aac0152aea8a6d2b0d320186ab0552f0455d5a468398e0adba`.
No firmware was flashed or modified.

UEFIExtract A76 identified Acer's firmware modules. Its release ZIP SHA256
`0e723a7c57526e1d872d8caa3ff33f1220b1102844d19ec75ebbfb30fb351621`
matched the digest published by the upstream release. Capstone disassembly of
the extracted x64 modules and disasm51 decoding of the EC supplied the addresses
below. This was local static analysis, not a REA decompiler result.

The ITE/8051 payload begins at file offset `0x1E6B50`. Its identity string is
`ZBJ-KQ  $1.08 $2025/12/29$01$`; the ITE signature appears at payload offset
`0x1000`. The live machine reports `Predator PHN16S-71`, BIOS V1.28, embedded
controller major 1/minor 8. Version agreement supports relevance; it does not
establish byte-for-byte identity with the running EC flash.

## Why GetGamingFanTable returns status 1

The ACPI `WMBH` case `0x13` calls `OSMI(Arg1)` with no input buffer. No fan index
or additional WMI parameter is missing from the diagnostic call.

In extracted `A01WMISmmCallback`, the gaming dispatcher table at RVA `0xF600`
routes method `0x13` to RVA `0xC580`. This handler calls the ODM protocol's
function at offset `0x1F8`. `A01ODMSmmServiceDriver` publishes that protocol at
allocation offset `0x30`; its assignment at RVA `0x1C43` places the function at
allocation offset `0x228`, pointing to RVA `0x233C`:

```text
mov rax, 0x8000000000000003  ; EFI_UNSUPPORTED
ret
```

Set Fan Table uses the same stub (protocol offset `0x1F0`, allocation offset
`0x220`). The WMI handler converts EFI_UNSUPPORTED into output status 1. The
live V1.28 getter returned that status in three elevated samples. This explains
the observed failure; this is not a curve-export interface we can repair by
changing a user-space argument.

## Actual table layout

EC payload offset `0x10190` contains ten tables of 132 bytes: 12 stages of
11 bytes each. The EC loader at payload offset `0x11032` copies one table to
private XRAM `0x0800`. Its code reads ROM address `0x8190`, adds
`tableIndex * 0x84 + stage * 0x0B`, and reads the fields below. Banked 8051 ROM
address `0x8190` here corresponds to payload offset `0x10190`.

| Byte | Meaning |
|---|---|
| 0 | Stage index (0–11) |
| 1 | Transition-counter threshold; FF bypasses the corresponding delay |
| 2 | CPU rising threshold, °C |
| 3 | CPU falling threshold, °C |
| 4 | GPU rising threshold, °C |
| 5 | GPU falling threshold, °C |
| 6–7 | CPU fan target RPM, big-endian |
| 8–9 | GPU fan target RPM, big-endian |
| 10 | Filter-path flag (raw 0/1; not a generic enabled flag) |

The selection routine at payload offset `0x11001` maps EC mode `0/1/2/3/4`
to Turbo/Performance/Balanced/Quiet/Eco. These EC mode codes differ from the
app's WMI mode codes. XRAM `0x054E` bit 0 selects the second group of five
tables when set. The live mapped ERAM sample returned `0x02` at `0x054E`, so
bit 0 was clear and the first group is the applicable selection from this image.
The role of that variant bit beyond table selection has not been established.

The policy is stateful and responds to both CPU and GPU temperatures. The
current row's rising thresholds govern advancement to the *next* stage, whose
RPM targets are then used. Falling thresholds govern retreat. There are also
filtering, transition counters, feedback and override paths. A 127°C entry is
retained as a firmware sentinel; it is not a recommended operating temperature.
Counter values have not been converted to seconds without establishing the
caller cadence.

Balanced (first variant), normal stage transitions:

| Current stage | Advance at CPU or GPU | Next CPU/GPU target RPM |
|---|---|---|
| 0 | CPU ≥56°C or GPU ≥50°C | 1800 / 1850 |
| 1 | CPU ≥65°C or GPU ≥56°C | 1970 / 1980 |
| 2 | CPU ≥72°C or GPU ≥62°C | 2350 / 2350 |
| 3 | CPU ≥78°C or GPU ≥68°C | 2550 / 2550 |
| 4 | CPU ≥84°C or GPU ≥73°C | 3180 / 3170 |
| 5 | CPU ≥89°C or GPU ≥77°C | 3600 / 3600 |

This is a simplified rising-threshold view; transition counters, filtering and
falling thresholds still apply. Live WMI observed approximately 2360/2380 RPM
with CPU 74–82°C and GPU 56°C. That is consistent with the table, but is not
a full validation sweep of every stage or mode.

## Reproduce the export

```powershell
python tools/extract_factory_fans.py C:\Windows\Firmware\BIOS_V1.26.fd <output-directory>
```

The dependency-free decoder verifies the known payload and loader signatures,
then exports all 120 stages to `factory-fan-tables.json` and
`factory-fan-tables.csv`, including raw bytes and source hashes. Unknown layouts
are rejected. No firmware binary is redistributed with the application.

## Live read limits

Read-only captures through the existing inpoutx64 driver succeeded for host
regions `0xFE800400`, `0xFE800500` and `0xFE800600`. The latter matches power
profiles. The firmware's private `0x0800` XRAM is not exposed at the apparent
host counterpart `0xFE800800`: all three samples there returned FF bytes.
No fan targets, EC memory, operating modes or driver configuration were changed.

The app's custom percentage editor cannot reproduce this policy exactly by
dividing RPM by a single maximum: Acer's duty command and EC feedback/override
logic are different interfaces. Firmware Auto executes the factory policy.
The export is an inspectable reference. Its first-variant stage targets now form
the basis of the app's universal per-mode percentage starting presets, with a
20% floor and 100% high-temperature points. This scaling is approximate; it does
not recreate the EC's RPM feedback. Existing saved custom curves and factory
power-calibration defaults are not overwritten.
