# Tasks: GPS Live Stream and Fix-Based Verdict

**Input**: Design documents from `specs/004-gps-live-stream-fix/`
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Exact file paths included in all descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: New types and services needed by all user stories

- [x] T001 Create `src/ZScout.HwTest.App/Hardware/Gps/GnssFixUpdate.cs` — immutable record for parsed gpsd TPV/SKY data
- [x] T002 Create `src/ZScout.HwTest.App/Hardware/Gps/GpsFixAccumulator.cs` — session-scoped accumulator with Update/BuildSnapshot
- [x] T003 Add `StreamLinesAsync` static method to `src/ZScout.HwTest.App/Hardware/Common/ProcessHelper.cs`
- [x] T004 Create `src/ZScout.HwTest.App/Runs/RunCancellationService.cs` — singleton CTS registry with Register/Cancel/Unregister
- [x] T005 Register `RunCancellationService` as singleton in `src/ZScout.HwTest.App/Program.cs`

**Checkpoint**: Foundation ready — all downstream tasks can proceed

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Orchestrator and run lifecycle changes that all adapter work depends on

**⚠️ CRITICAL**: Adapter changes depend on CancellationToken propagation being wired

- [x] T006 Modify `RunCommandService.StartRunAsync` in `src/ZScout.HwTest.App/Dashboard/Services/RunCommandService.cs` — call `RunCancellationService.Register(runId)`, pass returned CTS token to `orchestrator.ExecuteAsync`
- [x] T007 Modify `RunCommandService.StopRunAsync` in `src/ZScout.HwTest.App/Dashboard/Services/RunCommandService.cs` — call `RunCancellationService.CancelAsync(runId)` before saving Stopped status
- [x] T008 Modify `RunOrchestrator.ExecuteAsync` in `src/ZScout.HwTest.App/Runs/RunOrchestrator.cs` — replace sequential `foreach` with `Task.WhenAll` over adapter probes; unregister CTS after all probes complete
- [x] T009 Update `RunOrchestrator.ExecuteAsync` signature to accept `CancellationToken ct` and pass it to each `ProbeAdapterSafeAsync` call

**Checkpoint**: CancellationToken now flows from Stop button to all adapter probes

---

## Phase 3: User Story 1 — Continuous Live GPS Fix Stream (Priority: P1) 🎯 MVP

**Goal**: GPS adapter streams live fix data to the dashboard in real time until the operator stops the test.

**Independent Test**: Start a run with gpsd running, verify CommandProgress events appear for GPS in the dashboard output log at ≤1 s intervals with lat/lon/alt/time fields. Click Stop to end.

### Implementation for User Story 1

- [x] T010 [US1] Rewrite `GpsAdapter.ProbeAsync` in `src/ZScout.HwTest.App/Hardware/Gps/GpsAdapter.cs`:
  - Step 1: pgrep gpsd check (return Unavailable if not running)
  - Step 2: start `gpspipe -w` loop via `ProcessHelper.StreamLinesAsync`
  - Step 3: for each line, parse JSON via `GnssJsonParser` helper (internal static), call `Update(fix)` on `GpsFixAccumulator`
  - Step 4: call `reportStep("gpspipe -w", formattedSummary, isError: false)` with human-readable fix update string
  - Step 5: on CT cancellation or process exit, build final DiagnosticEnvelope from accumulator
- [x] T011 [US1] Add internal static `GnssJsonParser` class in `src/ZScout.HwTest.App/Hardware/Gps/GnssFixUpdate.cs` — parses TPV and SKY JSON objects using `System.Text.Json.JsonDocument`; returns null for unparseable lines

**Checkpoint**: Dashboard shows live GPS fix updates in the peripheral command log during an active run

---

## Phase 4: User Story 2 — Fix-Based Pass/Fail Verdict (Priority: P2)

**Goal**: After Stop, the test verdict is PASS only if a qualifying fix (non-zero lat/lon/alt/time/sat_count) was captured; FAIL otherwise.

**Independent Test**: Run a session without GPS fix (mode=1 TPVs only) → verify FAIL verdict. Then run with a qualifying fix → verify PASS verdict. Verify Unavailable (gpsd not running) → immediate Unavailable without blocking others.

### Implementation for User Story 2

- [x] T012 [US2] Implement `GpsFixAccumulator.IsQualifying(GnssFixUpdate)` in `src/ZScout.HwTest.App/Hardware/Gps/GpsFixAccumulator.cs` — returns true when TPV has mode≥2, lat≠0, lon≠0, alt≠null, time not null/empty
- [x] T013 [US2] Implement `GpsFixAccumulator.Update(GnssFixUpdate)` in `src/ZScout.HwTest.App/Hardware/Gps/GpsFixAccumulator.cs` — sets FixObtained/BestFix for qualifying TPV updates; tracks LastSkyUpdate for SKY objects; always increments TotalFixUpdates for TPV lines
- [x] T014 [US2] Set `DiagnosticEnvelope.Status` in `GpsAdapter.ProbeAsync` based on accumulator: `Ready` if `FixObtained`, `Degraded` if not (gpsd was running but no fix obtained)
- [x] T015 [US2] Add diagnostic message to `GpsAdapter.ProbeAsync` on Degraded path: "GPS test stopped: no qualifying fix obtained during session (N TPV updates received)"

