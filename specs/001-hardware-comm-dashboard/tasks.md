# Tasks: Hardware Communication Dashboard

**Input**: Design documents from `/specs/001-hardware-comm-dashboard/`  
**Prerequisites**: `plan.md` (required), `spec.md` (required), `research.md`, `data-model.md`, `contracts/`, `quickstart.md`

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Initialize solution, deployment scaffolding, and baseline configuration.

- [X] T001 Create .NET solution and projects in `zSCOUT-hw-test.slnx` (generated with `dotnet new sln --format slnx`), `src/ZScout.HwTest.App/ZScout.HwTest.App.csproj`, `src/ZScout.HwTest.Cli/ZScout.HwTest.Cli.csproj`, and `src/ZScout.HwTest.Contracts/ZScout.HwTest.Contracts.csproj`
- [X] T002 Configure shared build settings and package versions in `Directory.Build.props` and `Directory.Packages.props`
- [X] T003 [P] Add container build and runtime scaffolding in `deploy/Dockerfile` and `deploy/docker-compose.yml`
- [X] T004 [P] Add image distribution helper script in `deploy/export-image.sh`
- [X] T005 [P] Add baseline runtime configuration files in `src/ZScout.HwTest.App/appsettings.json` and `src/ZScout.HwTest.App/appsettings.Production.json`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Build core architecture required by all user stories.

**CRITICAL**: No user story implementation starts until this phase is complete.

- [X] T006 Implement shared domain contracts for runs, peripherals, evidence, verdicts, and export jobs in `src/ZScout.HwTest.Contracts/Models/`
- [X] T007 [P] Implement file-backed persistence abstractions and repositories in `src/ZScout.HwTest.App/Persistence/`
- [X] T008 [P] Implement local authentication service and user store in `src/ZScout.HwTest.App/Auth/LocalAuthService.cs` and `src/ZScout.HwTest.App/Auth/UserStore.cs`
- [X] T009 Implement role-based authorization policies and session middleware wiring in `src/ZScout.HwTest.App/Program.cs` and `src/ZScout.HwTest.App/Auth/AuthorizationPolicies.cs`
- [X] T010 Implement single-active-run guard service in `src/ZScout.HwTest.App/Runs/RunLockService.cs`
- [X] T011 [P] Implement base hardware adapter abstractions and diagnostic envelope types in `src/ZScout.HwTest.App/Hardware/Common/`
- [X] T012 [P] Scaffold API endpoint groups and route registration for auth, runs, peripherals, streams, and exports in `src/ZScout.HwTest.App/Api/`
- [X] T013 Implement machine-readable run result serializer compatible with contract schema in `src/ZScout.HwTest.App/Runs/RunResultSerializer.cs`
- [X] T014 Implement retention policy and pruning background service in `src/ZScout.HwTest.App/Persistence/RetentionPolicy.cs` and `src/ZScout.HwTest.App/Persistence/RetentionPrunerService.cs`
- [X] T015 Implement SignalR hub and live event publisher foundation in `src/ZScout.HwTest.App/Dashboard/Hubs/HardwareStatusHub.cs` and `src/ZScout.HwTest.App/Streams/LiveEventPublisher.cs`

**Checkpoint**: Foundation complete; user stories can proceed.

---

## Phase 3: User Story 1 - Verify Peripheral Connectivity End-to-End (Priority: P1) 🎯 MVP

**Goal**: Execute full hardware communication suite in host and container modes with evidence capture and manual verdict assignment.

**Independent Test**: Run one host-mode and one container-mode full suite on CM5; verify each of four peripherals has evidence plus operator-assigned pass/fail outcome.

