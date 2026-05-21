# Feature Specification: SSE GPS Fix Diagnostic Panel

**Feature Branch**: `009-sse-gps-fix-panel`
**Created**: 2026-05-21
**Status**: Draft
**Input**: User description: "Add streaming fix diagnostic panel in dashboard — consume GET /api/stream/fixes via SSE (issue #37)"

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Live GPS Fix Panel on Dashboard (Priority: P1)

An operator opens the zSCOUT dashboard and navigates to the Streams page. A dedicated GPS Fix panel is always visible, continuously showing the latest GPS fix data — status badge (HEALTHY/DEGRADED/UNAVAILABLE), fix mode, latitude, longitude, altitude, speed, track, HDOP, VDOP, fix time, satellites used/visible, max/min SNR, and a "last updated" relative timestamp. The panel updates in real time as new fixes arrive from the gps-svc SSE endpoint, without requiring a test run to be active.

**Why this priority**: This is the core feature — a persistent, always-visible live GPS fix quality view that supplements the per-run GPS status display.

**Independent Test**: Navigate to the Streams page while gps-svc is running and streaming fixes; verify all listed fields update in real time with ≤1 second latency.

**Acceptance Scenarios**:

1. **Given** gps-svc is running and streaming fixes, **When** the operator opens the Streams page, **Then** the GPS Fix panel displays all listed fields updating in real time.
2. **Given** the GPS Fix panel is active, **When** a new fix arrives from gps-svc, **Then** all field values update within 1 second of the fix being emitted.
3. **Given** the panel is displaying fix data, **When** the "Last updated" field is visible, **Then** it shows relative time (e.g., "0.3 s ago") that updates continuously.

---

### User Story 2 — Unavailable State Display (Priority: P2)

When gps-svc is not running or not reachable, the panel clearly shows an UNAVAILABLE status badge and greys out or shows placeholder values for all fields, so the operator immediately understands the GPS service is offline.

**Why this priority**: Operators need to distinguish between "no fix" and "service down" to take the correct corrective action.

**Independent Test**: Stop gps-svc, open the Streams page, and verify the panel shows UNAVAILABLE status with appropriate placeholder values.

**Acceptance Scenarios**:

1. **Given** gps-svc is not running, **When** the operator views the GPS Fix panel, **Then** the status badge shows UNAVAILABLE and all fields show "--" or equivalent placeholder.
2. **Given** gps-svc goes offline while the panel is active, **When** the SSE connection drops, **Then** the panel transitions to UNAVAILABLE status within a reasonable timeout.

---

### User Story 3 — Backend SSE Relay via SignalR (Priority: P1)

