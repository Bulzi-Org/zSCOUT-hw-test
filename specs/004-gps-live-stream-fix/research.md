# Research: GPS Live Stream and Fix-Based Verdict

## gpsd JSON Output Format (gpspipe -w)

**Decision**: Use `gpspipe -w` (JSON mode) as the primary streaming source.
**Rationale**: gpsd pre-parses NMEA sentences into structured JSON, eliminating checksum validation and manual field tokenisation. The JSON `class` field disambiguates message types.
**Alternatives considered**: `gpspipe -r` (raw NMEA) was rejected because it requires manual NMEA tokenisation and is prone to malformed-sentence edge cases.

### TPV (Time-Position-Velocity) Object

Published whenever the GPS module updates its position estimate. Key fields:

```json
{
  "class": "TPV",
  "mode": 2,
  "time": "2026-05-18T00:45:00.000Z",
  "lat": 37.123456,
  "lon": -122.654321,
  "alt": 42.5,
  "speed": 0.05,
  "track": 180.0,
  "hdop": 1.2
}
```

- `mode`: 0=unknown, 1=no fix, 2=2D fix, 3=3D fix
- `alt` = altitude above MSL in metres
- `speed` = speed over ground in m/s (convert to knots: ×1.94384)
- `hdop` = horizontal dilution of precision

### SKY (Satellite View) Object

Published alongside TPV. Key fields:

```json
{
  "class": "SKY",
  "satellites": [
    {"PRN": 5, "used": true, "ss": 42},
    {"PRN": 12, "used": false, "ss": 18}
  ]
}
```

- `satellites[].used`: true if this satellite contributed to the fix
- `satellites[].ss`: signal strength (SNR in dBHz)
- Satellites used count = `satellites.Count(s => s.used)`
- Satellites visible = `satellites.Count`

## Streaming Process Approach

**Decision**: Add `ProcessHelper.StreamLinesAsync` that reads stdout line-by-line.
**Rationale**: `ReadToEndAsync` buffers the entire stdout before returning — unsuitable for indefinite streaming. `ReadLineAsync` with a loop provides per-line callbacks while respecting CancellationToken.
**Implementation notes**:
- Process started without `UseShellExecute`, with `RedirectStandardOutput = true`
- Loop: `while (!ct.IsCancellationRequested) { line = await reader.ReadLineAsync(ct); if (line is null) break; await handler(line, ct); }`
- On CT cancellation: `process.Kill(entireProcessTree: true)` in finally block
- stderr captured separately for diagnostics

## CancellationToken Propagation

**Decision**: Add `RunCancellationService` singleton to manage per-run CTS.
**Rationale**: Currently `RunCommandService.StartRunAsync` fires the orchestrator as `Task.Run` with no cancellation token. `StopRunAsync` only persists the Stopped state — no signal reaches running adapters. A registry-based CTS service is the minimal, non-breaking way to wire this up.
**Alternative rejected**: Thread.Abort / process kill from outside — unsafe; IHostedService with channels — heavier than needed.

## Task.WhenAll for Adapter Concurrency

**Decision**: Replace sequential `foreach` in `RunOrchestrator.ExecuteAsync` with `Task.WhenAll`.
**Rationale**: AGENTS.md constitution specifies concurrent execution via `Task.WhenAll`. The current sequential implementation means GPS streaming (indefinite) would block SDR, HaLow, and Compass adapters from running. With `Task.WhenAll`, all four adapters start concurrently; the three short-lived ones complete within seconds while GPS streams.
**Isolation preserved**: Each adapter is wrapped in `ProbeAdapterSafeAsync` with try/catch; any exception returns Unavailable without affecting siblings.

## HealthSnapshot Field Mapping

| Field | Source | Notes |
|---|---|---|
| gpsd_running | pgrep result | bool |
| fix_obtained | accumulator | bool: any TPV with mode≥2 and non-zero lat/lon/alt/time |
| fix_quality | best TPV.mode | int: 0-3 |
| latitude | best TPV.lat | double? |
| longitude | best TPV.lon | double? |
| altitude_m | best TPV.alt | double? |
| utc_time | best TPV.time | string? ISO-8601 |
| satellites_used | SKY.satellites.Count(used) | int |
| satellites_visible | SKY.satellites.Count | int |
| hdop | best TPV.hdop | double? |
| max_snr_db | SKY.satellites.Max(ss) | int? |
| min_snr_db | SKY.satellites.Min(ss) | int? |
| speed_knots | best TPV.speed × 1.94384 | double? |
| total_fix_updates | accumulator counter | int |
