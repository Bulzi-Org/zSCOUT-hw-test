# Feature Specification: Enhance HalowAdapter with Layer 0-3 MM8108 Hardware Diagnostics

**Feature Branch**: `005-halow-layer-diagnostics`
**Created**: 2026-05-19
**Status**: Draft
**Input**: GitHub Issue #25 — Enhance HalowAdapter with Layer 0-3 MM8108 hardware diagnostics

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Sequential Layer Diagnostics (Priority: P1)

As an operator running the zSCOUT hardware test suite, I want the HaLow adapter to probe all four hardware layers (USB → kernel module → wireless interface → radio health) sequentially, so that I can pinpoint exactly which layer is failing when the MM8108 radio is not working.

**Why this priority**: This is the core feature — without sequential layer testing, the operator cannot isolate hardware failures. The current adapter only checks module load and interface presence, missing USB enumeration and radio health entirely.

**Independent Test**: Can be tested by running a hardware probe and verifying the adapter stops at the first failing layer, returning the correct status and diagnostic messages.

**Acceptance Scenarios**:

1. **Given** the MM8108 USB dongle is not physically connected, **When** ProbeAsync runs, **Then** Layer 0 fails and returns `Unavailable` with message "MM8108 USB device not detected" — Layers 1-3 are not attempted.
2. **Given** the USB dongle is connected but morse_driver is not installed, **When** ProbeAsync runs, **Then** Layer 0 passes, Layer 1 fails with "morse_driver not installed", and status is `Unavailable`.
3. **Given** the module is loaded but no wireless interface is created, **When** ProbeAsync runs, **Then** Layers 0-1 pass, Layer 2 returns `Degraded` with "no wireless interface created".
4. **Given** all layers pass, **When** ProbeAsync runs, **Then** status is `Ready` and HealthSnapshot contains all diagnostic values.

---

### User Story 2 - Comprehensive HealthSnapshot (Priority: P1)

As an operator reviewing diagnostic results, I want the HealthSnapshot to include detailed values from each layer (USB detection, module status, firmware version, interface name, PHY capabilities, health check results), so that I have a complete picture of the radio's state.

**Why this priority**: The expanded snapshot provides the data needed for automated pass/fail verdicts and troubleshooting. Without it, operators must manually run CLI commands to gather the same information.

**Independent Test**: Can be tested by mocking process outputs for each layer and verifying the HealthSnapshot.Values dictionary contains the required 9+ keys with correct values.

**Acceptance Scenarios**:

1. **Given** all layers pass, **When** HealthSnapshot is examined, **Then** it contains keys: `usb_device_found`, `vendor_id`, `module_loaded`, `firmware_loaded`, `firmware_version`, `interface_name`, `phy_name`, `supported_channels`, `health_check_ok`.
2. **Given** Layer 0 fails, **When** HealthSnapshot is examined, **Then** `usb_device_found` is false and downstream keys are null/absent.

---

### User Story 3 - Updated Raw Sample Output (Priority: P2)

As a telemetry consumer, I want ReadRawSampleAsync to return `iw dev <iface> info` output instead of `ip link show`, so that the raw telemetry reflects wireless-specific interface details.

**Why this priority**: Provides more relevant raw data for HaLow monitoring, but is not critical for the diagnostic probe itself.

**Independent Test**: Can be tested by calling ReadRawSampleAsync and verifying the output comes from `iw dev` rather than `ip link show`.

**Acceptance Scenarios**:

1. **Given** a HaLow wireless interface exists, **When** ReadRawSampleAsync is called, **Then** it returns the output of `iw dev <iface> info`.
2. **Given** no interface is discovered, **When** ReadRawSampleAsync is called, **Then** it returns null.

---

### User Story 4 - Optional morse_cli Health Check (Priority: P3)

As an operator with morse_cli installed, I want the adapter to run a radio-level health check when the tool is available, so that I get deeper hardware validation without requiring it as a dependency.