A backend hosted service connects to the gps-svc SSE endpoint (`GET /api/stream/fixes`), deserializes incoming `GpsFix` JSON objects, and broadcasts them to connected Blazor clients via the existing SignalR hub. This uses the same relay pattern as the NMEA panel (issue #36).

**Why this priority**: The backend relay is essential infrastructure — without it the Blazor component has no data source.

**Independent Test**: Start the hosted service with gps-svc running; verify SignalR hub emits `GpsFixReceived` events containing valid `GpsFix` data.

**Acceptance Scenarios**:

1. **Given** gps-svc is running, **When** the hosted service starts, **Then** it connects to `/api/stream/fixes` and begins relaying fix objects via SignalR.
2. **Given** the SSE connection drops, **When** the service detects disconnection, **Then** it retries with exponential backoff and resumes relaying when the connection is restored.
3. **Given** the hosted service is relaying fixes, **When** a Blazor client connects to the SignalR hub, **Then** the client receives `GpsFixReceived` events containing deserialized `GpsFix` data.

---

### Edge Cases

- What happens when gps-svc sends malformed JSON in the SSE stream? The service skips the malformed line and continues processing subsequent events.
- What happens when the SSE connection is interrupted mid-stream? The hosted service retries connection with exponential backoff.
- What if no Blazor clients are connected? The hosted service still maintains the SSE connection but SignalR broadcast is a no-op.
- What if the operator navigates away from the Streams page and returns? The component re-subscribes to SignalR and shows the latest fix from the next event.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST include a backend `IHostedService` that connects to the gps-svc SSE endpoint (`GET /api/stream/fixes`) and relays parsed `GpsFix` objects to Blazor clients via SignalR.
- **FR-002**: The hosted service MUST reconnect with exponential backoff when the SSE connection drops or gps-svc is unreachable.
- **FR-003**: The hosted service MUST resolve the gps-svc endpoint from configuration (`Peripherals:Gps:Host` and `Peripherals:Gps:RestPort`), consistent with `GpsAdapter`.
- **FR-004**: A new `GpsFixStreamPanel.razor` component MUST be added to the Streams page displaying all fields from the issue specification: Status, Fix mode, Lat/Lon, Altitude, Speed, Track, HDOP, VDOP, Fix time, Sats used/visible, Max/Min SNR, Last updated.
- **FR-005**: The panel MUST update in real time as new fixes arrive, with ≤1 second latency from GPS to UI.
- **FR-006**: The panel MUST show an UNAVAILABLE status badge with placeholder field values when gps-svc is not reachable.
- **FR-007**: The "Last updated" field MUST display relative time (e.g., "0.3 s ago") that updates continuously.
- **FR-008**: The SignalR hub MUST include a new event type (`GpsFixReceived`) for broadcasting GPS fix data.
- **FR-009**: The system MUST work in both host mode and container mode deployments.
- **FR-010**: The hosted service MUST NOT throw unhandled exceptions; all errors must be caught and logged.

### Key Entities

- **GpsFix**: Existing immutable record in `Hardware/Gps/GpsFix.cs` — the deserialized GPS fix from the SSE stream.
- **GpsFixStreamService**: New `IHostedService` that maintains the SSE connection and relays data.
- **GpsFixStreamPanel**: New Blazor component displaying the live GPS fix data grid.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The GPS Fix panel displays all specified fields and updates within 1 second when gps-svc is streaming.
- **SC-002**: The panel correctly shows UNAVAILABLE when gps-svc is not reachable.
- **SC-003**: The feature works in both host mode and container mode deployments.
- **SC-004**: The backend service reconnects automatically after SSE connection drops.
- **SC-005**: The project builds cleanly with zero warnings (`TreatWarningsAsErrors` enabled).

## Assumptions

- The gps-svc is running as a separate service on the same host or accessible via Docker network, exposing `GET /api/stream/fixes` as an SSE endpoint.
- The existing `GpsFix` record model in `Hardware/Gps/GpsFix.cs` matches the JSON schema from the SSE stream.
- The SSE format uses `data:` prefixed lines with JSON payloads, consistent with the existing SSE parsing in `GpsAdapter`.
- The `GpsFix` record does not include a `vdop` field; VDOP display will show "--" unless the model is extended or the field is available in the JSON.
- The existing SignalR infrastructure (`HardwareStatusHub`, `LiveEventPublisher`) can be extended with new event types without breaking existing functionality.
- The NMEA panel (issue #36) may or may not be implemented yet; this feature uses the same pattern but is independent.

## Clarifications

### Session 2026-05-21

- Q: Should the hosted service run continuously from app startup, or only when clients are connected? → A: Continuously from app startup — the SSE connection is maintained independently of client connections so the latest fix is immediately available when a client connects.
- Q: What reconnection backoff strategy should the hosted service use when gps-svc is unreachable? → A: Exponential backoff starting at 2 seconds, capped at 30 seconds, with jitter. Standard resilience pattern matching existing adapter timeout behavior.
- Q: Should the GpsFixStreamPanel appear at the top of the Streams page above the existing run-based telemetry, or in a separate tab? → A: At the top of the Streams page above the existing run selector, as a standalone card section. This makes it always visible without navigation changes.
- Q: How should the VDOP field be handled since it's not in the current GpsFix model? → A: Display "--" placeholder for VDOP. The field will be shown in the panel layout but will not have data until the GpsFix model is extended in a future PR.
- Q: Should the panel use the LiveEventPublisher .NET event pattern or subscribe directly to SignalR hub events? → A: Use the LiveEventPublisher .NET event pattern for Blazor Server components (same pattern as Control.razor subscribes to RunStatusChanged), since Blazor Server components run in-process and don't need a JS SignalR connection.
