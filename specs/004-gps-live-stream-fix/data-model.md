# Data Model: GPS Live Stream and Fix-Based Verdict

## GnssFixUpdate (new C# record)

Represents a single parsed update from the gpsd JSON stream.

```
GnssFixUpdate
├── Class: string              ("TPV" | "SKY" | other)
├── Mode: int?                 (0=unknown, 1=no-fix, 2=2D, 3=3D)
├── Latitude: double?
├── Longitude: double?
├── AltitudeM: double?
├── UtcTime: string?           (ISO-8601 from gpsd)
├── SpeedMs: double?           (m/s; convert to knots on snapshot build)
├── Track: double?             (degrees true)
├── Hdop: double?
├── SatellitesUsed: int        (count of SKY.satellites where used=true)
├── SatellitesVisible: int     (total count of SKY.satellites)
├── MaxSnrDb: int?             (max ss across SKY.satellites)
└── MinSnrDb: int?             (min ss across SKY.satellites with ss > 0)
```

**Lifecycle**: Created once per parsed gpsd JSON line; immediately consumed by `GpsFixAccumulator`; not persisted.

## GpsFixAccumulator (new C# class)

Mutable session-scoped object. One instance per `ProbeAsync` call.

```
GpsFixAccumulator
├── FixObtained: bool           (true once a qualifying fix is seen)
├── BestFix: GnssFixUpdate?     (the most recent qualifying TPV update)
├── LastSkyUpdate: GnssFixUpdate? (the most recent SKY update)
├── TotalFixUpdates: int        (count of all TPV updates received, fix or not)
└── Methods:
    ├── Update(GnssFixUpdate)   — integrates a new parsed update
    ├── IsQualifying(GnssFixUpdate) — returns true if TPV has non-null/non-zero lat/lon/alt/time/mode≥2
    └── BuildSnapshot() → Dictionary<string, object?> — produces the 14-field HealthSnapshot.Values
```

**Fix qualification rule**: A TPV update qualifies if `mode >= 2`, `lat != 0`, `lon != 0`, `alt != null`, `time != null`.

## HealthSnapshot.Values Extension (GPS-specific keys)

The existing `HealthSnapshot.Values` dictionary is extended with these 14 keys for GPS evidence:

| Key | Type | Default (no fix) | Description |
|---|---|---|---|
| `gpsd_running` | bool | false | pgrep -x gpsd exit code 0 |
| `fix_obtained` | bool | false | at least one qualifying fix seen |
| `fix_quality` | int | 0 | best TPV.mode value (0-3) |
| `latitude` | double? | null | lat from best qualifying TPV |
| `longitude` | double? | null | lon from best qualifying TPV |
| `altitude_m` | double? | null | alt from best qualifying TPV |
| `utc_time` | string? | null | time from best qualifying TPV |
| `satellites_used` | int | 0 | from most recent SKY update |
| `satellites_visible` | int | 0 | from most recent SKY update |
| `hdop` | double? | null | hdop from best qualifying TPV |
| `max_snr_db` | int? | null | max ss from most recent SKY update |
| `min_snr_db` | int? | null | min ss from most recent SKY update |
| `speed_knots` | double? | null | speed_ms × 1.94384 from best TPV |
| `total_fix_updates` | int | 0 | total TPV lines received |

## RunCancellationService (new C# class)

Singleton-scoped service. Maps `runId → CancellationTokenSource`.

```
RunCancellationService
├── RegisterAsync(runId) → CancellationToken
├── CancelAsync(runId) — cancels and removes the CTS for the given run
└── Unregister(runId)  — removes without cancelling (cleanup after normal completion)
```

**Lifecycle**: Registered before orchestrator starts; cancelled by `StopRunAsync`; unregistered after orchestrator completes.

## Existing Entities (unchanged)

- `PeripheralEvidence` — no field additions; GPS-specific data lives in `HealthSnapshot.Values`
- `DiagnosticEnvelope` — no changes; status and messages populated as before
- `TestRun` — no changes
- `IHardwareAdapter` — no interface changes; `ProbeAsync` and `ReadRawSampleAsync` signatures preserved
