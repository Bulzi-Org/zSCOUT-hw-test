# Tasks: HalowAdapter Layer 0-3 Diagnostics

**Feature**: specs/005-halow-layer-diagnostics
**Issue**: #25

## Task Order

Tasks are ordered by dependency. Each task builds on the previous.

### T1: Rewrite HalowAdapter.ProbeAsync with Layer 0-3 pipeline
**File**: `src/ZScout.HwTest.App/Hardware/Halow/HalowAdapter.cs`
**Dependencies**: None
**Description**: Replace the current three-check approach with a sequential 4-layer diagnostic pipeline:
- Layer 0: USB detection via `/sys/bus/usb/devices/` sysfs grep for vendor ID `325b`
- Layer 1: `lsmod` check for morse/morse_driver/dot11ah + `dmesg` firmware parsing
- Layer 2: `iw dev` for wireless interface + `iw phy` for capabilities
- Layer 3: Optional `morse_cli health_check` + `iw dev <iface> info`
- Stop at first failing layer, return appropriate status
- Report each command via `reportStep` callback
- Build comprehensive HealthSnapshot with all 9 keys
- Add `_lastDiscoveredInterface` field for ReadRawSampleAsync

### T2: Update ReadRawSampleAsync
**File**: `src/ZScout.HwTest.App/Hardware/Halow/HalowAdapter.cs`
**Dependencies**: T1
**Description**: Change from `ip link show` to `iw dev <iface> info` using the cached interface name from the most recent probe. If no interface cached, attempt quick `iw dev` discovery.

### T3: Update Dockerfile
**File**: `deploy/Dockerfile`
**Dependencies**: None
**Description**: Add `iw` to the `apt-get install` line in the runtime stage. The `iw` package provides the `iw` command for wireless interface management.

### T4: Create HalowAdapter tests
**File**: `tests/ZScout.HwTest.App.Tests/Hardware/Halow/HalowAdapterTests.cs`
**Dependencies**: T1, T2
**Description**: Create tests following `GpsAdapterReportStepTests` pattern:
- Test probe returns Unavailable when no hardware present (USB not found)
- Test reportStep is called for each layer's commands
- Test null reportStep does not throw
- Test ReadRawSampleAsync returns null when no interface available
- Test HealthSnapshot contains expected keys

### T5: Build verification
**Dependencies**: T1-T4
**Description**: Run `dotnet build zSCOUT-hw-test.slnx` — fix any compiler warnings/errors. Run `dotnet test`. Run `docker build -f deploy/Dockerfile -t zscout-hw-test .`.
