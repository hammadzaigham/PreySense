"""Offline decoder for the verified PHN16S-71 ZBJ-KQ EC 1.08 layout.

Reads an Acer firmware package or extracted EC image. Never accesses hardware.
Unknown firmware layouts are rejected rather than interpreted as fan defaults.
"""
import argparse
import csv
import hashlib
import json
import re
from pathlib import Path


MODES = ("Turbo", "Performance", "Balanced", "Quiet", "Eco")
TABLE_OFFSET = 0x10190
STAGES = 12
STRIDE = 11


def decode(image: bytes) -> dict:
    marker = b"ITE EC-V14.0    \x00QUANTA\x00"
    matches = []
    start = 0
    while (position := image.find(marker, start)) >= 0:
        base = position - 0x1000
        ec = image[base:base + 0x27000] if base >= 0 else b""
        if (ec.startswith(bytes.fromhex("0201000228002222"))
                and ec[0x1104B:0x11051] == bytes.fromhex("908190129594")
                and ec[0x11594:0x1159B] == bytes.fromhex("75f084ef127923")
                and b"ZBJ-KQ  $1.08 $2025/12/29$01$" in ec):
            matches.append((base, ec))
        start = position + len(marker)
    if len(matches) != 1:
        raise ValueError("Expected exactly one verified ZBJ-KQ EC 1.08 payload and table-loader signature.")
    base, ec = matches[0]
    profiles = []
    for variant in range(2):
        for mode_index, name in enumerate(MODES):
            offset = TABLE_OFFSET + (variant * 5 + mode_index) * STAGES * STRIDE
            stages = []
            for index in range(STAGES):
                raw = ec[offset + index * STRIDE:offset + (index + 1) * STRIDE]
                if len(raw) != STRIDE or raw[0] != index or raw[10] not in (0, 1):
                    raise ValueError(f"Invalid stage structure at EC offset {offset + index * STRIDE:#x}.")
                cpu_rpm = int.from_bytes(raw[6:8], "big")
                gpu_rpm = int.from_bytes(raw[8:10], "big")
                if max(raw[2:6]) > 127 or max(cpu_rpm, gpu_rpm) > 7000:
                    raise ValueError("Invalid temperature/RPM data; refusing unsupported layout.")
                stages.append(dict(stage=index, transition_counter_raw=raw[1],
                                   cpu_rise_c=raw[2], cpu_fall_c=raw[3],
                                   gpu_rise_c=raw[4], gpu_fall_c=raw[5],
                                   cpu_target_rpm=cpu_rpm, gpu_target_rpm=gpu_rpm,
                                   filter_flag_raw=raw[10], raw_hex=raw.hex()))
            profiles.append(dict(mode=name, ec_mode_code=mode_index, variant_bit=variant,
                                 ec_image_offset=hex(offset), stages=stages))
    return dict(model="Predator PHN16S-71", ec_version="1.08", ec_build="2025-12-29",
                source_sha256=hashlib.sha256(image).hexdigest(),
                ec_payload_offset=hex(base), decoded_ec_prefix_sha256=hashlib.sha256(ec).hexdigest(),
                provenance="Offline firmware extraction; not a live private-EC-RAM capture.",
                variant_selection="EC XRAM 0x054E bit 0; 0 selects tables 0-4, 1 selects tables 5-9.",
                semantics="Each row is the current stage: rising thresholds advance to the next row; falling thresholds retreat. CPU/GPU conditions are coupled. 127 is retained as a firmware sentinel, not a recommended temperature.",
                profiles=profiles)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("firmware", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    result = decode(args.firmware.read_bytes())
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output / "factory-fan-tables.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    columns = ["mode", "variant_bit", "stage", "cpu_rise_c", "cpu_fall_c", "gpu_rise_c",
               "gpu_fall_c", "cpu_target_rpm", "gpu_target_rpm", "transition_counter_raw",
               "filter_flag_raw", "raw_hex"]
    with (args.output / "factory-fan-tables.csv").open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=columns)
        writer.writeheader()
        for profile in result["profiles"]:
            for stage in profile["stages"]:
                writer.writerow(dict(mode=profile["mode"], variant_bit=profile["variant_bit"], **stage))
    print(f"Decoded {len(result['profiles'])} tables / 120 stages from EC {result['ec_version']}.")


if __name__ == "__main__":
    main()
