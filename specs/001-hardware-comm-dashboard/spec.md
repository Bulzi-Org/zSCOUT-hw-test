# Feature Specification: Hardware Communication Dashboard

**Feature Branch**: `[002-hw-comm-test]`  
**Created**: 2026-05-09  
**Status**: Draft  
**Input**: User description: "Build a hardware communication test suite and Blazor Server dashboard that verifies all peripherals on the CM5 + CM5-IO-BASE-B carrier board (MicoAir MG-A01 GPS via gpsd, Wavelet-Lab uSDR via SoapySDR, Morse Micro MM8108 Wi-Fi HaLow via morse_driver, QMC5883L compass via I2C) can be communicated with from Docker containers running on the zSCOUT base image. Tests must run in both host mode and container mode. The dashboard at http://<cm5-ip>:5000 provides live hardware status, test execution control, test configuration, raw data streams (GPS NMEA, SDR info, HaLow metrics, compass headings), and test history. The project produces a single Docker image for linux-arm64 deployable via GHCR pull, docker save/load, or baked into the SD card image."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Verify Peripheral Connectivity End-to-End (Priority: P1)

As a field engineer, I need a repeatable hardware communication test suite that validates every required peripheral in both host and container execution modes so I can confirm the CM5 device is deployment-ready.

**Why this priority**: Verifying communication with all attached peripherals is the core business objective and the minimum usable outcome.

**Independent Test**: Can be fully tested by running the test suite in host mode and container mode on a prepared CM5 unit and confirming pass/fail status for each required peripheral in both modes.

**Acceptance Scenarios**:

1. **Given** a CM5 system with all supported peripherals connected, **When** the operator runs the full suite in host mode, **Then** the system reports pass/fail results for GPS, SDR, Wi-Fi HaLow, and compass connectivity.
2. **Given** a CM5 system with all supported peripherals connected, **When** the operator runs the full suite in container mode using the project image, **Then** the system reports pass/fail results for GPS, SDR, Wi-Fi HaLow, and compass connectivity.
3. **Given** one peripheral is disconnected or unavailable, **When** the operator runs the suite, **Then** the system marks only the affected peripheral as failed and records a clear failure reason.

---

### User Story 2 - Operate and Observe Tests from Dashboard (Priority: P2)

As a technician, I need a single dashboard endpoint to start tests, choose test settings, and monitor live hardware status so I can diagnose communication issues without command-line interaction.

**Why this priority**: The dashboard reduces operational effort and makes test execution accessible to broader support teams.

**Independent Test**: Can be fully tested by opening the dashboard, launching at least one test run, changing test configuration values, and confirming that live status updates reflect current peripheral state.

**Acceptance Scenarios**:

1. **Given** the dashboard is reachable, **When** the operator starts a test run, **Then** the dashboard shows run state transitions (queued, running, completed, failed).
2. **Given** configurable test parameters are available, **When** the operator updates settings and starts a run, **Then** the run uses the updated settings and displays them with the run record.
3. **Given** peripherals are active, **When** the operator views the dashboard, **Then** live per-peripheral health and communication status is visible without refreshing the page.

---

### User Story 3 - Review Raw Streams and Historical Results (Priority: P3)

As a support analyst, I need access to raw telemetry streams and historical test outcomes so I can investigate failures and compare behavior over time.

**Why this priority**: Historical and raw data are key for troubleshooting, but the product still delivers core value without deep history features in the first increment.

**Independent Test**: Can be fully tested by viewing live raw streams during an active run and then retrieving prior run records with status and timestamps.

**Acceptance Scenarios**:

1. **Given** a test run is in progress, **When** the operator opens raw stream views, **Then** the dashboard shows current GPS NMEA messages, SDR device information, HaLow metrics, and compass heading updates.
2. **Given** previous test runs exist, **When** the operator opens test history, **Then** the system lists prior runs with timestamp, mode, per-peripheral outcomes, and overall result.

### Edge Cases

- A peripheral intermittently responds during a run and alternates between available and unavailable states.
- Host mode passes while container mode fails for the same peripheral due to runtime access differences.
- Multiple operators request test execution at nearly the same time.
- Raw telemetry produces malformed or partial payloads.
- The dashboard becomes temporarily unreachable during an active test run and then reconnects.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST execute a hardware communication test suite that includes GPS, SDR, Wi-Fi HaLow, and compass peripherals on CM5 + CM5-IO-BASE-B hardware.
- **FR-002**: The system MUST support executing the same logical test suite in both host mode and container mode.
- **FR-003**: The system MUST report per-peripheral test outcome (pass/fail) and a human-readable failure reason for each failed check.
- **FR-004**: The system MUST provide a dashboard reachable at port 5000 on the CM5 device for test operation and status monitoring.
- **FR-005**: Users MUST be able to start, stop, and re-run tests from the dashboard.
- **FR-006**: Users MUST be able to configure test execution parameters before starting a run.
- **FR-007**: The dashboard MUST display live hardware communication status for each supported peripheral during test execution.
- **FR-008**: The dashboard MUST expose live raw data views for GPS messages, SDR information, HaLow metrics, and compass headings.
- **FR-009**: The system MUST persist test run history including execution mode, timestamps, per-peripheral outcomes, and overall result.
- **FR-010**: The project MUST produce one deployable linux-arm64 container image that supports the full feature set.
- **FR-011**: The produced image MUST be distributable via registry pull, offline save/load workflow, and inclusion in SD card imaging workflows.
- **FR-012**: The system MUST clearly indicate when a requested test run cannot start and provide the reason (for example, hardware unavailable or another run already active).

### Key Entities *(include if feature involves data)*

- **Peripheral Profile**: Represents each supported hardware component, including identifier, expected communication path, live status, and latest diagnostic message.
- **Test Run**: Represents one execution instance of the communication suite, including mode, start/end timestamps, operator-selected configuration, and aggregate status.
- **Peripheral Result**: Represents the outcome of one peripheral check within a test run, including pass/fail state, evidence summary, and failure reason when applicable.
- **Telemetry Stream Record**: Represents time-ordered raw data samples captured during runs for each stream type (GPS, SDR, HaLow, compass).
- **Image Distribution Artifact**: Represents metadata about the produced deployable image and supported distribution methods.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of required peripherals are included in every full-suite run in both host mode and container mode.
- **SC-002**: In validation testing, at least 95% of full-suite runs complete with a final result in 5 minutes or less under normal operating conditions.
- **SC-003**: At least 95% of dashboard-initiated runs show first visible status feedback to the operator within 3 seconds.
- **SC-004**: At least 90% of evaluated operators can complete a full test run from the dashboard without command-line assistance on their first attempt.
- **SC-005**: 100% of completed runs are retrievable in test history with mode, timestamp, per-peripheral outcomes, and overall result.
- **SC-006**: The same produced image can be successfully deployed through all three target distribution paths (registry pull, offline transfer, SD card image integration).

## Assumptions

- Each target CM5 test unit is provisioned with the expected carrier board and listed peripherals before running the suite.
- The deployment environment allows access to required hardware interfaces from both host and container execution contexts.
- Only one active full-suite execution is required at a time per device; additional requests can be queued or rejected with a clear message.
- Dashboard users are on a reachable network segment and can access the CM5 device by IP and port 5000.
- Initial release scope focuses on communication verification and observability, not long-term fleet analytics across many devices.