# Tasks: Fix GPS Service Compose Configuration

**Branch**: `007-fix-gps-compose` | **Date**: 2026-05-21
**Plan**: [plan.md](plan.md) | **Spec**: [spec.md](spec.md)

## Task List

### Task 1: Add GPS_BAUD environment variable to gps-svc

**Priority**: P1
**Spec Ref**: FR-001
**Dependencies**: None

**Description**: Add an `environment` section to the `gps-svc` service in `deploy/docker-compose.yml` with `GPS_BAUD=115200`.

**Acceptance Criteria**:
- The gps-svc service block contains `environment:` with `- GPS_BAUD=115200`
- The environment section is placed after the `devices` block and before the `healthcheck` block
- YAML syntax is valid

---

### Task 2: Fix gps-svc healthcheck endpoint

**Priority**: P1
**Spec Ref**: FR-002
**Dependencies**: None

**Description**: Change the gps-svc healthcheck test from `http://localhost:5200/api/status` to `http://localhost:5200/cgi-bin/health`.

**Acceptance Criteria**:
- The healthcheck test line reads: `test: [ "CMD", "curl", "-f", "http://localhost:5200/cgi-bin/health" ]`
- No other healthcheck parameters (interval, timeout, retries, start_period) are changed
- YAML syntax is valid

---

### Task 3: Verify no unintended changes

**Priority**: P2
**Spec Ref**: FR-003
**Dependencies**: Task 1, Task 2

**Description**: Validate that only the gps-svc service was modified and all other services remain unchanged.

**Acceptance Criteria**:
- `git diff` shows changes only in the gps-svc service block
- compass-svc, sdr-svc, and zscout-hw-test service blocks are unchanged
- `docker compose config` (or equivalent syntax check) validates the YAML

---

### Task 4: Build verification

**Priority**: P2
**Spec Ref**: SC-004
**Dependencies**: Task 1, Task 2

**Description**: Run `dotnet build zSCOUT-hw-test.slnx` to verify the project still builds (no compose changes affect the .NET build, but verify as a sanity check).

**Acceptance Criteria**:
- Build completes without errors
