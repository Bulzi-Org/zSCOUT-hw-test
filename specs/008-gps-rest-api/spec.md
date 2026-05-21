# Feature Specification: GPS REST API Migration

**Feature Branch**: `008-gps-rest-api`
**Created**: 2026-05-21
**Status**: Draft
**Input**: Refactor GpsAdapter to consume gps-svc REST API (/api/fix, /api/stream/fixes) instead of direct gpspipe/gpsd

## User Scenarios & Testing

### User Story 1 - GPS Availability Check via REST (Priority: P1)

When a test run begins, the system checks whether the GPS peripheral is available by calling the gps-svc REST API instead of spawning a gpspipe subprocess. An operator sees immediate feedback on GPS service reachability without requiring gpsd-clients installed in the container.

**Why this priority**: Core availability detection — the foundation for all GPS probe functionality. Without this, no GPS testing can occur.

**Independent Test**: Can be fully tested by starting a test run when gps-svc is offline, verifying the adapter returns Unavailable status with appropriate messaging.

**Acceptance Scenarios**:

1. **Given** gps-svc is running and healthy, **When** a test run starts, **Then** GET /api/fix returns a valid response and the adapter proceeds to streaming.
2. **Given** gps-svc is not reachable (down or network error), **When** a test run starts, **Then** the adapter returns Unavailable immediately without spawning any subprocess.
3. **Given** gps-svc returns HTTP 503 (no GPS device), **When** a test run starts, **Then** the adapter returns Unavailable with a descriptive message.

---

### User Story 2 - Live GPS Fix Streaming via SSE (Priority: P1)

During a test run, the system streams live GPS fix data from gps-svc via the SSE endpoint (/api/stream/fixes) instead of reading gpspipe stdout. Fix updates are displayed in real-time on the dashboard.

**Why this priority**: Equal priority with availability — streaming is the core probe mechanism that produces the PASS/FAIL verdict.

**Independent Test**: Can be tested by running a GPS probe with gps-svc active and verifying fix updates appear on the dashboard in real-time.

**Acceptance Scenarios**:

1. **Given** gps-svc is reachable and streaming fixes, **When** the operator starts a test run, **Then** the dashboard displays live fix updates (lat, lon, alt, mode, satellites).
2. **Given** gps-svc is streaming, **When** the operator clicks Stop, **Then** the SSE connection is closed gracefully and the verdict is computed from accumulated fixes.
3. **Given** gps-svc is streaming, **When** a qualifying fix is received (mode≥2, non-zero lat/lon/alt, valid time), **Then** the verdict is PASS (Ready).
4. **Given** gps-svc is streaming but no qualifying fix arrives before the operator stops, **Then** the verdict is FAIL (Degraded).

---

### User Story 3 - Docker Image Cleanup (Priority: P2)

The Docker image no longer installs gpsd-clients since the adapter no longer shells out to gpspipe. This reduces image size and eliminates an unnecessary runtime dependency.

**Why this priority**: Cleanup task — the functional change is complete without this, but it aligns the container dependencies with the new architecture.

**Independent Test**: Build the Docker image and verify gpspipe is not present; confirm GPS probe still works via REST.

**Acceptance Scenarios**:

1. **Given** the updated Dockerfile, **When** the image is built, **Then** gpsd-clients is not installed and gpspipe binary is absent.

---

### Edge Cases

- What happens when the SSE stream disconnects mid-session? The adapter treats accumulated fixes up to that point for the verdict.
- What happens when /api/fix returns 200 but /api/stream/fixes is unavailable? The adapter returns Degraded since the service is reachable but streaming failed.
- What happens when the SSE stream sends malformed JSON lines? They are silently skipped (same resilience as the current gpspipe parser).

## Requirements

### Functional Requirements

- **FR-001**: System MUST check GPS availability via GET /api/fix on the configured host and REST port.
- **FR-002**: System MUST return Unavailable status when GET /api/fix returns 503 or the request fails (network error, timeout).
- **FR-003**: System MUST stream live fix data via GET /api/stream/fixes using SSE (Server-Sent Events), parsing each `data:` line as GpsFix JSON.
- **FR-004**: System MUST determine the PASS/FAIL verdict using the same qualifying-fix criteria (mode≥2, non-zero lat/lon/alt, valid time).
- **FR-005**: System MUST publish each parsed fix update to the dashboard via the existing reportStep callback for real-time display.
- **FR-006**: System MUST gracefully close the SSE connection when the CancellationToken is cancelled (operator Stop).
- **FR-007**: System MUST remove the TCP fallback to gpsd port 2947 — REST API is the sole availability signal.
- **FR-008**: System MUST remove gpsd-clients from the Dockerfile apt install list.
- **FR-009**: System MUST remove the Peripherals:Gps:Port configuration key (gpsd port 2947 is no longer needed).
- **FR-010**: System MUST continue to build the 14-field HealthSnapshot with equivalent data from the GpsFix REST model.
- **FR-011**: System MUST update or replace existing unit tests to cover the REST-based adapter behavior.

### Key Entities

- **GpsFix**: The GPS fix data model from gps-svc REST API, containing latitude, longitude, altitude, mode, time, speed, satellites, and fix quality indicators.
- **GpsAdapter**: The hardware adapter that probes GPS availability and streams fix data, now consuming REST API instead of gpspipe.
- **GpsFixAccumulator**: Session accumulator for fix data — retained but updated to work with GpsFix model instead of GnssFixUpdate.

## Success Criteria

### Measurable Outcomes

- **SC-001**: GPS probe completes successfully using only HTTP calls — no subprocess spawned.
- **SC-002**: Live fix updates appear on the dashboard during a test run with sub-second latency from gps-svc.
- **SC-003**: PASS/FAIL verdict matches the same qualifying-fix criteria as the previous implementation.
- **SC-004**: Docker image size decreases due to removal of gpsd-clients package.
- **SC-005**: All existing GPS-related tests pass or are updated to reflect the new REST-based behavior.

## Assumptions

- gps-svc REST API (/api/fix, /api/stream/fixes) is available and stable (merged via zSCOUT-gps-svc PR #18 and #19).
- The GpsFix model from zSCOUT-common is available or can be replicated locally for deserialization.
- gps-svc SSE stream uses standard `text/event-stream` format with `data:` prefixed JSON lines.
- The dashboard's existing reportStep callback mechanism is sufficient for displaying REST-sourced fix data.
- Network connectivity between hw-test container and gps-svc is available via host networking (docker-compose.yml).
