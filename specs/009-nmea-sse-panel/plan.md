# Implementation Plan: NMEA Diagnostic Stream Panel

**Branch**: `009-nmea-sse-panel` | **Date**: 2026-05-21 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/009-nmea-sse-panel/spec.md`

## Summary

Add a live NMEA sentence diagnostic panel to the Blazor Server dashboard. A backend `NmeaStreamService` connects to gps-svc's `GET /api/stream/nmea` SSE endpoint and relays sentences through `LiveEventPublisher` events. A new `GpsNmeaStreamPanel.razor` component subscribes to these events and displays a scrolling, capped buffer with Clear/Copy buttons and a Connected/Disconnected status badge.

## Technical Context

**Language/Version**: C# (latest) / .NET 10
**Primary Dependencies**: ASP.NET Core Blazor Server, SignalR, IHttpClientFactory
**Storage**: N/A (in-memory display buffer only)
**Testing**: xUnit
**Target Platform**: Linux ARM64 (Raspberry Pi CM5)
**Project Type**: Web dashboard (Blazor Server)
**Performance Goals**: NMEA sentences displayed within 1s of arrival
**Constraints**: Buffer capped at configurable max lines (default 200); no unbounded memory growth
**Scale/Scope**: Single diagnostic panel, single SSE source (gps-svc)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- ✅ Hardware-First: Panel consumes real NMEA data from gps-svc → gpsd → hardware GPS module
- ✅ Docker Parity: gps-svc runs in Docker; dashboard connects via REST — same in both Host and Container modes
- ✅ Actionable Diagnostics: Panel shows Disconnected badge when SSE drops, helping operator diagnose connectivity
- ✅ Structured Output: Raw NMEA sentences displayed as-is for diagnostic purposes
- ✅ Isolation: NMEA panel is independent; doesn't affect other dashboard pages or adapters
- ✅ Minimal Dependencies: No new dependencies — uses existing IHttpClientFactory, LiveEventPublisher, Blazor Server

## Project Structure

### Source Code Changes

```text
src/ZScout.HwTest.App/
├── Streams/
│   └── NmeaStreamService.cs          # NEW: IHostedService that reads SSE and publishes events
├── Dashboard/
│   ├── Pages/
│   │   └── NmeaDiagnostics.razor     # NEW: Full-page NMEA diagnostic panel
│   └── Shared/
│       └── MainLayout.razor          # MODIFIED: Add NMEA nav item
├── Program.cs                        # MODIFIED: Register NmeaStreamService
tests/ZScout.HwTest.App.Tests/
└── NmeaStreamServiceTests.cs         # NEW: Unit tests
```

## Architecture

### Backend: NmeaStreamService

A singleton service (not IHostedService — starts/stops on demand from the panel) that:
1. Connects to `http://{gps-host}:{restPort}/api/stream/nmea` using `IHttpClientFactory`
2. Reads SSE lines (`data:` prefixed)
3. Publishes each NMEA sentence via a new `NmeaSentenceReceived` event on `LiveEventPublisher`
4. Tracks connection state (Connected/Disconnected) and publishes state changes
5. On SSE stream end or error, transitions to Disconnected state

The service is reference-counted: first subscriber starts the stream, last unsubscriber stops it.

### Frontend: NmeaDiagnostics.razor

A Blazor Server page (`/nmea-diagnostics`) that:
1. On mount, subscribes to `LiveEventPublisher.NmeaSentenceReceived` and signals `NmeaStreamService` to start
2. Appends sentences to a `List<string>` buffer, capped at configurable max (default 200)
3. Renders in a fixed-height scrolling `<pre>` block with monospace font
4. Shows Connected/Disconnected badge based on stream state
5. Clear button empties the buffer; Copy button uses JS interop for clipboard
6. On dispose, unsubscribes and signals service to stop if no other subscribers

### LiveEventPublisher Extension

Add to `LiveEventPublisher`:
- `event EventHandler<NmeaSentenceEventArgs>? NmeaSentenceReceived`
- `event EventHandler<NmeaConnectionStateEventArgs>? NmeaConnectionStateChanged`
- `PublishNmeaSentenceAsync(string sentence)` method
- `PublishNmeaConnectionStateAsync(bool connected)` method

## Complexity Tracking

No constitution violations. Feature uses existing patterns (LiveEventPublisher events, Blazor component lifecycle, IHttpClientFactory SSE consumption).
