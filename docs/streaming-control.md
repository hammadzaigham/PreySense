# Streaming controls

Streaming control starts whenever PreySense runs. The pipe accepts the PreySense account and local SYSTEM for Windows service hosts. It denies network logons.

## RubyLight and Moonlight

### Native couch menu preview

Run the modified RubyLight preview under the same Windows account as PreySense, or as the local SYSTEM account used by the RubyLight service. In RubyLight, give the paired Moonlight device **Host control** and enable its couch menu. Open it with **L3 + R3** or hold the upper-left corner in a touch mode that sends pointer input. Select **Acer Performance**.

## Acer Performance

- **Performance Mode** selects a saved mode. Choose Eco, Quiet, Balanced, Performance, or Turbo.
- **Fan Control** offers Auto, Custom, and Max. Custom follows the active performance mode's saved PreySense fan curve.

The panel shows battery charge and power source, plus CPU and GPU utilization, temperature, wattage, clock speed, and fan RPM.

The preview uses RubyLight's existing direct-encoder couch-menu path. If the submenu says PreySense is unavailable, check that PreySense is running and verify the Windows account running the RubyLight host. Offline or unsupported readings and controls may be absent.
