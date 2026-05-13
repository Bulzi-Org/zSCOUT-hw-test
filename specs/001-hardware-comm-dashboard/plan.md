# Implementation Plan: Hardware Communication Dashboard

**Branch**: `[002-hw-comm-test]` | **Date**: 2026-05-09 | **Spec**: `/specs/001-hardware-comm-dashboard/spec.md`
**Input**: Feature specification from `/specs/001-hardware-comm-dashboard/spec.md`

## Summary

Build a linux-arm64 .NET 10 solution that runs a hardware communication test suite and Blazor Server dashboard in one Docker image, validating GPS, uSDR, MM8108 HaLow, and QMC5883L peripherals in both host and container modes. The implementation uses independent peripheral adapters, a run orchestrator, local authenticated dashboard workflows, operator-assigned verdicts with evidence capture, and 30-day on-device history/telemetry retention with export.

## Technical Context

**Language/Version**: C# 13 on .NET 10 (LTS)  
**Primary Dependencies**: ASP.NET Core Blazor Server, SignalR, xUnit, built-in `System.Diagnostics.Process` integration to gpsd clients/SoapySDR tools/i2c-tools, Docker Engine runtime integration via process execution  
**Storage**: On-device file-backed persistence (JSON metadata plus append-only stream/event files) retained for 30 days, exportable via dashboard  
**Testing**: xUnit unit/integration tests, contract tests for HTTP endpoints and JSON result schema, on-device host/container smoke tests on CM5 hardware  
**Target Platform**: Raspberry Pi CM5 (linux-arm64), Raspberry Pi OS Lite (Bookworm), Docker privileged containers with host networking  
**Project Type**: Single backend/web application with CLI mode and dashboard in one deployable container image  
**Performance Goals**: 95% of full-suite runs complete <=5 minutes, first dashboard feedback <=3 seconds, live telemetry updates without manual refresh during active runs  
**Constraints**: One active run per device, role-based local auth (viewer/operator/admin), manual operator verdict assignment, continue unaffected peripherals when one dependency fails, minimal dependencies beyond constitution-approved stack  
**Scale/Scope**: Single-device execution scope, four required peripherals, 30-day local history and raw telemetry retention, distribution via GHCR/offline tar/SD-image bake-in

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Phase 0 Gate Review

- **I. Hardware-First Validation**: PASS. Plan requires real CM5 hardware execution for authoritative validation and keeps mock/stub behavior explicitly non-authoritative.
- **II. Docker Parity (NON-NEGOTIABLE)**: PASS. Host and container modes are first-class flows with parity checks and critical mismatch reporting.
- **III. Actionable Diagnostics**: PASS. Peripheral adapters and run results include concrete failure reasons and remediation-oriented evidence fields.
- **IV. Structured Output**: PASS. Contracts include machine-readable JSON schema plus human-readable dashboard/CLI presentation.
- **V. Isolation and Independence**: PASS. Peripheral tests are independently runnable and orchestrator continues unaffected checks.
- **VI. Minimal Dependencies**: PASS. Uses .NET 10 + Blazor + xUnit + native tools already present in zSCOUT image; no heavyweight framework additions.

### Post-Phase 1 Re-Check

- Gate status remains PASS after defining data model, interface contracts, and quickstart workflow.
- No constitution violations introduced by the design artifacts.

## Project Structure

### Documentation (this feature)

```text
specs/001-hardware-comm-dashboard/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── dashboard-api.yaml
│   └── run-result.schema.json
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── ZScout.HwTest.App/
│   ├── Program.cs
│   ├── Api/
│   ├── Auth/
│   ├── Dashboard/
│   ├── Hardware/
│   │   ├── Gps/
│   │   ├── Sdr/
│   │   ├── Halow/
│   │   └── Compass/
│   ├── Runs/
│   ├── Streams/
│   └── Persistence/
├── ZScout.HwTest.Cli/
│   ├── Program.cs
│   └── Commands/
└── ZScout.HwTest.Contracts/
    └── Models/

tests/
├── ZScout.HwTest.Unit/
├── ZScout.HwTest.Integration/
└── ZScout.HwTest.Contract/

deploy/
├── Dockerfile
├── docker-compose.yml
└── export-image.sh
```

**Structure Decision**: Single .NET solution containing one primary app (dashboard + APIs + orchestrator), a CLI entrypoint, and shared contracts. This aligns with constitution requirements for one image, minimal dependencies, and shared behavior across host/container execution.

## Phase 0: Research Results

See `/specs/001-hardware-comm-dashboard/research.md`.

All open technical design choices from this plan are resolved with explicit decisions and alternatives.

## Phase 1: Design & Contracts

- Data model: `/specs/001-hardware-comm-dashboard/data-model.md`
- Interface contracts: `/specs/001-hardware-comm-dashboard/contracts/dashboard-api.yaml`, `/specs/001-hardware-comm-dashboard/contracts/run-result.schema.json`
- Operator quickstart: `/specs/001-hardware-comm-dashboard/quickstart.md`
- Agent context updated: `.github/copilot-instructions.md` now references this plan.

## Complexity Tracking

No constitution violations requiring justification.
