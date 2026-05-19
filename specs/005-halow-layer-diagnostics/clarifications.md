# Clarifications: HalowAdapter Layer 0-3 Diagnostics

**Date**: 2026-05-19
**Mode**: Autonomous (no human in loop)

## Resolved Clarifications

### C1: USB Detection Method
**Question**: Should we use `lsusb` or `/sys/bus/usb/devices/` for Layer 0?
**Resolution**: Use `/sys/bus/usb/devices/` as the primary method (grep for vendor ID `325b`) since it requires no additional tooling. This is more reliable in a container environment where `lsusb` may not be installed. Fall back to `lsusb` if the sysfs path is not accessible.

### C2: Interface Discovery for ReadRawSampleAsync
**Question**: How does ReadRawSampleAsync know which interface to query with `iw dev`?
**Resolution**: Cache the discovered interface name from the most recent ProbeAsync call in a private field. If no interface has been discovered yet, run a quick `iw dev` to find it. Return null if no interface exists.

### C3: Morse Module Name Variants
**Question**: Issue mentions `morse_driver`, `morse`, and `dot11ah` — which to check?
**Resolution**: Check all three variants in `lsmod` output. The actual module name depends on the driver version. The issue specifically mentions these three names.

### C4: dmesg Filtering
**Question**: How much dmesg output to parse? The full kernel ring buffer could be large.
**Resolution**: Use `dmesg --facility=kern --level=info,warn,err` and grep for `morse` to limit output. Also pipe through `tail -n 100` as a safety limit.

## No Remaining Gaps

All aspects of the issue are well-specified. No clarification questions remain.
