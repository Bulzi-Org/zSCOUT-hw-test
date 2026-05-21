# Feature Specification: NMEA Diagnostic Stream Panel

**Feature Branch**: `009-nmea-sse-panel`
**Created**: 2026-05-21
**Status**: Draft
**Input**: User description: "Add NMEA diagnostic stream panel in dashboard — consume GET /api/stream/nmea via SSE"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - View Live NMEA Sentences (Priority: P1)

An operator opens the NMEA Diagnostics panel in the dashboard to see raw NMEA sentences streaming in real time from the GPS module. This allows them to verify the GPS signal chain is working end-to-end — from hardware through gpsd translation to the API.

**Why this priority**: This is the core purpose of the panel. Without live NMEA display, the feature has no value.

**Independent Test**: Can be fully tested by opening the NMEA panel while gps-svc is running and streaming — NMEA sentences ($GNGGA, $GNRMC, etc.) appear in the scrolling display.

**Acceptance Scenarios**:

1. **Given** gps-svc is running and the GPS module is producing data, **When** the operator opens the NMEA panel, **Then** NMEA sentences appear in real time in a scrolling display area.
2. **Given** the NMEA panel is open and streaming, **When** a new NMEA sentence arrives, **Then** it is appended to the display within 1 second of arriving at the SSE endpoint.

---

### User Story 2 - Graceful Disconnection Handling (Priority: P2)

The operator sees a clear status indicator when the NMEA stream is connected or disconnected. If the SSE connection drops (e.g., gps-svc restarts), the panel shows a "Disconnected" badge instead of silently hanging.

**Why this priority**: Without disconnection feedback, the operator cannot distinguish "no GPS data" from "broken connection."

**Independent Test**: Stop gps-svc while the panel is open — a "Disconnected" indicator appears.

**Acceptance Scenarios**:

1. **Given** the NMEA panel is streaming, **When** gps-svc becomes unavailable, **Then** a "Disconnected" badge is displayed.
2. **Given** the panel shows "Disconnected", **When** gps-svc becomes available again and the operator reconnects, **Then** streaming resumes.

---

### User Story 3 - Buffer Management and Utilities (Priority: P3)

The operator can clear the displayed NMEA output and copy it to the clipboard. The display is capped at a configurable number of lines to prevent memory growth during long observation sessions.

**Why this priority**: Supports usability during extended diagnostic sessions but is not essential for the core viewing function.

**Independent Test**: Let the panel accumulate 300+ sentences — oldest lines are trimmed. Click Clear — display empties. Click Copy — clipboard contains the current buffer.

**Acceptance Scenarios**:

1. **Given** the display buffer exceeds the configured line limit, **When** a new sentence arrives, **Then** the oldest sentence is removed to maintain the cap.
2. **Given** sentences are displayed, **When** the operator clicks "Clear", **Then** all displayed sentences are removed.
3. **Given** sentences are displayed, **When** the operator clicks "Copy", **Then** the current buffer content is copied to the clipboard.

---

### Edge Cases

- What happens when gps-svc is not running when the panel is first opened? → Panel shows "Disconnected" state immediately.
- What happens when NMEA output contains garbled/malformed sentences? → Garbled lines are displayed as-is (this is a diagnostic tool; showing raw output is intentional).
- What happens when the operator navigates away from the panel? → The SSE connection is closed to free resources.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST display raw NMEA sentences received from the gps-svc SSE endpoint in real time.
- **FR-002**: System MUST show a connection status indicator (Connected/Disconnected) reflecting the SSE stream state.
- **FR-003**: System MUST cap the displayed buffer at a configurable maximum number of lines (default 200), removing the oldest lines when exceeded.
- **FR-004**: System MUST provide a "Clear" button that empties the current display buffer.
- **FR-005**: System MUST provide a "Copy to clipboard" button that copies the displayed buffer contents.
- **FR-006**: System MUST close the SSE connection when the operator navigates away from the panel.
- **FR-007**: System MUST label the panel as a diagnostic / read-only view.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Operators can see live NMEA sentences within 2 seconds of opening the panel (when gps-svc is available).
- **SC-002**: Disconnection is indicated within 5 seconds of gps-svc becoming unavailable.
- **SC-003**: Display buffer never exceeds the configured line limit, preventing unbounded memory growth.
- **SC-004**: Panel operates without degrading dashboard responsiveness for other pages.

## Assumptions

- gps-svc is deployed alongside the dashboard (same host network) and exposes GET /api/stream/nmea on its configured REST port.
- The NMEA SSE endpoint uses standard SSE format (lines prefixed with `data:`).
- The default buffer cap of 200 lines is sufficient for typical diagnostic sessions.
- The panel is an additional nav item or section in the existing Streams page, consistent with dashboard navigation patterns.
