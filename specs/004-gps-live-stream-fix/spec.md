# Feature Specification: GPS Live Stream and Fix-Based Verdict

**Feature Branch**: `004-gps-live-stream-fix`
**Created**: 2026-05-18
**Status**: Draft
**Input**: User description: "GPS Test: Rewrite to stream live GNSS fixes and pass only on confirmed fix with full position data (issue #16)"

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Continuous Live GPS Fix Stream (Priority: P1)

An operator is in the field with a Raspberry Pi CM5 connected to a GPS module. They open the zSCOUT dashboard and start a GPS test. Instead of seeing a one-shot result, the dashboard continuously displays incoming GNSS fix data — latitude, longitude, altitude, UTC time, satellite count, and at least one satellite signal strength — updating in real time. The operator watches the fix quality improve as the module acquires more satellites, then clicks Stop when satisfied.

**Why this priority**: Without a live stream the operator has no way to observe GPS acquisition progress. This is the core behavioral change and delivers the most immediate field value.

**Independent Test**: Can be tested by starting the GPS adapter and verifying that parsed fix fields appear in the dashboard output window at regular intervals before any stop command is issued.

**Acceptance Scenarios**:

1. **Given** the GPS module is powered and gpsd is running, **When** the GPS test is started, **Then** the dashboard output window shows at least one GNSS fix update within 30 seconds, with parsed latitude, longitude, altitude, UTC time, and satellite count fields visible.
2. **Given** the GPS test is running and displaying live updates, **When** the operator clicks Stop, **Then** the live stream halts and a final verdict is displayed.
3. **Given** the GPS test is running, **When** a new gpsd JSON update arrives, **Then** the output window shows the updated field values without requiring a page refresh.

---

### User Story 2 — Fix-Based Pass/Fail Verdict (Priority: P2)

After the operator stops a GPS test, the system evaluates whether a valid GNSS fix was obtained during the session. A PASS is issued only if at least one update contained non-zero latitude, longitude, altitude, UTC time, and at least one satellite. A FAIL is issued if the session ended with no qualifying fix captured.

**Why this priority**: The current NMEA-sentence-only pass criterion is misleading — it can pass on all-zero output. This story ensures the verdict reflects actual GNSS fix quality.

**Independent Test**: Can be tested independently by stopping a session with and without a valid fix present in the captured data, verifying PASS/FAIL is assigned correctly each time.

**Acceptance Scenarios**:

1. **Given** the GPS test ran and at least one fix update with non-zero lat/lon/alt/time/sat_count was captured, **When** the operator stops the test, **Then** the peripheral verdict is PASS.
2. **Given** the GPS test ran but every update had zero or null lat/lon/alt/time fields, **When** the operator stops the test, **Then** the peripheral verdict is FAIL with a message indicating no fix was obtained.
3. **Given** gpsd is not running when the GPS test starts, **When** the adapter is probed, **Then** the result is Unavailable immediately, without blocking other peripheral tests (T024 isolation preserved).

---

### User Story 3 — Comprehensive Fix Evidence in HealthSnapshot (Priority: P3)

After stopping a GPS test, the stored `HealthSnapshot` contains a rich set of GPS fix fields — fix quality, latitude, longitude, altitude, UTC time, satellites used, satellites visible, HDOP, max/min satellite SNR, speed, and total fix update count — populated from the session or set to null/default where not observed.

**Why this priority**: Historical evidence and CI gating rely on structured snapshot data. Richer data makes past runs more useful for diagnostics and regression analysis.

**Independent Test**: Can be tested by inspecting the saved `PeripheralEvidence.HealthSnapshot` for a completed run and verifying all listed fields are present, with correct values or explicit null defaults.

**Acceptance Scenarios**:

1. **Given** a GPS test session that obtained a complete fix, **When** the run completes, **Then** the `HealthSnapshot.Values` dictionary contains all fields listed in §4 of issue #16 with observed values.
2. **Given** a GPS test session that never obtained a fix, **When** the run completes, **Then** the `HealthSnapshot.Values` dictionary contains all expected keys with null or zero defaults, and `fix_obtained` is `false`.
3. **Given** any GPS test run, **When** the `ReadRawSampleAsync` method is called during or after the run, **Then** it returns a valid raw NMEA sentence or `null` without affecting the streaming state.

---

### Edge Cases

