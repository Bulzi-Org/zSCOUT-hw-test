# zSCOUT Hardware Communication Test Suite

A Blazor Server dashboard and CLI tool that verifies all peripherals on the
**Raspberry Pi Compute Module 5 (CM5)** + CM5-IO-BASE-B carrier board can be
communicated with from Docker containers running on the zSCOUT base image.

## Hardware targets

| Peripheral | Interface | Dependency |
|---|---|---|
| MicoAir MG-A01 GPS | UART → gpsd | `gpspipe` |
| Wavelet-Lab uSDR | USB → SoapySDR | `SoapySDRUtil` |
| Morse Micro MM8108 Wi-Fi HaLow | PCIe → morse_driver | `lsmod`, `ip link` |
| QMC5883L compass | I²C (0x0d) | `i2cdetect`, `i2cget` |

## Quick start (CM5, online)

```bash
# Pull the image from GHCR
docker pull ghcr.io/bulzi-org/zscout-hw-test:latest

# Run the dashboard (host networking required for hardware access)
docker run --rm \
  --privileged \
  --network host \
  -v /dev:/dev:ro \
  -v /sys:/sys:ro \
  -v "$(pwd)/data:/app/data" \
  ghcr.io/bulzi-org/zscout-hw-test:latest
```

Browse to `http://<cm5-ip>:5000` to access the dashboard.

## Quick start (docker compose)

```bash
cd deploy/
docker compose up
```

See [deploy/README.md](deploy/README.md) for full deployment options including
offline (air-gapped) operation and SD-card bake-in.

## CLI (headless / CI)

Run the test suite without the dashboard:

```bash
# Host mode
docker run --rm --privileged --network host \
  -v /dev:/dev:ro -v /sys:/sys:ro \
  ghcr.io/bulzi-org/zscout-hw-test:latest \
  dotnet ZScout.HwTest.Cli.dll --mode Host

# Container mode (default)
docker run --rm --privileged --network host \
  -v /dev:/dev:ro -v /sys:/sys:ro \
  ghcr.io/bulzi-org/zscout-hw-test:latest \
  dotnet ZScout.HwTest.Cli.dll --mode Container --format json
```

Exit codes: `0` = all peripherals ready, `1` = one or more unavailable/degraded.

## Parity smoke test

Verify host-mode and container-mode give identical peripheral statuses:

```bash
bash scripts/run-parity-smoke.sh
```

## Development

### Prerequisites

- .NET 10 SDK (`dotnet --version` → `10.0.x`)
- Docker 24+ with buildx

### Build

```bash
dotnet build zSCOUT-hw-test.slnx
```

### Run locally (dashboard)

```bash
cd src/ZScout.HwTest.App
dotnet run
```

### Run tests

```bash
dotnet test
```

## Project structure

```
src/
  ZScout.HwTest.Contracts/   # Shared domain models (enums, records)
  ZScout.HwTest.App/         # Blazor Server dashboard + REST API + orchestrator
  ZScout.HwTest.Cli/         # Headless CLI runner
deploy/
  Dockerfile                 # Multi-stage ARM64 build
  docker-compose.yml         # Compose file for CM5 deployment
  export-image.sh            # Offline image save/load helper
specs/                       # Speckit design artifacts
scripts/
  run-parity-smoke.sh        # Host vs container parity test
docs/
  validation/                # On-device validation evidence
```

## API

The dashboard exposes a REST API documented in
[specs/001-hardware-comm-dashboard/contracts/dashboard-api.yaml](specs/001-hardware-comm-dashboard/contracts/dashboard-api.yaml).

## License

See [LICENSE](LICENSE).
