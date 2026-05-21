# Tasks: SSE GPS Fix Diagnostic Panel

**Feature**: specs/009-sse-gps-fix-panel
**Created**: 2026-05-21
**Plan**: [plan.md](plan.md)

## Task List

### T1: Add GpsFixReceived event to LiveEventPublisher and HubEvents

**Priority**: P0 (prerequisite for all other tasks)
**Files**: `Streams/LiveEventPublisher.cs`, `Streams/NullLiveEventPublisher.cs`, `Dashboard/Hubs/HardwareStatusHub.cs`
**Dependencies**: None

- Add `GpsFixEventArgs` record to `LiveEventPublisher.cs`
- Add `GpsFixReceived` .NET event to `LiveEventPublisher`
- Add `PublishGpsFixAsync()` virtual method
- Add `GpsFixReceived` constant to `HubEvents`
- Override `PublishGpsFixAsync` in `NullLiveEventPublisher` as no-op

### T2: Create GpsFixStreamService (BackgroundService)

**Priority**: P0 (backend infrastructure)
**Files**: `Streams/GpsFixStreamService.cs` (NEW)
**Dependencies**: T1

- Create `GpsFixStreamService` extending `BackgroundService`
- Inject `IConfiguration`, `IHttpClientFactory`, `LiveEventPublisher`, `ILogger`
- Resolve gps-svc endpoint from config (`Peripherals:Gps:Host`, `Peripherals:Gps:RestPort`)
- Connect to `GET /api/stream/fixes` SSE endpoint
- Parse `data:` lines, deserialize `GpsFix` JSON
- Call `LiveEventPublisher.PublishGpsFixAsync()` for each fix
- Implement exponential backoff reconnection (2s initial, 30s cap, jitter)
- Catch all exceptions, log errors, never crash

### T3: Register GpsFixStreamService in DI

**Priority**: P0 (wiring)
**Files**: `Program.cs`
**Dependencies**: T2

- Add `builder.Services.AddHostedService<GpsFixStreamService>()` in Program.cs

### T4: Create GpsFixStreamPanel.razor component

**Priority**: P1 (UI)
**Files**: `Dashboard/Components/GpsFixStreamPanel.razor` (NEW)
**Dependencies**: T1

- Create Blazor component with card layout
- Subscribe to `LiveEventPublisher.GpsFixReceived` event
- Display all fields: Status badge, Fix mode, Lat/Lon (6dp), Altitude (m MSL), Speed (m/s), Track (degrees), HDOP, VDOP (--), Fix time (ISO-8601), Sats used/visible, Max/Min SNR (dBHz), Last updated (relative time)
- Status badge logic: HEALTHY (hasQualifyingFix), DEGRADED (connected but no qualifying fix), UNAVAILABLE (no data)
- Timer-based "Last updated" relative time display (1s refresh)
- Proper `IDisposable` implementation for event unsubscription and timer disposal

### T5: Integrate GpsFixStreamPanel into Streams.razor

**Priority**: P1 (page integration)
**Files**: `Dashboard/Pages/Streams.razor`
**Dependencies**: T4

- Add `@inject LiveEventPublisher` if not present
- Add `<GpsFixStreamPanel />` at top of page above run selector

### T6: Build verification and cleanup

**Priority**: P2 (quality gate)
**Files**: All changed files
**Dependencies**: T1-T5

- Run `dotnet build zSCOUT-hw-test.slnx`
- Fix any warnings (TreatWarningsAsErrors)
- Verify nullable reference type compliance
- Check code style consistency (tabs, sealed, file-scoped namespaces, XML docs)