- [X] T016 [P] [US1] Implement GPS communication adapter using gpsd tooling in `src/ZScout.HwTest.App/Hardware/Gps/GpsAdapter.cs`
- [X] T017 [P] [US1] Implement uSDR communication adapter using SoapySDR tooling in `src/ZScout.HwTest.App/Hardware/Sdr/SdrAdapter.cs`
- [X] T018 [P] [US1] Implement MM8108 HaLow communication adapter using morse driver checks in `src/ZScout.HwTest.App/Hardware/Halow/HalowAdapter.cs`
- [X] T019 [P] [US1] Implement QMC5883L compass adapter using I2C tooling in `src/ZScout.HwTest.App/Hardware/Compass/CompassAdapter.cs`
- [X] T020 [US1] Implement run orchestrator to execute all peripheral adapters and collect per-peripheral evidence in `src/ZScout.HwTest.App/Runs/RunOrchestrator.cs`
- [X] T021 [US1] Implement manual verdict service with required failure-reason enforcement in `src/ZScout.HwTest.App/Runs/VerdictService.cs`
- [X] T022 [US1] Implement host/container CLI run command with human and JSON outputs in `src/ZScout.HwTest.Cli/Commands/RunCommand.cs`
- [X] T023 [US1] Implement run start/stop/detail/verdict endpoints in `src/ZScout.HwTest.App/Api/RunsEndpoints.cs`
- [X] T024 [US1] Implement dependency-failure isolation to continue unaffected peripherals in `src/ZScout.HwTest.App/Runs/RunOrchestrator.cs`
- [X] T025 [US1] Add CM5 parity smoke workflow script for host vs container suite validation in `scripts/run-parity-smoke.sh`

**Checkpoint**: US1 is independently functional as MVP.

---

## Phase 4: User Story 2 - Operate and Observe Tests from Dashboard (Priority: P2)

**Goal**: Provide authenticated dashboard control surface for execution, configuration, and live status.

**Independent Test**: Log in as operator, configure run options, start/stop a run from browser, and observe live status transitions without page refresh.

- [X] T026 [P] [US2] Implement login/logout UI and session state flow in `src/ZScout.HwTest.App/Dashboard/Pages/Login.razor` and `src/ZScout.HwTest.App/Dashboard/Services/AuthStateService.cs`
- [X] T027 [P] [US2] Implement role-gated run control panel for start/stop/rerun in `src/ZScout.HwTest.App/Dashboard/Pages/Control.razor`
- [X] T028 [US2] Implement configurable test settings UI and persistence binding in `src/ZScout.HwTest.App/Dashboard/Pages/Settings.razor` and `src/ZScout.HwTest.App/Runs/RunConfigurationService.cs`
- [X] T029 [US2] Implement live peripheral health tiles and run-state timeline components via SignalR in `src/ZScout.HwTest.App/Dashboard/Components/PeripheralStatusGrid.razor` and `src/ZScout.HwTest.App/Dashboard/Components/RunLifecyclePanel.razor`
- [X] T030 [US2] Implement auth and peripheral status endpoints used by dashboard state initialization in `src/ZScout.HwTest.App/Api/AuthEndpoints.cs` and `src/ZScout.HwTest.App/Api/PeripheralsEndpoints.cs`
- [X] T031 [US2] Implement overlapping-run rejection UX messaging and role-specific action visibility in `src/ZScout.HwTest.App/Dashboard/Services/RunCommandService.cs` and `src/ZScout.HwTest.App/Dashboard/Shared/MainLayout.razor`

**Checkpoint**: US2 is independently functional with authenticated dashboard operations.

---

## Phase 5: User Story 3 - Review Raw Streams and Historical Results (Priority: P3)

**Goal**: Surface raw telemetry streams, run history, and export workflows for diagnostics.

**Independent Test**: During an active run, open each raw stream panel and confirm updates; then view prior runs and export selected retained data window.

- [X] T032 [P] [US3] Implement append-only telemetry stream writer and malformed payload tagging in `src/ZScout.HwTest.App/Streams/TelemetryStreamWriter.cs`
- [X] T033 [P] [US3] Implement raw stream retrieval endpoint with per-stream filtering in `src/ZScout.HwTest.App/Api/StreamsEndpoints.cs`
- [X] T034 [US3] Implement run history query endpoint with mode/date filters and details projection in `src/ZScout.HwTest.App/Api/HistoryEndpoints.cs`
- [X] T035 [US3] Implement dashboard raw stream views for GPS, SDR, HaLow, and compass data in `src/ZScout.HwTest.App/Dashboard/Pages/Streams.razor`
- [X] T036 [US3] Implement dashboard history page with per-run drilldown in `src/ZScout.HwTest.App/Dashboard/Pages/History.razor`
- [X] T037 [US3] Implement export job service and export endpoint for retained run/telemetry data in `src/ZScout.HwTest.App/Persistence/ExportService.cs` and `src/ZScout.HwTest.App/Api/ExportsEndpoints.cs`
- [X] T038 [US3] Enforce 30-day retrieval and export boundaries in `src/ZScout.HwTest.App/Persistence/RetentionPolicy.cs`

