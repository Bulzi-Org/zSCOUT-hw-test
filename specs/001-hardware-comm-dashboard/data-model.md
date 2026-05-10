# Data Model: Hardware Communication Dashboard

## Entity: UserAccount

- **Purpose**: Local dashboard authentication principal.
- **Fields**:
  - `userId` (string, immutable)
  - `username` (string, unique)
  - `passwordHash` (string)
  - `role` (enum: `viewer`, `operator`, `admin`)
  - `isActive` (bool)
  - `createdAtUtc` (datetime)
  - `updatedAtUtc` (datetime)
- **Validation Rules**:
  - Username must be non-empty and unique.
  - Role must be one of allowed values.
  - Disabled users cannot start sessions.

## Entity: PeripheralProfile

- **Purpose**: Static and runtime metadata for one required peripheral.
- **Fields**:
  - `peripheralId` (enum-like string: `gps`, `sdr`, `halow`, `compass`)
  - `displayName` (string)
  - `transport` (string: usb, i2c, pcie, net)
  - `expectedPath` (string, nullable)
  - `dependencyService` (string, nullable)
  - `driverName` (string, nullable)
  - `lastObservedStatus` (enum: `unknown`, `ready`, `degraded`, `unavailable`)
  - `lastObservedAtUtc` (datetime, nullable)
  - `lastDiagnostic` (string, nullable)
- **Validation Rules**:
  - Exactly four required profiles exist.
  - `peripheralId` is immutable after creation.

## Entity: TestRun

- **Purpose**: Aggregate record for one orchestrated run.
- **Fields**:
  - `runId` (string/ULID)
  - `mode` (enum: `host`, `container`)
  - `status` (enum: `queued`, `running`, `awaiting_verdict`, `completed`, `failed`, `stopped`, `rejected`)
  - `requestedByUserId` (string)
  - `configuration` (object: timeouts, paths, polling intervals)
  - `startedAtUtc` (datetime, nullable)
  - `finishedAtUtc` (datetime, nullable)
  - `rejectionReason` (string, nullable)
  - `overallOutcome` (enum: `pass`, `fail`, `inconclusive`, nullable)
- **Validation Rules**:
  - Only one run can hold `queued` or `running` at a time per device.
  - `mode` is required at creation.
  - `rejectionReason` required when status is `rejected`.

## Entity: PeripheralEvidence

- **Purpose**: Captured objective data for one peripheral in one run.
- **Fields**:
  - `evidenceId` (string)
  - `runId` (string, foreign key to TestRun)
  - `peripheralId` (string, foreign key to PeripheralProfile)
  - `sampleCount` (integer)
  - `lastSampleAtUtc` (datetime, nullable)
  - `healthSnapshot` (object)
  - `diagnosticMessages` (array<string>)
  - `dependencyAvailable` (bool)
  - `rawStreamPointer` (string, reference to telemetry files)
- **Validation Rules**:
  - Exactly one evidence record per peripheral per run.
  - `runId + peripheralId` must be unique.

## Entity: PeripheralVerdict

- **Purpose**: Operator-assigned manual pass/fail decision.
- **Fields**:
  - `verdictId` (string)
  - `runId` (string)
  - `peripheralId` (string)
  - `outcome` (enum: `pass`, `fail`)
  - `failureReason` (string, nullable)
  - `assignedByUserId` (string)
  - `assignedAtUtc` (datetime)
- **Validation Rules**:
  - Outcome is required.
  - `failureReason` is required when outcome is `fail`.
  - User role must be `operator` or `admin`.

## Entity: TelemetryStreamRecord

- **Purpose**: Time-series raw stream sample for dashboard and exports.
- **Fields**:
  - `streamRecordId` (string)
  - `runId` (string)
  - `peripheralId` (string)
  - `streamType` (enum: `gps_nmea`, `sdr_info`, `halow_metrics`, `compass_heading`)
  - `timestampUtc` (datetime)
  - `payload` (string or JSON object)
  - `isMalformed` (bool)
- **Validation Rules**:
  - `timestampUtc` required and monotonic per stream file append.
  - `payload` stored even when malformed, with `isMalformed=true`.

## Entity: ExportJob

- **Purpose**: Tracks manual export request for retained data.
- **Fields**:
  - `exportJobId` (string)
  - `requestedByUserId` (string)
  - `fromUtc` (datetime)
  - `toUtc` (datetime)
  - `format` (enum: `zip-json`)
  - `status` (enum: `queued`, `ready`, `failed`)
  - `artifactPath` (string, nullable)
  - `createdAtUtc` (datetime)
  - `expiresAtUtc` (datetime)
- **Validation Rules**:
  - Requestor role must be authorized (`operator` or `admin`).
  - Time range must satisfy `fromUtc <= toUtc` and lie within retained window.

## Relationships

- One `UserAccount` creates many `TestRun` and `PeripheralVerdict` records.
- One `TestRun` has exactly four `PeripheralEvidence` records (one per required peripheral).
- One `TestRun` has up to four `PeripheralVerdict` records (one per peripheral).
- One `TestRun` has many `TelemetryStreamRecord` entries.
- `ExportJob` can include data across many runs within the selected time range.

## State Transitions

### TestRun.status

1. `queued` -> `running` when execution starts.
2. `running` -> `awaiting_verdict` when evidence collection completes.
3. `running` -> `failed` when orchestration-level fatal condition occurs.
4. `running` -> `stopped` when operator stops run.
5. `awaiting_verdict` -> `completed` after required peripheral verdicts are submitted.
6. New request -> `rejected` if another run is active.

### ExportJob.status

1. `queued` -> `ready` when artifact assembled.
2. `queued` -> `failed` if export generation fails.