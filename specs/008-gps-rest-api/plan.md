# Implementation Plan: GPS REST API Migration

**Branch**: `008-gps-rest-api` | **Date**: 2026-05-21 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/008-gps-rest-api/spec.md`

## Summary

Refactor GpsAdapter to consume gps-svc REST API (/api/fix for availability, /api/stream/fixes for SSE streaming) instead of direct gpspipe subprocess calls. This removes the gpsd-clients dependency from the Docker image, eliminates the TCP fallback to port 2947, and replaces the internal GnssFixUpdate/GnssJsonParser models with a GpsFix record matching the gps-svc JSON shape.

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: ASP.NET Core, HttpClient (SSE streaming), System.Text.Json
**Storage**: N/A (no changes to persistence)
**Testing**: xUnit
**Target Platform**: Linux ARM64 (Raspberry Pi CM5)
**Project Type**: Blazor Server web-service + CLI
**Constraints**: Must work over host networking with gps-svc on localhost:5200

## Constitution Check

- ✅ Hardware-First Validation: Adapter still targets real GPS hardware via gps-svc.
- ✅ Docker Parity: Both Host and Container modes use the same REST API path.
- ✅ Actionable Diagnostics: Unavailable returns include HTTP status codes and error details.
- ✅ Structured Output: HealthSnapshot 14-field structure preserved.
- ✅ Isolation: GPS adapter remains independently runnable.
- ✅ Minimal Dependencies: Removing gpsd-clients reduces dependencies. No new packages needed — HttpClient is built-in.

## Project Structure

### Source Code Changes

```text
src/ZScout.HwTest.App/Hardware/Gps/
├── GpsAdapter.cs              # MODIFY: Replace gpspipe with REST API calls
├── GpsFix.cs                  # NEW: GpsFix record for /api/fix JSON deserialization
├── GpsFixAccumulator.cs       # MODIFY: Update to accept GpsFix instead of GnssFixUpdate
├── GnssFixUpdate.cs           # DELETE: Replaced by GpsFix
└── (GnssJsonParser in GnssFixUpdate.cs) # DELETE: No longer needed

deploy/
└── Dockerfile                 # MODIFY: Remove gpsd-clients from apt install

tests/ZScout.HwTest.App.Tests/
├── GpsAdapterReportStepTests.cs           # MODIFY: Update for REST-only behavior
└── Hardware/Gps/
    ├── GnssFixParserTests.cs              # DELETE: Parser removed
    ├── GpsFixAccumulatorTests.cs          # MODIFY: Update to use GpsFix
    └── GpsAdapterSseStreamTests.cs        # NEW: Test SSE parsing logic
```

## Design Decisions

### D1: GpsFix record shape
Define a local `GpsFix` record in `src/ZScout.HwTest.App/Hardware/Gps/GpsFix.cs` matching the gps-svc JSON response. Fields: Mode, Latitude, Longitude, AltitudeM, UtcTime, SpeedMs, Track, Hdop, SatellitesUsed, SatellitesVisible, HasQualifyingFix. Use System.Text.Json attributes for camelCase deserialization.

### D2: SSE stream consumption
Use HttpClient.GetStreamAsync() with StreamReader to read SSE `data:` lines. Parse each line prefix, strip `data:`, deserialize as GpsFix JSON. This avoids any third-party SSE library dependency.

### D3: GpsFixAccumulator adaptation
Keep GpsFixAccumulator but change its Update method to accept GpsFix instead of GnssFixUpdate. The IsQualifying check uses GpsFix.HasQualifyingFix from the REST model (server-side check) or equivalent local logic.

### D4: ReadRawSampleAsync removal
Remove the one-shot NMEA method — gps-svc doesn't expose raw NMEA via REST. If health polling is needed in the future, GET /api/fix serves that purpose.

### D5: Configuration cleanup
Remove `Peripherals:Gps:Port` (2947) config key usage. Keep only `Host`, `RestPort`, and `TimeoutMs`.