**Checkpoint**: US3 is independently functional for diagnostics and export.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final hardening and deployment-readiness work across stories.

- [X] T039 [P] Update deployment and operations documentation for GHCR pull, offline save/load, and SD image bake-in in `README.md` and `deploy/README.md`
- [X] T040 Add structured logging and correlation IDs across run lifecycle and API requests in `src/ZScout.HwTest.App/Program.cs` and `src/ZScout.HwTest.App/Runs/`
- [ ] T041 Validate OpenAPI and JSON schema contracts against implemented endpoints/output in `specs/001-hardware-comm-dashboard/contracts/dashboard-api.yaml` and `specs/001-hardware-comm-dashboard/contracts/run-result.schema.json`
- [ ] T042 Execute quickstart end-to-end on CM5 and record evidence with remediation notes in `docs/validation/hw-comm-dashboard.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- Phase 1 (Setup): no dependencies.
- Phase 2 (Foundational): depends on Phase 1 and blocks all user stories.
- Phase 3 (US1): depends on Phase 2.
- Phase 4 (US2): depends on Phase 2; can run in parallel with Phase 3 after foundation, but MVP path prioritizes US1 first.
- Phase 5 (US3): depends on Phase 2; can run in parallel with Phases 3-4 after foundation.
- Phase 6 (Polish): depends on completion of targeted user stories.

### User Story Dependencies

- **US1 (P1)**: independent after foundational phase; no dependency on US2/US3.
- **US2 (P2)**: independent after foundational phase, integrates with shared run services.
- **US3 (P3)**: independent after foundational phase, integrates with shared persistence and stream services.

### Within Each User Story

- Adapter/data capture tasks before orchestration wiring.
- Service tasks before API wiring.
- API wiring before dashboard UX integration.
- Story checkpoint validation before moving to next priority if following incremental delivery.

## Parallel Opportunities

- Setup parallel tasks: T003, T004, T005.
- Foundational parallel tasks: T007, T008, T011, T012.
- US1 parallel tasks: T016, T017, T018, T019.
- US2 parallel tasks: T026, T027.
- US3 parallel tasks: T032, T033.

## Parallel Example: User Story 1

```bash
# Parallel adapter implementation
T016 src/ZScout.HwTest.App/Hardware/Gps/GpsAdapter.cs
T017 src/ZScout.HwTest.App/Hardware/Sdr/SdrAdapter.cs
T018 src/ZScout.HwTest.App/Hardware/Halow/HalowAdapter.cs
T019 src/ZScout.HwTest.App/Hardware/Compass/CompassAdapter.cs
```

## Parallel Example: User Story 2

```bash
# Parallel UI work
T026 src/ZScout.HwTest.App/Dashboard/Pages/Login.razor
T027 src/ZScout.HwTest.App/Dashboard/Pages/Control.razor
```

## Parallel Example: User Story 3

```bash
# Parallel stream backend tasks
T032 src/ZScout.HwTest.App/Streams/TelemetryStreamWriter.cs
T033 src/ZScout.HwTest.App/Api/StreamsEndpoints.cs
```

## Implementation Strategy

### MVP First (US1 only)

1. Complete Phase 1 and Phase 2.
2. Complete Phase 3 (US1).
3. Validate host/container parity and manual verdict workflow on CM5.
4. Demo/deploy MVP image.

### Incremental Delivery

1. Deliver US1 (MVP parity testing core).
2. Deliver US2 (dashboard operation and live status).
3. Deliver US3 (history, raw streams, export).
4. Finish with Phase 6 hardening and deployment validation.

### Team Parallel Strategy

1. Team completes Setup + Foundational together.
2. Then split by story tracks:
   - Engineer A: US1 hardware adapters + orchestration.
   - Engineer B: US2 dashboard/auth UX.
   - Engineer C: US3 telemetry/history/export.
