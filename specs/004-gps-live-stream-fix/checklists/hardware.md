# Hardware Requirements Quality Checklist: GPS Live Stream

**Purpose**: Validate requirements completeness, clarity, and consistency for the GPS streaming and fix-verdict feature, focusing on hardware adapter behavior, Docker parity, and constitution compliance
**Created**: 2026-05-18
**Feature**: [spec.md](../spec.md)
**Depth**: Standard | **Audience**: Reviewer (PR) | **Focus**: Hardware adapter, isolation, Docker parity, diagnostics

## Requirement Completeness

- [ ] CHK001 Are streaming termination requirements defined for all cancellation paths (operator Stop, CancellationToken, process crash)? [Completeness, Spec §Edge Cases]
- [ ] CHK002 Is the requirement for "Unavailable immediately" upon gpsd absence scoped to the initial probe step only, or to any point during streaming? [Completeness, Spec §FR-008]
- [ ] CHK003 Are requirements for the dashboard GPS output refresh interval defined with a specific target (e.g., ≤1 update per second)? [Completeness, Spec §SC-003]
- [ ] CHK004 Are requirements specified for what happens when `gpspipe -w` produces only non-TPV/non-SKY class objects (e.g., VERSION, DEVICES)? [Completeness, Spec §Edge Cases]
- [ ] CHK005 Are the requirements for `ReadRawSampleAsync` explicitly scoped to exclude interaction with the streaming session? [Completeness, Spec §FR-007]

## Requirement Clarity

- [ ] CHK006 Is the "qualifying fix" definition precise enough to be unambiguously implemented? (mode≥2, lat≠0, lon≠0, alt≠null, time≠null) [Clarity, Spec §FR-004]
- [ ] CHK007 Is "non-zero latitude/longitude" clearly distinguished from "mode≥2"? (A module can report mode=2 with near-zero coordinates at the null island) [Clarity, Spec §FR-004, Gap]
- [ ] CHK008 Is `fix_quality` field defined as TPV.mode (0-3 int) consistently throughout spec, data model, and contract? [Clarity, Spec §FR-006, data-model.md]
- [ ] CHK009 Is the HealthSnapshot key name `altitude_m` consistent with the issue §4 field name `altitude_m`? [Clarity, Spec §FR-006]
- [ ] CHK010 Is "streaming" sufficiently defined — does it include a minimum data rate requirement or just a liveness requirement? [Clarity, Spec §SC-003]

## Requirement Consistency

- [ ] CHK011 Does FR-005 (Ready→Pass when fix_obtained) align with the AutoAssignVerdict logic in RunOrchestrator that maps Ready→Pass? [Consistency, Spec §FR-005, plan.md Phase 3]
- [ ] CHK012 Is FR-010 (isolation) consistent with the proposed Task.WhenAll change in plan.md? Does the spec say "independently runnable" when the implementation runs all adapters concurrently? [Consistency, Spec §FR-010, plan.md]
- [ ] CHK013 Do FR-001 and FR-002 together imply that `reportStep` is called once per parsed line (not only when a qualifying fix is detected)? Is this consistent with dashboard behavior? [Consistency, Spec §FR-001, §FR-002]

## Acceptance Criteria Quality

- [ ] CHK014 Can SC-002 ("100% of sessions with qualifying fix → PASS") be objectively measured in automated tests without real GPS hardware? [Measurability, Spec §SC-002]
- [ ] CHK015 Is SC-001 ("fix visible within 30 seconds") measurable given that acquisition time is hardware-dependent? [Measurability, Spec §SC-001]
- [ ] CHK016 Are acceptance scenarios in US1–US3 specific enough to distinguish unit-testable from hardware-only testable criteria? [Acceptance Criteria Quality, Spec §User Scenarios]

## Scenario Coverage

- [ ] CHK017 Are requirements defined for the scenario where `gpspipe -w` exits with exit code 0 before CT cancellation (normal EOF from gpsd)? [Coverage, Gap]
- [ ] CHK018 Are recovery requirements specified if `gpspipe -w` process crashes and restarts mid-session? [Coverage, Spec §Edge Cases]
- [ ] CHK019 Are concurrent call requirements addressed? (e.g., two simultaneous `ReadRawSampleAsync` calls while streaming) [Coverage, Gap]

## Edge Case Coverage

- [ ] CHK020 Is the "null island" edge case (lat=0.0, lon=0.0) explicitly excluded by the qualifying fix definition? [Edge Case, Spec §FR-004]
- [ ] CHK021 Are timezone and UTC offset requirements for `utc_time` field defined? (gpsd outputs ISO-8601 UTC) [Edge Case, Spec §FR-006]
- [ ] CHK022 Is handling of `alt` field absence (2D fix has no altitude) defined in the qualifying fix rule? [Edge Case, Spec §FR-004, data-model.md]

## Non-Functional Requirements

- [ ] CHK023 Are memory requirements specified for the `GpsFixAccumulator`? (unbounded SKY data could grow if satellite list is large) [Non-Functional, Gap]
- [ ] CHK024 Are Docker parity requirements explicitly stated for the streaming path (same gpspipe command in host and container modes)? [Non-Functional, Constitution §II, Spec §Assumptions]
- [ ] CHK025 Are diagnostic output requirements (Constitution §III) met — does the spec require actionable error messages for all failure modes (gpsd not running, process crash, no fix)? [Non-Functional, Constitution §III, Spec §FR-008, §FR-009]

## Dependencies & Assumptions

- [ ] CHK026 Is the assumption that `gpspipe -w` is available in the Docker container image documented and testable? [Dependency, Spec §Assumptions]
- [ ] CHK027 Is the assumption that `RunOrchestrator` uses sequential execution (current) vs. concurrent (proposed) documented as a prerequisite for GPS streaming to work? [Assumption, plan.md Phase 3]
- [ ] CHK028 Are the implications of changing `RunOrchestrator` from sequential to `Task.WhenAll` on other adapters (SDR, HaLow, Compass) evaluated in the spec or plan? [Dependency, plan.md §Phase 3]

## Notes

- CHK007 (null island) is an edge case worth raising: a GPS module at lat=0.0, lon=0.0 with mode=2 would currently qualify as a fix. This is geographically implausible for CM5 deployments but should be documented as an accepted limitation.
- CHK022 (2D fix alt=null): The qualifying fix rule requires alt≠null, which means 2D fixes (mode=2) without altitude will not qualify. This matches the issue §2 requirement: "Altitude (above MSL)" must be non-null. Consistent.
- All CHK items are requirements quality checks, not implementation tests.
