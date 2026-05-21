# Tasks: GPS REST API Migration

**Branch**: `008-gps-rest-api` | **Date**: 2026-05-21
**Plan**: [plan.md](plan.md) | **Spec**: [spec.md](spec.md)

## Task List

### T1: Create GpsFix record model
**Priority**: P1 | **Depends on**: None
**Files**: `src/ZScout.HwTest.App/Hardware/Gps/GpsFix.cs` (NEW)

Create a `GpsFix` sealed record with properties matching the gps-svc REST API JSON response: Mode, Latitude, Longitude, AltitudeM, UtcTime, SpeedMs, Track, Hdop, SatellitesUsed, SatellitesVisible, HasQualifyingFix. Use System.Text.Json camelCase attributes. Include XML doc comments.

### T2: Update GpsFixAccumulator to use GpsFix
**Priority**: P1 | **Depends on**: T1
**Files**: `src/ZScout.HwTest.App/Hardware/Gps/GpsFixAccumulator.cs` (MODIFY)

Change `Update(GnssFixUpdate)` to `Update(GpsFix)`. Update `IsQualifying` to accept `GpsFix`. Update `BestFix` and `LastSkyUpdate` types. Adjust `FormatFixSummary` and `BuildSnapshot` accordingly. Since GpsFix merges TPV+SKY data into a single model, simplify the accumulator to track satellite info from the same fix object.

### T3: Rewrite GpsAdapter.ProbeAsync for REST API
**Priority**: P1 | **Depends on**: T1, T2
**Files**: `src/ZScout.HwTest.App/Hardware/Gps/GpsAdapter.cs` (MODIFY)

- Step 1: Replace /api/status call with GET /api/fix for availability check. Return Unavailable on 503 or network error. Remove TCP fallback (TcpHealthCheck).
- Step 2: Replace gpspipe subprocess with SSE stream from GET /api/stream/fixes. Use HttpClient.GetStreamAsync + StreamReader to read `data:` lines. Deserialize each line as GpsFix JSON. Feed to GpsFixAccumulator.
- Remove `ReadRawSampleAsync` method.
- Remove `Peripherals:Gps:Port` config key usage.
- Update doc comments to reference REST API instead of gpspipe.

### T4: Delete GnssFixUpdate.cs (GnssFixUpdate + GnssJsonParser)
**Priority**: P1 | **Depends on**: T2, T3
**Files**: `src/ZScout.HwTest.App/Hardware/Gps/GnssFixUpdate.cs` (DELETE)

Remove the file containing GnssFixUpdate record and GnssJsonParser static class. These are fully replaced by GpsFix + direct System.Text.Json deserialization.

### T5: Remove gpsd-clients from Dockerfile
**Priority**: P2 | **Depends on**: T3
**Files**: `deploy/Dockerfile` (MODIFY)

Remove `gpsd-clients` from the apt-get install line. Update the comment to note GPS now uses gps-svc REST API.

### T6: Update unit tests
**Priority**: P1 | **Depends on**: T1, T2, T3, T4
**Files**:
- `tests/ZScout.HwTest.App.Tests/Hardware/Gps/GnssFixParserTests.cs` (DELETE)
- `tests/ZScout.HwTest.App.Tests/Hardware/Gps/GpsFixAccumulatorTests.cs` (MODIFY)
- `tests/ZScout.HwTest.App.Tests/GpsAdapterReportStepTests.cs` (MODIFY)

- Delete GnssFixParserTests.cs (parser removed).
- Update GpsFixAccumulatorTests.cs to use GpsFix instead of GnssFixUpdate.
- Update GpsAdapterReportStepTests.cs for REST-only behavior (no TCP fallback expected).

### T7: Build verification and self-review
**Priority**: P1 | **Depends on**: T1-T6
**Files**: All modified files

Run `dotnet build zSCOUT-hw-test.slnx` and `dotnet test`. Fix any compilation errors or test failures. Review all changes for alignment with spec.
