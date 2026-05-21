# Implementation Plan: SSE GPS Fix Diagnostic Panel

**Branch**: `009-sse-gps-fix-panel` | **Date**: 2026-05-21 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/009-sse-gps-fix-panel/spec.md`

## Summary

Add a live GPS fix diagnostic panel to the Blazor dashboard that consumes the gps-svc SSE endpoint (`GET /api/stream/fixes`) via a backend `IHostedService`, relays parsed `GpsFix` objects through the existing `LiveEventPublisher` .NET event system, and displays all specified fields in a real-time updating `GpsFixStreamPanel.razor` component on the Streams page.

## Technical Context

**Language/Version**: C# (latest LangVersion), .NET 10, `net10.0` TFM
**Primary Dependencies**: ASP.NET Core (Blazor Server, SignalR), System.Text.Json
**Storage**: N/A (real-time streaming only, no persistence)
**Testing**: xUnit
**Target Platform**: Raspberry Pi CM5 (ARM64 Linux, Docker)
**Project Type**: Blazor Server web application
**Performance Goals**: ≤1s latency from GPS fix emission to UI display
**Constraints**: TreatWarningsAsErrors enabled, nullable reference types, tab indentation
**Scale/Scope**: Single GPS fix stream, <10 concurrent dashboard clients

## Architecture

### Data Flow

```
gps-svc (SSE) → GpsFixStreamService (IHostedService) → LiveEventPublisher (.NET event) → GpsFixStreamPanel.razor (UI)
```

### Component Design

#### 1. GpsFixStreamService (Backend)

**Location**: `src/ZScout.HwTest.App/Streams/GpsFixStreamService.cs`

- Implements `BackgroundService` (inherits `IHostedService`)
- On startup, connects to `GET /api/stream/fixes` SSE endpoint
- Resolves host/port from `IConfiguration` (`Peripherals:Gps:Host`, `Peripherals:Gps:RestPort`)
- Parses `data:` prefixed SSE lines, deserializes `GpsFix` JSON
- Publishes each fix via `LiveEventPublisher.PublishGpsFixAsync()` (new method)
- Reconnects with exponential backoff (2s initial, 30s cap, jitter) on connection drop
- Catches all exceptions — logs and retries, never crashes

#### 2. LiveEventPublisher Extension

**Location**: `src/ZScout.HwTest.App/Streams/LiveEventPublisher.cs`

- Add `GpsFixReceived` .NET event (`EventHandler<GpsFixEventArgs>`)
- Add `PublishGpsFixAsync()` method that raises the event and sends via SignalR hub
- Add new hub event constant `GpsFixReceived` to `HubEvents`
- Update `NullLiveEventPublisher` with no-op override

#### 3. GpsFixStreamPanel.razor (Frontend)

**Location**: `src/ZScout.HwTest.App/Dashboard/Components/GpsFixStreamPanel.razor`

- Subscribes to `LiveEventPublisher.GpsFixReceived` .NET event
- Displays structured card with all fields from issue spec
- Status badge: HEALTHY (mode≥2 + qualifying fix), DEGRADED (mode<2 or no qualifying fix), UNAVAILABLE (no data received)
- "Last updated" field uses a 1-second timer to update relative time display
- All values show "--" placeholder when no data available

#### 4. Streams.razor Integration

**Location**: `src/ZScout.HwTest.App/Dashboard/Pages/Streams.razor`

- Add `<GpsFixStreamPanel />` at top of page, above existing run selector

## Project Structure

### Source Code Changes

```
src/ZScout.HwTest.App/
├── Streams/
│   ├── GpsFixStreamService.cs     # NEW: IHostedService SSE consumer
│   └── LiveEventPublisher.cs      # MODIFY: add GpsFixReceived event + publish method
│   └── NullLiveEventPublisher.cs   # MODIFY: add no-op override
├── Dashboard/
│   ├── Components/
│   │   └── GpsFixStreamPanel.razor # NEW: live GPS fix display component
│   ├── Pages/
│   │   └── Streams.razor           # MODIFY: add panel at top
│   └── Hubs/
│       └── HardwareStatusHub.cs    # MODIFY: add GpsFixReceived constant
├── Program.cs                      # MODIFY: register GpsFixStreamService
```

## Key Design Decisions

- **LiveEventPublisher .NET events** (not direct SignalR): Matches existing Control.razor pattern. Blazor Server components run in-process, so .NET events are more efficient than JS SignalR roundtrip.
- **BackgroundService** (not middleware or manual thread): Standard ASP.NET Core pattern for long-running background work with proper lifetime management and cancellation.
- **Component in Dashboard/Components/**: Reusable component pattern consistent with existing `PeripheralStatusGrid.razor` and `RunLifecyclePanel.razor`.
- **VDOP shows "--"**: The `GpsFix` model doesn't include VDOP. Display placeholder; extend model in a future PR.
