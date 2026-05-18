# Implementation Plan: GPS Live Stream and Fix-Based Verdict

**Branch**: `004-gps-live-stream-fix` | **Date**: 2026-05-18 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/004-gps-live-stream-fix/spec.md`

## Summary

Rewrite `GpsAdapter.ProbeAsync` to stream `gpspipe -w` JSON output continuously until the operator clicks Stop (via `CancellationToken`), parse `TPV` and `SKY` objects in real time, push each fix update via the existing `reportStep` / `LiveEventPublisher` pipeline to the Blazor Dashboard, and return a `DiagnosticEnvelope` with a `Ready`/`Degraded`/`Unavailable` status and a fully-populated 14-field `HealthSnapshot` when the stream ends. Update `RunOrchestrator` to use `Task.WhenAll` (so short-lived adapters can complete while GPS streams), add `RunCancellationService` to propagate the Stop command as a `CancellationToken`, and update `RunCommandService` to cancel the active run's CTS on Stop.

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: Blazor Server, SignalR, xUnit, `System.Text.Json`, `System.Diagnostics.Process`
**Storage**: File-backed `HealthSnapshot` via existing `PeripheralEvidence` persistence
**Testing**: xUnit — unit tests for JSON parsing logic, `GpsFixAccumulator`, and snapshot construction
**Target Platform**: `linux-arm64` (Raspberry Pi CM5); Docker ARM64 multi-stage image
**Project Type**: Blazor Server web application + CLI
**Performance Goals**: Dashboard refresh latency ≤ 1 s per fix update; `ReadRawSampleAsync` ≤ 6 s
**Constraints**: Single .NET runtime dependency; no new NuGet packages; `TreatWarningsAsErrors` stays enabled
**Scale/Scope**: Single-peripheral streaming loop; 14 `HealthSnapshot` fields; ≤ 100 cmd-log entries per peripheral (existing cap)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| I. Hardware-First Validation | ✅ PASS | ProbeAsync still interacts with real gpsd/gpspipe; mock only in unit tests |
| II. Docker Parity | ✅ PASS | gpspipe -w uses existing gpsd socket; same command in host and container modes |
| III. Actionable Diagnostics | ✅ PASS | Unavailable message includes root cause; streaming errors include exit code and stderr |
| IV. Structured Output | ✅ PASS | HealthSnapshot.Values provides machine-readable fields; CommandProgress provides human display |
| V. Isolation and Independence | ✅ PASS | T024: gpsd-not-running → Unavailable immediately; Task.WhenAll prevents GPS blocking others |
| VI. Minimal Dependencies | ✅ PASS | System.Text.Json is .NET 10 BCL; no new packages required |

## Project Structure

### Documentation (this feature)

```text
specs/004-gps-live-stream-fix/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── gps-health-snapshot.schema.json
└── tasks.md             # Phase 2 output (created by /speckit.tasks)
```

### Source Code Changes

```text
src/ZScout.HwTest.App/
├── Hardware/
│   ├── Common/
│   │   └── ProcessHelper.cs              # ADD StreamLinesAsync method
│   └── Gps/
│       ├── GpsAdapter.cs                 # REWRITE ProbeAsync; keep ReadRawSampleAsync
│       ├── GnssFixUpdate.cs              # NEW: parsed gpsd TPV/SKY record
│       └── GpsFixAccumulator.cs          # NEW: stateful session accumulator
├── Runs/
│   ├── RunOrchestrator.cs                # MODIFY: Task.WhenAll + cancellation token
│   ├── RunCancellationService.cs         # NEW: CTS registry per active run
│   └── RunLockService.cs                 # no change
└── Dashboard/
    └── Services/
        └── RunCommandService.cs          # MODIFY: cancel CTS on Stop

tests/ZScout.HwTest.App.Tests/
└── Hardware/
    └── Gps/
        ├── GpsFixAccumulatorTests.cs     # NEW
        └── GnssFixParserTests.cs         # NEW
```

**Structure Decision**: Single Blazor Server project with minimal targeted additions. No new projects, no new NuGet packages.

## Implementation Phases

### Phase 1: Foundation (non-breaking additions)

1. Add `ProcessHelper.StreamLinesAsync` — new static method reading stdout line by line
2. Add `GnssFixUpdate` record — parsed fields from TPV/SKY JSON objects  
3. Add `GpsFixAccumulator` — accumulates session state from a stream of `GnssFixUpdate` values
4. Add `RunCancellationService` — singleton CTS registry; register/cancel/unregister per run
5. Register `RunCancellationService` in DI (`Program.cs`)

### Phase 2: Adapter Rewrite

6. Rewrite `GpsAdapter.ProbeAsync`:
   - Check gpsd running; return Unavailable immediately if not (preserves T024)
   - Start `gpspipe -w` streaming loop via `ProcessHelper.StreamLinesAsync`
   - Parse each line into `GnssFixUpdate`; update `GpsFixAccumulator`; call `reportStep` with formatted summary
   - On cancellation or process exit, build final `DiagnosticEnvelope` from accumulator
7. Keep `ReadRawSampleAsync` unchanged

### Phase 3: Orchestrator and Run Lifecycle

8. Modify `RunOrchestrator.ExecuteAsync` to run adapters via `Task.WhenAll`
9. Modify `RunCommandService.StartRunAsync` to register a CTS via `RunCancellationService`
10. Pass that CTS token to `RunOrchestrator.ExecuteAsync`
11. Modify `RunCommandService.StopRunAsync` to cancel the CTS before saving Stopped status

### Phase 4: Tests

12. Add `GpsFixAccumulatorTests` — unit tests for accumulation logic and snapshot construction
13. Add `GnssFixParserTests` — unit tests for JSON parsing edge cases
