# Research: Hardware Communication Dashboard

## Decision 1: Solution topology is one .NET 10 app image with dual operation modes (host parity and container parity)

- **Decision**: Implement one deployable linux-arm64 image that contains the dashboard, orchestration APIs, and CLI-compatible test execution pathways. Host mode runs the same test modules directly on CM5 for parity baseline.
- **Rationale**: Constitution enforces Docker parity and one-image delivery. A single runtime surface minimizes drift between host and container behavior.
- **Alternatives considered**:
  - Separate dashboard and runner images: rejected due to deployment complexity and parity drift risk.
  - Container-only execution with no host baseline: rejected because host mode is mandatory.

## Decision 2: Peripheral communication adapters invoke existing native tools/drivers through Process wrappers

- **Decision**: Use dedicated adapters for GPS/gpsd, SDR/SoapySDR utilities, HaLow/morse driver inspection, and compass/I2C queries via existing command-line tooling.
- **Rationale**: Constitution mandates minimal dependencies and explicitly allows native tool invocation via `System.Diagnostics.Process`.
- **Alternatives considered**:
  - Introducing additional managed device SDK wrappers: rejected as unnecessary dependency expansion.
  - Shell script orchestration only: rejected due to weaker type safety and dashboard integration complexity.

## Decision 3: Manual operator verdict workflow with mandatory evidence capture

- **Decision**: Test runs collect objective communication evidence, while authorized operators assign final per-peripheral pass/fail verdicts with required reason text for failures.
- **Rationale**: Clarified requirement mandates manual review-only pass/fail decisions and demands actionable diagnostics.
- **Alternatives considered**:
  - Fully automated threshold pass/fail: rejected due to clarification choice.
  - Free-form notes without structured evidence: rejected because it undermines consistency and auditability.

## Decision 4: Local role-based authentication with three fixed roles

- **Decision**: Implement local authentication and RBAC roles (`viewer`, `operator`, `admin`) with endpoint-level authorization.
- **Rationale**: Clarification and FR-013..FR-015 require local login and role separation for field deployments that may lack internet.
- **Alternatives considered**:
  - Network-only security: rejected due to missing application-level access control.
  - External SSO/OAuth: rejected because offline compatibility is required.

## Decision 5: Retention and export model uses local append-only records with 30-day pruning

- **Decision**: Persist run metadata and telemetry streams on-device with retention pruning at 30 days and manual export endpoints for authorized users.
- **Rationale**: Meets clarified retention policy and allows deterministic storage management in constrained environments.
- **Alternatives considered**:
  - Unlimited retention: rejected due to storage pressure on CM5 deployments.
  - 24-hour retention: rejected because it reduces troubleshooting window.

## Decision 6: Failure isolation strategy continues unaffected peripheral checks

- **Decision**: When a dependency service/driver is unavailable, mark only impacted peripherals unavailable/failed while continuing unaffected modules.
- **Rationale**: Aligns with clarified edge-case behavior and constitution rule for module independence.
- **Alternatives considered**:
  - Abort entire run on first dependency failure: rejected because it hides healthy subsystem data.
  - Retry-until-success blocking flow: rejected because it can stall runs and delay operator feedback.