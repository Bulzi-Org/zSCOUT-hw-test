# Feature Specification: Tier 2 Service API Adapter Refactor

**Feature Branch**: `006-tier2-adapter-refactor`
**Created**: 2026-05-19
**Status**: Draft
**Input**: Refactor all hardware adapters to consume Tier 2 service APIs (supersedes #17, #18, #19, #23)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - GPS hardware validation via gps-svc TCP (Priority: P1)

An operator runs the hw-test suite from a Docker container. The GPS adapter connects to the gps-svc container on TCP port 2947 instead of checking for a host gpsd process (which fails in container PID namespaces). The streaming fix session works identically to the current behavior, but sourced from the gps-svc container.

**Why this priority**: GPS is currently broken in container mode (#23 Bug 1 — pgrep fails across PID namespaces). This is the most critical fix because it blocks all container-mode testing.

**Independent Test**: Run hw-test in container mode with gps-svc running on the same host. Verify GPS adapter reports Ready with fix data, or Unavailable with clear message if gps-svc is not running.

**Acceptance Scenarios**:

1. **Given** gps-svc is running on localhost:2947, **When** the GPS adapter probes, **Then** it connects via TCP, streams TPV/SKY data, and returns Ready with a qualifying fix.
2. **Given** gps-svc is NOT running, **When** the GPS adapter probes, **Then** it returns Unavailable with message "gps-svc not reachable on localhost:2947" within 5 seconds.
3. **Given** a run with all four adapters, **When** GPS streaming is active, **Then** other adapter results are saved and verdicts assigned as each completes (not blocked until GPS finishes).

---

### User Story 2 - HaLow two-tier hardware + mesh validation (Priority: P1)

The HaLow adapter validates the MM8108 radio hardware (Tier A: Layers 0-3) using direct sysfs/iw commands (no privileged mode, just read-only /sys), then optionally validates mesh connectivity (Tier B: Layer 4+) via gRPC to the zSCOUT-mesh service if available. Tier A works independently of any mesh container.

**Why this priority**: HaLow validation currently requires --privileged mode. The two-tier approach enables unprivileged hardware checks while also supporting mesh integration when available.

**Independent Test**: Run hw-test with only the MM8108 radio present (no mesh container). Verify Tier A returns hardware health. Then start zSCOUT-mesh and verify Tier B adds mesh connectivity data.

**Acceptance Scenarios**:

1. **Given** MM8108 USB device is present and driver loaded, **When** HaLow adapter probes with no mesh service, **Then** Tier A passes (Ready) and Tier B reports mesh as NotTested.
2. **Given** MM8108 hardware is healthy and zSCOUT-mesh is running on localhost:5102, **When** HaLow adapter probes, **Then** both Tier A and Tier B results are reported with full hardware + mesh health data.
3. **Given** MM8108 USB device is NOT present, **When** HaLow adapter probes, **Then** Tier A fails at Layer 0 and Tier B is not attempted.

---

### User Story 3 - Compass validation via compass-svc gRPC (Priority: P2)

The Compass adapter connects to the compass-svc gRPC service on localhost:5100 instead of directly accessing the I2C bus. It retrieves heading, XYZ magnetometer, temperature, and overflow data through the gRPC API.

**Why this priority**: Eliminates the need for /dev/i2c-* device pass-through and --privileged mode. Dependencies on i2c-tools are removed from the container.

**Independent Test**: Run hw-test with compass-svc running. Verify heading data is received and non-zero values result in a Pass verdict.

**Acceptance Scenarios**:

1. **Given** compass-svc is running on localhost:5100, **When** Compass adapter probes, **Then** it retrieves heading data via gRPC and returns Ready with non-zero magnetometer values.
2. **Given** compass-svc is NOT running, **When** Compass adapter probes, **Then** it returns Unavailable with "compass-svc not reachable on localhost:5100" within 5 seconds.

---

### User Story 4 - SDR validation via sdr-svc gRPC (Priority: P2)

The SDR adapter connects to the sdr-svc gRPC service on localhost:5101 instead of calling SoapySDRUtil directly. It retrieves device status, capabilities, and can perform band sweeps through the gRPC API.

**Why this priority**: Eliminates the need for USB device pass-through. SoapySDRUtil and related tools move into the sdr-svc container.

**Independent Test**: Run hw-test with sdr-svc running. Verify device detection and capability reporting works through the gRPC API.

**Acceptance Scenarios**:

1. **Given** sdr-svc is running on localhost:5101, **When** SDR adapter probes, **Then** it retrieves device status and capabilities via gRPC and returns Ready.
2. **Given** sdr-svc is NOT running, **When** SDR adapter probes, **Then** it returns Unavailable with "sdr-svc not reachable on localhost:5101" within 5 seconds.

---

### User Story 5 - Incremental adapter result processing (Priority: P1)

The run orchestrator processes adapter results as each adapter completes rather than waiting for all adapters to finish via Task.WhenAll. Evidence is saved and verdicts assigned incrementally so that fast adapters (Compass, SDR, HaLow Tier A) deliver results immediately while GPS continues streaming.

**Why this priority**: Fixes #23 Bug 2 where Task.WhenAll blocks all verdict assignment until GPS streaming stops.

**Independent Test**: Start a run with all four adapters. Verify that Compass/SDR/HaLow verdicts appear on the dashboard while GPS is still streaming.

**Acceptance Scenarios**:

1. **Given** all four adapters are running, **When** Compass completes in 2 seconds but GPS is still streaming, **Then** Compass evidence and verdict are saved and published immediately.
2. **Given** a run completes, **When** all adapters have finished, **Then** the overall run outcome is computed and the run transitions to Completed.

---

### User Story 6 - Unprivileged container operation (Priority: P2)

The hw-test container runs without --privileged mode. It requires only --network host and read-only /sys mount (/sys:/sys:ro). Device mounts (/dev) are no longer needed for GPS, Compass, or SDR adapters. HaLow Tier A needs /sys:ro for USB/sysfs enumeration.

**Why this priority**: Security improvement — eliminates unnecessary privilege escalation in production deployments.

**Independent Test**: Build and run the Docker container without --privileged. Verify all adapters function correctly with only --network host and /sys:/sys:ro.

**Acceptance Scenarios**:

1. **Given** the container runs without --privileged, **When** all service containers (gps-svc, compass-svc, sdr-svc) are available, **Then** GPS, Compass, and SDR adapters return valid results.
2. **Given** the container has /sys:/sys:ro mount, **When** HaLow adapter runs Tier A, **Then** USB enumeration and iw commands succeed.

---

### Edge Cases

- What happens when a gRPC service is reachable but returns an error response? → Adapter returns Degraded with the error details.
- What happens when TCP/gRPC connection succeeds but then drops mid-stream? → Adapter captures partial data, returns Degraded with collected samples.
- What happens when HaLow Tier A passes but Tier B gRPC connection times out? → Report hardware Ready, mesh NotTested (not a failure).
- What happens when multiple adapters fail simultaneously? → Each failure is independently recorded; no cascade effects between adapters.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: GPS adapter MUST connect to gps-svc via TCP on configurable host:port (default localhost:2947) instead of using pgrep to check host processes.
- **FR-002**: GPS adapter MUST perform a TCP connection check with 5-second timeout as its availability test.
- **FR-003**: GPS adapter MUST preserve existing gpspipe -w streaming behavior for fix data collection.
- **FR-004**: Compass adapter MUST connect to compass-svc via gRPC on configurable host:port (default localhost:5100).
- **FR-005**: Compass adapter MUST retrieve heading, XYZ magnetometer, temperature, and overflow data via gRPC.
- **FR-006**: SDR adapter MUST connect to sdr-svc via gRPC on configurable host:port (default localhost:5101).
- **FR-007**: SDR adapter MUST retrieve device status, capabilities, and support band sweep operations via gRPC.
- **FR-008**: HaLow adapter MUST implement a two-tier strategy: Tier A (hardware Layers 0-3, always runs) and Tier B (mesh Layer 4+, conditional on zSCOUT-mesh availability).
- **FR-009**: HaLow Tier A MUST check USB enumeration, kernel module, firmware, wireless interface, and PHY capabilities using read-only /sys access and standard Linux tools (grep, lsmod, iw).
- **FR-010**: HaLow Tier B MUST connect to mesh service via gRPC on configurable host:port (default localhost:5102) and report mesh association, peers, and connectivity.
- **FR-011**: HaLow adapter MUST skip Tier B without failure when mesh service is unavailable, reporting mesh status as NotTested.
- **FR-012**: RunOrchestrator MUST process adapter results incrementally as each completes, saving evidence and publishing status without waiting for all adapters.
- **FR-013**: All service connection timeouts MUST be configurable via appsettings, defaulting to 5 seconds.
- **FR-014**: Docker container MUST run without --privileged mode, using only --network host and /sys:/sys:ro.
- **FR-015**: Dockerfile MUST include iw, usbutils, kmod for HaLow Tier A and remove i2c-tools.
- **FR-016**: Dockerfile MUST add gRPC client packages (Grpc.Net.Client, Google.Protobuf) as NuGet dependencies.
- **FR-017**: All existing test infrastructure (runs, evidence, verdicts, export) MUST remain unchanged.
- **FR-018**: Each adapter MUST return Unavailable with a descriptive message when its backing service is not reachable, without blocking other adapters.

### Key Entities

- **GpsAdapter**: Connects to gps-svc TCP 2947; streams GNSS fix data; replaces pgrep-based availability check with TCP connect.
- **CompassAdapter**: gRPC client to compass-svc :5100; retrieves heading/magnetometer data; replaces I2C bus access.
- **SdrAdapter**: gRPC client to sdr-svc :5101; retrieves device status/capabilities; replaces SoapySDRUtil shell commands.
- **HalowAdapter**: Two-tier: Tier A (direct hardware via /sys + iw, preserved from current) + Tier B (gRPC to mesh :5102, new).
- **RunOrchestrator**: Manages adapter execution with incremental result processing instead of Task.WhenAll.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All four adapters connect to their respective Tier 2 service APIs and return valid diagnostic results when services are available.
- **SC-002**: GPS streaming session produces fix data within 30 seconds of starting when gps-svc is running with a connected receiver.
- **SC-003**: HaLow Tier A hardware checks complete within 10 seconds without requiring --privileged container mode.
- **SC-004**: Adapter results are visible on the dashboard within 2 seconds of each adapter completing, regardless of other adapters' state.
- **SC-005**: Service unavailability is detected and reported within 5 seconds for any adapter.
- **SC-006**: The hw-test container runs successfully without --privileged and without writable /dev mounts.
- **SC-007**: Build completes with zero warnings, all tests pass, and Docker image builds successfully.

## Assumptions

- gps-svc, compass-svc, sdr-svc, and zSCOUT-mesh containers are deployed and managed separately — hw-test only needs network access to them.
- The gRPC service APIs (compass-svc, sdr-svc, mesh) use standard gRPC with protobuf; .proto definitions will be defined in this project for the client side.
- gpspipe TCP streaming to gps-svc on port 2947 uses the same gpsd protocol as the current direct gpsd connection.
- The host networking mode (--network host) provides access to all Tier 2 service ports on localhost.
- HaLow Tier A tools (iw, lsmod, grep) are available in the base Docker image or installed via apt.
- The existing HealthSnapshot dictionary-based approach is sufficient for the expanded HaLow snapshot fields.