**Why this priority**: Provides additional diagnostic depth but is entirely optional — the adapter must not fail if morse_cli is absent.

**Independent Test**: Can be tested by verifying the adapter gracefully skips Layer 3 health check when morse_cli is not on PATH, and runs it when available.

**Acceptance Scenarios**:

1. **Given** morse_cli is not installed, **When** ProbeAsync reaches Layer 3, **Then** it skips the health check with a note in messages and sets `health_check_ok` to null.
2. **Given** morse_cli is installed, **When** ProbeAsync reaches Layer 3, **Then** it runs the health check and records the result.

---

### Edge Cases

- What happens when USB subsystem commands (`lsusb`) are not available in the container?
  - The adapter falls back to checking `/sys/bus/usb/devices/` directly via `find`.
- What happens when `iw` is not installed?
  - Layer 2 returns `Degraded` with message indicating `iw` is not available.
- What happens when `dmesg` output is empty or permission-denied?
  - Firmware version is set to null; firmware_loaded is determined from module load success only.
- What happens when multiple wireless interfaces exist?
  - The adapter uses the first one that is associated with a Morse Micro PHY.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST check USB enumeration for Morse Micro vendor ID `0x325B` as Layer 0, before any driver checks.
- **FR-002**: System MUST check `lsmod` for morse kernel module (`morse`, `morse_driver`, or `dot11ah`) as Layer 1.
- **FR-003**: System MUST parse recent `dmesg` output for morse firmware load status and extract firmware version if available.
- **FR-004**: System MUST use `iw dev` (not `ip link show`) to find the HaLow wireless interface as Layer 2.
- **FR-005**: System MUST use `iw phy` to capture radio capabilities (supported channels, max TX power) in Layer 2.
- **FR-006**: System MUST run `morse_cli health_check` in Layer 3 if the tool is available on PATH; skip gracefully if absent.
- **FR-007**: System MUST stop probing at the first layer that fails and return the appropriate status.
- **FR-008**: System MUST populate HealthSnapshot.Values with: `usb_device_found`, `vendor_id`, `module_loaded`, `firmware_loaded`, `firmware_version`, `interface_name`, `phy_name`, `supported_channels`, `health_check_ok`.
- **FR-009**: System MUST return `iw dev <iface> info` from ReadRawSampleAsync instead of `ip link show`.
- **FR-010**: System MUST report step progress via the `reportStep` callback for each layer's commands.

### Key Entities

- **DiagnosticEnvelope**: Wraps the probe result with status, messages, snapshot, and metadata.
- **HealthSnapshot**: Dictionary of key-value pairs capturing hardware state at each layer.
- **PeripheralStatus**: Enum — Ready (all layers pass), Degraded (partial pass), Unavailable (critical layer fails).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Probe correctly identifies USB device absence within 5 seconds and returns Unavailable.
- **SC-002**: Probe correctly identifies missing kernel module and returns actionable message within 5 seconds.
- **SC-003**: HealthSnapshot contains all 9 required keys after a successful full probe.
- **SC-004**: Probe stops at first failing layer — no unnecessary commands are executed for downstream layers.
- **SC-005**: All existing tests continue to pass; new tests cover each layer individually.
- **SC-006**: Project builds with zero warnings (`dotnet build zSCOUT-hw-test.slnx`).
- **SC-007**: Docker build succeeds (`docker build -f deploy/Dockerfile -t zscout-hw-test .`).

## Assumptions

- The container has access to the host's USB subsystem via bind mounts (`/sys/bus/usb/devices/` and/or `lsusb`).
- The container runs with `network_mode: host` and appropriate privileges to access `/sys` and run `dmesg`.
- `iw` is available or can be added to the Dockerfile's `apt-get install` list.
- `morse_cli` availability is optional and varies by deployment.
- The morse_driver kernel module is installed on the host OS, not inside the container.
- Only one MM8108 device is connected at a time.
