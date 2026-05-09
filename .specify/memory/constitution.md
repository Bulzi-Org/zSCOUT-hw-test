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

Tests run on resource-constrained ARM64 hardware. Dependencies must be minimal and justified. Prefer standard library and tools already present in the zSCOUT base image (Python 3, gpsd-clients, SoapySDR, i2c-tools). Do not introduce heavy test frameworks.

## Hardware Constraints

- **Target platform**: Raspberry Pi CM5 Lite (BCM2712, ARM64, no eMMC) on Waveshare CM5-IO-BASE-B carrier board
- **Boot media**: microSD card, headless Raspberry Pi OS Lite (Bookworm)
- **Peripherals under test**:
  - MicoAir MG-A01 GPS via Waveshare FT232 USB-to-TTL → `gpsd`
  - Wavelet-Lab uSDR (LMS6002D) via M.2 A+E→M-key adapter → LimeSuite / SoapySDR (USB + PCIe)
  - Morse Micro MM8108 Wi-Fi HaLow → `morse_driver` kernel module
  - QMC5883L compass → I2C bus 1
- **Docker runtime**: Docker Engine + Docker Compose, containers use `privileged: true` and `network_mode: host` per production config
- **Network access**: WiFi + Bluetooth for SSH; no guaranteed internet during testing

## Development Workflow

- All development follows the Spec Kit (SDD) methodology: constitution → specify → plan → tasks → implement
- Each SpecKit phase is committed and pushed independently for traceability
- Feature branches follow the pattern `NNN-feature-name` (e.g., `001-hw-comm-test`)
- Every commit includes `Co-Authored-By: Oz <oz-agent@warp.dev>` when AI-assisted
- Tests are validated on real hardware before merging to main

## Governance

This constitution supersedes all other development practices in this repository. Amendments require documentation in a commit message explaining the rationale. All PRs must verify compliance with these principles.

**Version**: 1.0.0 | **Ratified**: 2026-05-09 | **Last Amended**: 2026-05-09
