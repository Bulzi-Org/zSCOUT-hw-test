# Implementation Plan: Fix GPS Service Compose Configuration

**Branch**: `007-fix-gps-compose` | **Date**: 2026-05-21 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/007-fix-gps-compose/spec.md`

## Summary

Fix two configuration bugs in `deploy/docker-compose.yml` for the `gps-svc` service:
1. Add `GPS_BAUD=115200` environment variable to match the u-blox module's persistent baud rate after gpsd auto-reconfiguration.
2. Change the healthcheck endpoint from `/api/status` (returns 401) to `/cgi-bin/health` (correct endpoint per upstream PR #23).

## Technical Context

**Language/Version**: N/A (YAML configuration change only)
**Primary Dependencies**: Docker Compose, gps-svc image (ghcr.io/bulzi-org/zscout-gps-svc:latest)
**Storage**: N/A
**Testing**: Manual deployment verification on CM5; `docker compose config` for syntax validation
**Target Platform**: Raspberry Pi CM5, linux-arm64, Docker
**Project Type**: Docker Compose service configuration
**Constraints**: Changes must be limited to `gps-svc` service block; no other services affected

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- ✅ **Docker Parity**: Changes maintain container mode compatibility with correct device communication
- ✅ **Hardware-First**: GPS_BAUD=115200 matches actual u-blox hardware state after gpsd auto-configuration
- ✅ **Minimal Dependencies**: No new dependencies introduced
- ✅ **Isolation**: Only gps-svc service modified; other services unaffected

## Project Structure

### Source Code Changes

```text
deploy/
└── docker-compose.yml   # Two changes in gps-svc service block
```

## Changes Detail

### Change 1: Add GPS_BAUD environment variable (FR-001)

Add an `environment` section to the `gps-svc` service with `GPS_BAUD=115200`. This overrides the image default of 9600 to match the u-blox module's persistent 115200 baud UBX binary mode.

**Location**: `deploy/docker-compose.yml`, gps-svc service, after `devices` block (line ~11)

### Change 2: Fix healthcheck endpoint (FR-002)

Change the healthcheck test command from:
- `curl -f http://localhost:5200/api/status` (broken, returns 401)

To:
- `curl -f http://localhost:5200/cgi-bin/health` (correct endpoint)

**Location**: `deploy/docker-compose.yml`, gps-svc service, healthcheck.test (line 13)

## Complexity Tracking

No constitution violations. This is a minimal two-line configuration fix.
