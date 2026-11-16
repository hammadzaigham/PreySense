# LCD overdrive backend

The backend setting applies to the laptop's internal panel through Acer gaming
firmware. It has no monitor identifier and does not control HDMI, DisplayPort,
USB-C or other external displays. External-display overdrive remains a monitor
feature; this protocol does not establish a way to change it.

Settings provides an LCD Overdrive switch above USB Power-Off Charging. It starts
disabled and is enabled only after a defined firmware state is read. A rejected
write or mismatching readback disables the switch until Settings is reopened and
support is checked again. Opening Settings never changes overdrive. There is no
persisted preference or startup override. The backend now
reads firmware instead of returning a false-initialized application cache.
`TryGetLcdOverdrive` reports an unavailable state separately from disabled;
`GetLcdOverdrive` throws when no defined state is available. Writes require a
readable defined state and matching readback; an already selected state is a no-op.

In the inspected BIOS 1.26 firmware, `GetGamingProfile` (WMI method 3, input 0)
places overdrive in output byte six, bits 48–55. Values 0 and 1 are accepted;
FF denotes unsupported in the inspected backend, and other values are treated as
unknown. `SetGamingProfile` (method 1) selects overdrive with input mask 0x10,
using input byte six for its value: 0x1000000000010 on, 0x10 off.

Evidence: A01WMISmmCallback RVAs BAB4–BBF6 read profile selectors and place ODM
selector 6 at output byte six (BBD0–BBD3); B931–B94A writes that selector from
input byte six. A01ODMSmmServiceDriver 29D0–2A19 reads selector 6, returning FF
when EC-mapped AEA is at least 2; otherwise it calls the panel accessor with
index 0D. Setter 4420–45C7 handles selector 6 via the corresponding accessor and
platform state. The inspected image alone does not establish support for every
Acer panel, every firmware revision or a replacement internal panel.

Readback confirms firmware-reported state, not a measured pixel response-time
change. No hardware command was executed while implementing or testing this
change; tests use a simulated command transport.
