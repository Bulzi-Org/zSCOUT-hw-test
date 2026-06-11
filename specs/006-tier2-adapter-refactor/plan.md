# Implementation Plan: Tier 2 Service API Adapter Refactor

**Branch**: `006-tier2-adapter-refactor` | **Date**: 2026-05-19 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/006-tier2-adapter-refactor/spec.md`

## Summary

Refactor all four hardware adapters (GPS, Compass, SDR, HaLow) to consume Tier 2 service APIs instead of directly accessing hardware. GPS switches from pgrep-based gpsd detection to TCP connection to gps-svc:2947. Compass and SDR replace shell-command probes with gRPC clients to compass-svc:5100 and sdr-svc:5101. HaLow adopts a two-tier strategy: Tier A preserves direct hardware checks (Layers 0-3), Tier B adds REST mesh connectivity via zSCOUT-mesh:5102 (`GET /api/status`). RunOrchestrator switches from Task.WhenAll to incremental result processing. Docker container drops --privileged mode.

## Technical Context

**Language/Version**: C# (latest LangVersion), .NET 10
**Primary Dependencies**: ASP.NET Core, Blazor Server, SignalR, Grpc.Net.Client, Google.Protobuf, Grpc.Tools
**Storage**: File-backed repositories (unchanged)
**Testing**: xUnit
**Target Platform**: linux-arm64 (Raspberry Pi CM5)
**Project Type**: Blazor Server web app + CLI
**Constraints**: ARM64, resource-constrained, --network host, /sys:/sys:ro (no --privileged)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Hardware-First**: ✅ Tier A HaLow checks still directly probe hardware. GPS/Compass/SDR go through Tier 2 services that interact with real hardware.
- **II. Docker Parity**: ✅ Removing --privileged improves parity — adapters use same network-based APIs in both modes.
- **III. Actionable Diagnostics**: ✅ Each adapter returns descriptive Unavailable messages when services are unreachable.
- **IV. Structured Output**: ✅ HealthSnapshot dictionary approach preserved; expanded for HaLow Tier B.
- **V. Isolation and Independence**: ✅ Incremental result processing ensures each adapter is fully independent.
- **VI. Minimal Dependencies**: ⚠️ Adding Grpc.Net.Client + Google.Protobuf + Grpc.Tools — justified as standard zSCOUT Tier 2 communication protocol.

## Project Structure

### Documentation (this feature)

```text
specs/006-tier2-adapter-refactor/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── tasks.md             # Phase 2 output (/speckit.tasks)
└── checklists/          # Requirements quality checklist
```

### Source Code Changes

```text
src/
  ZScout.HwTest.Contracts/
    Models/Enums.cs                    # Add NotTested to PeripheralStatus
  ZScout.HwTest.App/
    Hardware/
      Common/
        TcpHealthCheck.cs              # NEW: TCP connectivity helper
        GrpcChannelFactory.cs          # NEW: Shared gRPC channel creation
      Gps/
        GpsAdapter.cs                  # MODIFY: Replace pgrep with TCP check to :2947
      Compass/
        CompassAdapter.cs              # REWRITE: gRPC client to compass-svc :5100
      Sdr/
        SdrAdapter.cs                  # REWRITE: gRPC client to sdr-svc :5101
      Halow/
        HalowAdapter.cs               # MODIFY: Add Tier B REST mesh after Tier A
    Protos/
      compass.proto                    # NEW: CompassService gRPC definition
      sdr.proto                        # NEW: SdrService gRPC definition
    Runs/
      RunOrchestrator.cs               # MODIFY: Task.WhenAll → incremental processing
tests/
  ZScout.HwTest.App.Tests/
    Hardware/
      Gps/GpsAdapterTests.cs           # NEW: TCP-based GPS adapter tests
      Compass/CompassAdapterTests.cs   # NEW: gRPC compass adapter tests
      Sdr/SdrAdapterTests.cs           # NEW: gRPC SDR adapter tests
      Halow/HalowAdapterTests.cs       # MODIFY: Add Tier B tests
    Runs/RunOrchestratorTests.cs       # NEW: Incremental processing tests
deploy/
  Dockerfile                           # MODIFY: Remove i2c-tools, keep iw/usbutils/kmod
  docker-compose.yml                   # MODIFY: Remove privileged, remove /dev mount
```

**Structure Decision**: Existing project structure preserved. New files added for gRPC infrastructure (protos, helpers). No new projects — gRPC packages added to ZScout.HwTest.App.csproj.

## Design Decisions

### D1: GPS — TCP connect instead of pgrep
Replace `pgrep -x gpsd` with a TCP socket connection attempt to `localhost:2947` (configurable). Use `gpspipe -w -l localhost` to connect to gps-svc container's TCP socket. Fixes #23 Bug 1.

### D2: Compass/SDR — gRPC clients with .proto definitions
Define minimal .proto files for compass-svc and sdr-svc. Use Grpc.Net.Client for typed service stubs. Proto files in `src/ZScout.HwTest.App/Protos/`, compiled with Grpc.Tools at build time.

### D3: HaLow — Two-tier with graceful Tier B fallback
Tier A (Layers 0-3) preserved exactly as-is. Tier B appended: REST call to mesh `GET /api/status` on :5102 with 5s timeout. If unavailable, report NotTested. New snapshot fields: mesh_service_available, mesh_associated, peer_count, gateway_mode, bat0_ip, internet_reachable.

### D4: RunOrchestrator — Incremental result processing
Replace `Task.WhenAll` + post-loop with processing each adapter's result as it completes. Evidence is saved and status published immediately per adapter. Overall run completion after all adapters finish.

### D5: Docker — Drop --privileged, keep /sys:ro
Remove `privileged: true` and `/dev:/dev:ro`. Keep `/sys:/sys:ro` for HaLow Tier A. Keep `--network host` for TCP/gRPC/REST.

## Complexity Tracking

No constitution violations requiring justification. The gRPC dependency addition (VI) is the standard communication pattern in the zSCOUT service architecture.
