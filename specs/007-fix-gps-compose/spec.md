# Feature Specification: Fix GPS Service Compose Configuration

**Feature Branch**: `007-fix-gps-compose`
**Created**: 2026-05-21
**Status**: Draft
**Input**: User description: "gps-svc compose missing GPS_BAUD override and uses broken healthcheck endpoint"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - GPS Service Starts at Correct Baud Rate (Priority: P1)

As a field operator deploying the zSCOUT hardware test suite on a Raspberry Pi CM5, the GPS service container must start with the correct baud rate (115200) matching the u-blox module's persistent configuration after gpsd auto-upgrade, so the GPS receiver communicates successfully on every container restart.

**Why this priority**: Without the correct baud rate, the GPS service cannot communicate with the receiver at all, making the entire GPS subsystem non-functional after the first gpsd auto-reconfiguration.

**Independent Test**: Deploy the docker-compose stack and verify the gps-svc container starts with GPS_BAUD=115200 by inspecting the container environment. Confirm GPS data flows correctly through the service.

**Acceptance Scenarios**:

1. **Given** the gps-svc container is defined in docker-compose.yml, **When** the stack is deployed, **Then** the GPS_BAUD environment variable is set to 115200.
2. **Given** gpsd has previously auto-configured the u-blox module to 115200 baud, **When** the gps-svc container restarts, **Then** serial communication succeeds immediately without baud rate mismatch.

---

### User Story 2 - GPS Service Healthcheck Succeeds (Priority: P1)

As a field operator, the GPS service healthcheck must use the correct endpoint so Docker can accurately report service health and dependent services (zscout-hw-test) can start reliably.

**Why this priority**: A broken healthcheck prevents dependent services from starting (they wait for `service_healthy` condition), blocking the entire test suite.

**Independent Test**: Deploy the docker-compose stack and verify the gps-svc healthcheck passes by checking `docker inspect` for healthy status. The healthcheck endpoint must return HTTP 200.

**Acceptance Scenarios**:

1. **Given** the gps-svc container is running, **When** Docker executes the healthcheck, **Then** the curl command targets `http://localhost:5200/cgi-bin/health` and receives an HTTP 200 response.
2. **Given** the gps-svc container is running, **When** Docker reports service health, **Then** the service reaches "healthy" status within the configured start_period + (interval × retries).

---

### Edge Cases

- What happens if the GPS receiver is not physically connected? The healthcheck may still pass (service is running) but GPS data will not be available — this is expected behavior and handled by the hw-test adapter layer.
- What happens if the gps-svc image is updated to change the default GPS_BAUD? The explicit environment override in docker-compose.yml ensures the baud rate is always 115200 regardless of image defaults.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The gps-svc service definition in docker-compose.yml MUST include an environment variable `GPS_BAUD=115200` to match the u-blox module's persistent baud rate configuration.
- **FR-002**: The gps-svc healthcheck MUST use the endpoint `http://localhost:5200/cgi-bin/health` instead of the broken `http://localhost:5200/api/status` endpoint.
- **FR-003**: All other gps-svc configuration (image, container_name, restart policy, network_mode, devices, healthcheck timing) MUST remain unchanged.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The gps-svc container starts with GPS_BAUD=115200 visible in its environment.
- **SC-002**: The gps-svc healthcheck passes successfully when the service is running and responsive.
- **SC-003**: The zscout-hw-test container (which depends on gps-svc being healthy) starts without being blocked by a failing GPS healthcheck.
- **SC-004**: No other services in the docker-compose.yml are affected by these changes.

## Assumptions

- The u-blox GPS module retains its 115200 baud UBX binary mode configuration persistently after gpsd auto-reconfiguration.
- The gps-svc image (ghcr.io/bulzi-org/zscout-gps-svc:latest) exposes the `/cgi-bin/health` endpoint for health checks (confirmed by upstream PR #23 merge).
- The gps-svc image accepts GPS_BAUD as an environment variable to override the default baud rate (confirmed by upstream PR #22 merge).
- Only the `deploy/docker-compose.yml` file needs modification; no application code changes are required.