- What happens when gpsd is running but the GPS module is not yet outputting data (e.g., cold start)? The stream should remain open with no-fix updates until the operator stops it.
- How does the system handle a `gpspipe` process that exits unexpectedly mid-stream? The adapter must detect process exit and surface a diagnostic message without throwing.
- What if `gpspipe` outputs malformed JSON that cannot be parsed? Individual malformed lines must be skipped and logged; the stream continues.
- What if the test is cancelled via `CancellationToken` mid-stream? The adapter must cleanly terminate `gpspipe`, record the best snapshot seen so far, and return an appropriate `DiagnosticEnvelope`.
- What if both NMEA (raw) and JSON (`gpsd` JSON) modes conflict in field values? The adapter should use a single authoritative source (`gpspipe -w` JSON mode) for parsing to avoid ambiguity.
- What if the operator starts a run, the GPS test begins streaming, and then the run is stopped externally via the REST API? The `CancellationToken` propagation must ensure the gpspipe process is terminated.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The GPS adapter MUST run `gpspipe -w` in a continuous loop until cancelled or stopped, parsing each incoming JSON line for fix fields.
- **FR-002**: The adapter MUST publish each parsed fix update to the live event stream (via `reportStep` callback / `LiveEventPublisher`) so the Blazor Dashboard can display it in real time.
- **FR-003**: The dashboard GPS output window MUST display at minimum: latitude, longitude, altitude, UTC time, satellites used, and at least one satellite SNR value, updated in real time via SignalR.
- **FR-004**: After the test stops, the adapter MUST evaluate `fix_obtained`: true if at least one update contained non-null/non-zero latitude, longitude, altitude, UTC time, and satellites_used > 0; false otherwise.
- **FR-005**: The final `DiagnosticEnvelope.Status` MUST be `Ready` (→ Pass) when `fix_obtained` is true, and `Degraded` (→ Fail) when false. `Unavailable` is returned only when gpsd is not running.
- **FR-006**: The `HealthSnapshot.Values` dictionary MUST include all fields: `gpsd_running`, `fix_obtained`, `fix_quality`, `latitude`, `longitude`, `altitude_m`, `utc_time`, `satellites_used`, `satellites_visible`, `hdop`, `max_snr_db`, `min_snr_db`, `speed_knots`, `total_fix_updates`.
- **FR-007**: The `ReadRawSampleAsync` method MUST continue to function independently, returning a single raw NMEA sentence via a separate one-shot `gpspipe` call, without interfering with any active stream.
- **FR-008**: If gpsd is not running at probe start, the adapter MUST return `DiagnosticEnvelope.Unavailable` immediately without launching any streaming process (T024 isolation preserved).
- **FR-009**: The adapter MUST NOT throw unhandled exceptions. All errors (process crash, JSON parse failure, timeout) must be caught and surfaced as diagnostic messages.
- **FR-010**: The GPS test MUST be independently runnable and a failure MUST NOT prevent other peripheral adapters (SDR, HaLow, Compass) from executing in the same run.

### Key Entities

- **GnssFixUpdate**: A parsed record of one gpsd JSON output line, containing fix mode, lat, lon, alt, utc_time, satellites_used, satellites_visible, hdop, speed, track, and per-satellite PRN/SNR data.
- **GpsFixAccumulator**: Stateful object that tracks whether a qualifying fix has been seen and accumulates max/min SNR and total_fix_updates across the streaming session.
- **HealthSnapshot (GPS-extended)**: The dictionary of GPS evidence fields stored in `PeripheralEvidence.HealthSnapshot.Values` at session end.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Within 30 seconds of starting a GPS test on a module that has a fix, at least one complete fix update (all non-null/non-zero fields) is visible in the dashboard output window.
- **SC-002**: 100% of GPS test sessions that captured at least one qualifying fix are assigned a PASS verdict; 100% of sessions with no qualifying fix are assigned a FAIL verdict.
- **SC-003**: The dashboard GPS output window refreshes with new fix data at least once every 5 seconds while the GPS module is outputting data.
- **SC-004**: The stored `HealthSnapshot` for any GPS test run contains all 14 specified fields; no field is absent (null is an acceptable value where not observed).
- **SC-005**: Other peripheral tests (SDR, HaLow, Compass) complete successfully in the same run even when the GPS adapter returns Unavailable or Fail.
- **SC-006**: `ReadRawSampleAsync` returns a result (NMEA line or null) within 6 seconds under normal operating conditions, regardless of whether a streaming test is in progress.

## Assumptions

- The GPS module (MicoAir MG-A01 via FT232) communicates via `gpsd`, which is already installed and configured on the host and accessible from the Docker container via host network mode.
- `gpspipe -w` (JSON mode) is available and produces `TPV` and `SKY` class JSON objects; NMEA parsing is only a fallback if gpsd JSON is unavailable.
- The operator controls the test duration by clicking Stop in the dashboard; there is no automatic timeout for the streaming phase.
- The Blazor Dashboard uses the existing `CommandProgressReceived` / SignalR event pipeline for live output display; no new SignalR hub methods are required.
- Tests are validated on real hardware before merging; mock/stub tests cover unit-testable logic only (NMEA parsing, fix accumulation, snapshot construction).
- `ReadRawSampleAsync` is used only for periodic health polling, not for the streaming GPS test; these two code paths must not share process state.

## Clarifications

### Session 2026-05-18

- Q: Should the streaming probe use `gpspipe -w` (JSON) or `gpspipe -r` (NMEA) as its primary data source? → A: `gpspipe -w` (JSON mode) is the authoritative source. JSON provides structured, already-parsed fields and avoids manual NMEA tokenisation. NMEA mode is a fallback only if gpsd JSON is unavailable.
- Q: Is there a maximum streaming duration/timeout, or does the test run indefinitely until the operator stops it? → A: The test runs indefinitely until the operator clicks Stop or the run is cancelled via CancellationToken. No automatic timeout applies to the streaming phase.
- Q: Should per-satellite PRN + SNR data be stored in the HealthSnapshot, or only aggregate max/min SNR? → A: Only aggregate max_snr_db and min_snr_db are stored in HealthSnapshot to keep snapshot size bounded. Full per-satellite data (PRN list) may be surfaced in live dashboard output but need not be persisted.
