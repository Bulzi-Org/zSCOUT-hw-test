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

## Deploy to CM5 hardware

SSH into your CM5 running the zSCOUT base image and run this single command to
pull and start all services (hw-test dashboard + GPS, compass, and SDR services):

```bash
curl -fsSL https://raw.githubusercontent.com/Bulzi-Org/zSCOUT-hw-test/main/scripts/deploy-cm5.sh | bash
```

This will:
1. Create `/opt/zscout/hw-test/` with the docker-compose configuration
2. Pull all four container images from GHCR (arm64)
3. Start Tier 2 services (gps-svc, compass-svc, sdr-svc) with health checks
4. Start the hw-test dashboard once all dependencies are healthy
5. Print the dashboard URL

Browse to `http://<cm5-ip>:5000` to access the dashboard.

### Manual step-by-step

If you prefer to run each step individually:

```bash
# 1. Create deployment directory
sudo mkdir -p /opt/zscout/hw-test/{data,logs}

# 2. Download the compose file
curl -fsSL -o /opt/zscout/hw-test/docker-compose.yml \
  https://raw.githubusercontent.com/Bulzi-Org/zSCOUT-hw-test/main/deploy/docker-compose.yml

# 3. Pull all images
docker compose -f /opt/zscout/hw-test/docker-compose.yml pull

# 4. Start all services
docker compose -f /opt/zscout/hw-test/docker-compose.yml up -d

# 5. Check status
docker compose -f /opt/zscout/hw-test/docker-compose.yml ps
```

### Update to latest

```bash
# Re-run the deploy script (pulls latest images and restarts)
curl -fsSL https://raw.githubusercontent.com/Bulzi-Org/zSCOUT-hw-test/main/scripts/deploy-cm5.sh | bash

# Or manually:
docker compose -f /opt/zscout/hw-test/docker-compose.yml pull
docker compose -f /opt/zscout/hw-test/docker-compose.yml up -d
```

### Service architecture

```
CM5 (host network)
├── gps-svc        :5200 REST + :2947 gpsd   /dev/ttyUSB0
├── compass-svc    :5100 REST                /dev/i2c-1
├── sdr-svc        :5101 REST                /dev/bus/usb
└── zscout-hw-test :5000 dashboard + API     (depends on all above)
```

See [deploy/README.md](deploy/README.md) for offline (air-gapped) operation and
SD-card bake-in options.

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
