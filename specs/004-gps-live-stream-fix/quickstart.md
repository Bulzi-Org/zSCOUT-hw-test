# Quick-Start Test Scenarios: GPS Live Stream

## Scenario 1: Happy Path — Fix Obtained

**Given**: gpsd running, GPS module has satellite visibility, `gpspipe -w` emits TPV with mode=3.

```bash
# Start a run via dashboard or API, let GPS stream for 60s, then click Stop.
# Expected: HealthSnapshot.fix_obtained=true, verdict=PASS
# Expected dashboard: lat/lon/alt/time/sat_count all non-null in cmd-log
```

## Scenario 2: No Fix — Cold Start

**Given**: gpsd running but module has no satellite fix (mode=1 TPVs only).

```bash
# Start run, let GPS stream for ~30s, then Stop.
# Expected: HealthSnapshot.fix_obtained=false, verdict=FAIL
# Expected: diagnostic message "GPS test stopped: no qualifying fix obtained"
```

## Scenario 3: gpsd Not Running

**Given**: gpsd not running (pgrep exits non-zero).

```bash
# Start run immediately.
# Expected: GPS returns DiagnosticEnvelope.Unavailable immediately
# Expected: Other adapters (SDR, HaLow, Compass) are NOT blocked — they complete normally
# Expected: HealthSnapshot.gpsd_running=false
```

## Scenario 4: Run Stopped Mid-Stream via REST API

```bash
# Start run, wait 5s for GPS stream to begin, then:
curl -X POST http://localhost:5000/api/runs/{runId}/stop

# Expected: gpspipe process terminated cleanly
# Expected: HealthSnapshot populated with whatever was seen up to that point
# Expected: Run status = Stopped
```

## Scenario 5: ReadRawSampleAsync Independence

```bash
# While GPS test is streaming in an active run, call:
curl http://localhost:5000/api/peripherals/gps/sample

# Expected: Returns a raw NMEA sentence or null within 6s
# Expected: Active streaming session is NOT interrupted
```

## Unit Test Scenarios (GnssFixParserTests)

- Parse well-formed TPV JSON → all fields populated
- Parse TPV with missing optional fields (no alt, no hdop) → nullable fields null
- Parse TPV mode=1 (no fix) → fix not qualifying
- Parse SKY JSON with satellite list → used/visible counts, max/min SNR correct
- Parse malformed JSON line → returns null, no exception thrown
- Parse non-TPV/SKY class → returns null, no exception thrown

## Unit Test Scenarios (GpsFixAccumulatorTests)

- Update with no-fix TPV × 5 → FixObtained=false, TotalFixUpdates=5
- Update with qualifying TPV → FixObtained=true, BestFix set
- Update with SKY → LastSkyUpdate set, satellite counts updated
- BuildSnapshot with fix → all 14 fields present with values
- BuildSnapshot without fix → all 14 fields present with null/zero defaults
- Update with better fix (higher mode) replaces BestFix