**Checkpoint**: Verdict correctly assigned PASS/FAIL based on fix presence; Unavailable returned immediately for no-gpsd scenario

---

## Phase 5: User Story 3 — Comprehensive HealthSnapshot Evidence (Priority: P3)

**Goal**: Stored HealthSnapshot contains all 14 GPS fields, with values or null defaults, for complete historical evidence.

**Independent Test**: Inspect `PeripheralEvidence.HealthSnapshot.Values` after any GPS run and verify all 14 keys are present per the JSON schema contract.

### Implementation for User Story 3

- [x] T016 [US3] Implement `GpsFixAccumulator.BuildSnapshot()` in `src/ZScout.HwTest.App/Hardware/Gps/GpsFixAccumulator.cs` — returns `Dictionary<string, object?>` with all 14 keys per `contracts/gps-health-snapshot.schema.json`
- [x] T017 [US3] Update `GpsAdapter.ProbeAsync` to call `accumulator.BuildSnapshot()` and pass the result as `HealthSnapshot.Values` in the final `DiagnosticEnvelope`
- [x] T018 [US3] Ensure `gpsd_running` key is set in the snapshot based on pgrep result (before streaming begins)
- [x] T019 [P] [US3] Keep `GpsAdapter.ReadRawSampleAsync` unchanged — verify it still compiles and returns a NMEA sentence via a separate one-shot `gpspipe -r -n 1 -w` call

**Checkpoint**: All 14 HealthSnapshot fields present; ReadRawSampleAsync unaffected

---

## Phase 6: Tests

**Purpose**: Unit tests for parser and accumulator logic (hardware-independent)

- [x] T020 [P] Create `tests/ZScout.HwTest.App.Tests/Hardware/Gps/GnssFixParserTests.cs` — xUnit tests for `GnssJsonParser`: well-formed TPV, missing fields, mode=1, SKY parsing, malformed JSON, non-TPV class
- [x] T021 [P] Create `tests/ZScout.HwTest.App.Tests/Hardware/Gps/GpsFixAccumulatorTests.cs` — xUnit tests for `GpsFixAccumulator`: no-fix session, qualifying fix, SKY update, BuildSnapshot with/without fix, TotalFixUpdates counter

---

## Phase 7: Polish & Cross-Cutting Concerns

- [x] T022 Update `RunOrchestrator.ProbeAdapterSafeAsync` sample-count extraction to use `total_fix_updates` key (replacing `nmea_sentence_count`) for GPS evidence `SampleCount`
- [x] T023 Verify `.gitignore` covers `bin/` and `obj/` directories (no new patterns needed; existing patterns sufficient)
- [x] T024 Run `dotnet build zSCOUT-hw-test.slnx` — fix all warnings/errors until clean build

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately
- **Phase 2 (Foundational)**: Depends on T004 (RunCancellationService) from Phase 1
- **Phase 3 (US1)**: Depends on T001 (GnssFixUpdate), T002 (GpsFixAccumulator), T003 (ProcessHelper.StreamLinesAsync), and Phase 2
- **Phase 4 (US2)**: Depends on T010-T011 (US1 adapter rewrite) — IsQualifying and Update are called within ProbeAsync
- **Phase 5 (US3)**: Depends on T012-T013 (accumulator methods from US2 phase)
- **Phase 6 (Tests)**: Can start after T001-T002 and T011 exist
- **Phase 7 (Polish)**: Depends on all prior phases

### Parallel Opportunities

- T001 and T002 can run in parallel (different files)
- T003 and T004 can run in parallel (different files)
- T006 and T007 can run in parallel (different methods in same file — coordinate carefully)
- T020 and T021 can run in parallel (different test files)

---

## Implementation Strategy

### MVP (User Story 1 Only)
1. Complete Phase 1 + Phase 2 (foundation and CTS wiring)
2. Complete Phase 3 (GPS streaming rewrite)
3. **Validate**: Dashboard shows live fix updates; Stop terminates stream
4. Then add US2 (verdict logic) and US3 (snapshot)

### Incremental Delivery
1. Phase 1 + Phase 2 → CancellationToken wired
2. Phase 3 → Live GPS dashboard updates (US1 ✓)
3. Phase 4 → Fix-based verdict (US2 ✓)
4. Phase 5 → Rich HealthSnapshot (US3 ✓)
5. Phase 6 → Unit test coverage
6. Phase 7 → Build clean, sample count corrected
