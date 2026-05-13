# zSCOUT-hw-test Constitution

## Core Principles

### I. Hardware-First Validation

Every test MUST interact with real hardware or clearly declare itself a mock/stub. Tests are only authoritative when run on a live CM5 + CM5-IO-BASE-B system. Simulated results must be clearly labeled and never treated as pass/fail evidence for hardware readiness.

### II. Docker Parity (NON-NEGOTIABLE)

The primary purpose of this project is to prove that Docker containers can communicate with hardware peripherals. Every hardware test MUST have two execution modes:
- **Host mode**: runs natively on the CM5 to establish a baseline
- **Container mode**: runs inside a Docker container with the same device mappings used by the production `docker-compose.yml` in `zSCOUT-image-CM5`

If a test passes on the host but fails in the container, that is a critical finding — not a test infrastructure problem.

### III. Actionable Diagnostics

Test failures MUST produce actionable output: the specific device path, kernel module, or driver that is missing or misconfigured. A bare "FAIL" is never acceptable. Every failure message must guide the operator toward resolution — e.g., "morse_driver not loaded: run `modprobe morse`" or "/dev/ttyUSB0 not found: check FT232 USB connection."

### IV. Structured Output

All test results MUST be available in both human-readable (terminal) and machine-readable (JSON) formats. JSON output enables automated CI gating and cross-run comparison. Human output enables quick field diagnosis over SSH.

### V. Isolation and Independence

Each hardware test module (GPS, uSDR, MM8108, compass) MUST be independently runnable. A failure in one module must not prevent the others from executing. Test orchestration runs all modules and aggregates results, but each module stands alone.

### VI. Minimal Dependencies

Tests run on resource-constrained ARM64 hardware. The .NET runtime is the sole managed-code dependency. Native tool invocations (gpsd-clients, SoapySDRUtil, i2c-tools) already present in the zSCOUT base image are called via `System.Diagnostics.Process`. Do not introduce additional heavy frameworks beyond xUnit for testing and Blazor Server for the web dashboard.

## Technology Stack (NON-NEGOTIABLE)

- **Language**: C# 13
- **Runtime**: .NET 10 (LTS) — `mcr.microsoft.com/dotnet/runtime:10.0` and `mcr.microsoft.com/dotnet/sdk:10.0` base images
- **Target architecture**: `linux-arm64` (Raspberry Pi CM5 / BCM2712)
- **Test framework**: xUnit (for unit and integration tests within the solution)
- **Web dashboard**: Blazor Server (ASP.NET Core) with SignalR for real-time updates
- **Build**: `dotnet publish -r linux-arm64 --self-contained false` (framework-dependent, runtime provided by Docker image)
- **Container**: Multi-stage Dockerfile — SDK image for build, ASP.NET runtime image for execution

## Web Dashboard

The project includes a Blazor Server web dashboard accessible at `http://<cm5-ip>:5000`. The dashboard provides:

1. **Live hardware status** — real-time view of each peripheral's connectivity and health, updated via SignalR
2. **Test execution control** — start/stop individual test modules or the full suite from the browser
3. **Test configuration** — adjust test parameters (timeouts, device paths, polling intervals) without redeploying
4. **Raw data streams** — live views of:
   - GPS: NMEA sentence stream and parsed position/fix data
   - uSDR: SoapySDR device info, sample rate, gain settings
   - MM8108: Wi-Fi HaLow interface status, signal metrics, module state
   - Compass: heading, raw magnetometer X/Y/Z readings
5. **Test history** — results from previous runs with pass/fail trends and diagnostic details

The dashboard runs inside the same Docker container as the test suite. It is the primary user interface; CLI mode remains available for headless/CI use.

## Docker Image Delivery

The project produces a single Docker image (`zscout-hw-test`) that runs the test suite and web dashboard. Three delivery methods MUST be supported:

1. **Container Registry (GHCR)** — Primary method when internet is available
   - Image published to `ghcr.io/bulzi-org/zscout-hw-test` via GitHub Actions
   - On CM5: `docker pull ghcr.io/bulzi-org/zscout-hw-test:latest && docker run ...`
   - Cross-built with `docker buildx --platform linux/arm64`

2. **Offline Transfer (`docker save` / `docker load`)** — For field/air-gapped use
   - Dev machine: `docker save zscout-hw-test:latest -o zscout-hw-test.tar`
   - Transfer via SCP over WiFi: `scp zscout-hw-test.tar pi@<cm5-ip>:~/`
   - On CM5: `docker load -i zscout-hw-test.tar`
   - A convenience script (`scripts/export-image.sh`) automates the save+transfer

3. **Baked into SD Card Image** — For zero-touch deployment
   - `zSCOUT-image-CM5` pi-gen build pre-loads the test image tarball
   - Image available immediately after flashing without pull or transfer
   - Coordination: provide a `deploy/zscout-hw-test.tar` artifact for the image builder

## Hardware Constraints

- **Target platform**: Raspberry Pi CM5 Lite (BCM2712, ARM64, no eMMC) on Waveshare CM5-IO-BASE-B carrier board
- **Boot media**: microSD card, headless Raspberry Pi OS Lite (Bookworm)
- **Peripherals under test**:
  - MicoAir MG-A01 GPS via Waveshare FT232 USB-to-TTL → `gpsd`
  - Wavelet-Lab uSDR (LMS6002D) via M.2 A+E→M-key adapter → LimeSuite / SoapySDR (USB + PCIe)
  - Morse Micro MM8108 Wi-Fi HaLow → `morse_driver` kernel module
  - QMC5883L compass → I2C bus 1
- **Docker runtime**: Docker Engine + Docker Compose, containers use `privileged: true` and `network_mode: host` per production config
- **Network access**: WiFi + Bluetooth for SSH; internet availability varies by deployment

## Development Workflow

- All development follows the Spec Kit (SDD) methodology: constitution → specify → plan → tasks → implement
- Each SpecKit phase is committed and pushed independently for traceability
- Feature branches follow the pattern `NNN-feature-name` (e.g., `001-hw-comm-test`)
- AI-assisted commits do not require special attribution lines
- Tests are validated on real hardware before merging to main

## Governance

This constitution supersedes all other development practices in this repository. Amendments require documentation in a commit message explaining the rationale. All PRs must verify compliance with these principles.

**Version**: 1.2.0 | **Ratified**: 2026-05-09 | **Last Amended**: 2026-05-09
