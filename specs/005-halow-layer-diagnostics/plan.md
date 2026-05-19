# Implementation Plan: HalowAdapter Layer 0-3 Diagnostics

**Feature**: specs/005-halow-layer-diagnostics
**Issue**: #25
**Date**: 2026-05-19

## Current State

The `HalowAdapter.ProbeAsync` currently performs three checks:
1. `lsmod` for morse kernel module
2. `ip link show` for network interface
3. `find /sys/bus/sdio/devices` for sysfs device

This misses USB enumeration (Layer 0), firmware load verification, proper wireless interface detection via `iw`, radio capabilities, and `morse_cli` health checks.

## Approach

Rewrite `HalowAdapter.ProbeAsync` as a sequential 4-layer diagnostic pipeline. Each layer depends on the previous — if a layer fails, subsequent layers are skipped and an appropriate status is returned immediately.

### Layer 0 — USB Device Enumeration
- Check `/sys/bus/usb/devices/*/idVendor` for Morse Micro vendor ID `325b`
- Use `find` + `grep` on sysfs (no external tooling required)
- If not found: return `Unavailable` with "MM8108 USB device not detected"
- Populate: `usb_device_found`, `vendor_id`

### Layer 1 — Kernel Module + Firmware
- Check `lsmod` for `morse`, `morse_driver`, or `dot11ah` (existing logic, expanded)
- Parse `dmesg` output filtered for `morse` to find firmware load messages
- Extract firmware version from dmesg patterns like `firmware version: X.Y.Z` or `mm8108b2-rl.bin`
- If module not loaded: return `Unavailable` with "morse_driver not installed" (distinguish from USB not found)
- Populate: `module_loaded`, `firmware_loaded`, `firmware_version`

### Layer 2 — Wireless Interface
- Use `iw dev` to list wireless interfaces (replaces `ip link show`)
- Use `iw phy` to capture PHY capabilities (channels, TX power)
- If no wireless interface: return `Degraded` with "module loaded but no wireless interface created"
- Populate: `interface_name`, `phy_name`, `supported_channels`

### Layer 3 — Radio Health Check
- Check if `morse_cli` exists on PATH via `which morse_cli`
- If available: run `morse_cli health_check` and capture result
- If not available: skip with note in messages, set `health_check_ok` to null
- Also run `iw dev <iface> info` for interface details
- Populate: `health_check_ok`

### ReadRawSampleAsync Update
- Cache the discovered interface name from ProbeAsync in a `_lastDiscoveredInterface` field
- Return `iw dev <iface> info` output if an interface was discovered
- If no interface cached, attempt a quick `iw dev` discovery before returning null

### Dockerfile Update
- Add `iw` and `wireless-tools` to the `apt-get install` list in `deploy/Dockerfile`

### Test Strategy
- Create `tests/ZScout.HwTest.App.Tests/Hardware/Halow/HalowAdapterTests.cs`
- Test each layer's failure scenario (the adapter calls external processes, but we can test the logic paths by running in an environment without the hardware — all paths should return valid DiagnosticEnvelopes)
- Test that reportStep is called for each command
- Test ReadRawSampleAsync behavior
- Follow the pattern from `GpsAdapterReportStepTests.cs`

## Files Modified

- `src/ZScout.HwTest.App/Hardware/Halow/HalowAdapter.cs` — Main rewrite
- `deploy/Dockerfile` — Add `iw` package
- `tests/ZScout.HwTest.App.Tests/Hardware/Halow/HalowAdapterTests.cs` — New test file

## Out of Scope

- batman-adv mesh stack integration
- zSCOUT-mesh container interaction
- Multi-device support (only one MM8108 at a time)
