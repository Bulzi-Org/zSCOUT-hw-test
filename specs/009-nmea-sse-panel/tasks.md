# Tasks: NMEA Diagnostic Stream Panel

**Branch**: `009-nmea-sse-panel` | **Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

## Phase 1: Setup

- [ ] T001 Add NMEA event types to LiveEventPublisher in `src/ZScout.HwTest.App/Streams/LiveEventPublisher.cs`

## Phase 2: Backend Service

- [ ] T002 [US1] Create NmeaStreamService singleton in `src/ZScout.HwTest.App/Streams/NmeaStreamService.cs`
- [ ] T003 [US1] Register NmeaStreamService in DI in `src/ZScout.HwTest.App/Program.cs`

## Phase 3: User Story 1 — Live NMEA Display (P1)

- [ ] T004 [US1] Create NmeaDiagnostics.razor page in `src/ZScout.HwTest.App/Dashboard/Pages/NmeaDiagnostics.razor`
- [ ] T005 [US1] Add NMEA Diagnostics nav item in `src/ZScout.HwTest.App/Dashboard/Shared/MainLayout.razor`

## Phase 4: User Story 2 — Disconnection Handling (P2)

- [ ] T006 [US2] Add connection state tracking and Disconnected badge in `NmeaDiagnostics.razor` and `NmeaStreamService.cs`

## Phase 5: User Story 3 — Buffer Management & Utilities (P3)

- [ ] T007 [US3] Add Clear button and Copy-to-clipboard button in `NmeaDiagnostics.razor`

## Phase 6: Tests & Polish

- [ ] T008 Create NmeaStreamService unit tests in `tests/ZScout.HwTest.App.Tests/NmeaStreamServiceTests.cs`
- [ ] T009 Build verification — run `dotnet build zSCOUT-hw-test.slnx` and fix any errors

## Dependencies

- T001 → T002 (events must exist before service can publish)
- T002 → T004 (service must exist before page can subscribe)
- T003 → T004 (DI registration needed before page can inject)
- T004 → T005, T006, T007 (base page must exist before enhancements)
- T008 can run in parallel after T002

## Summary

- Total tasks: 9
- US1 (P1): 4 tasks (T001–T005)
- US2 (P2): 1 task (T006)
- US3 (P3): 1 task (T007)
- Tests/Polish: 2 tasks (T008–T009)
