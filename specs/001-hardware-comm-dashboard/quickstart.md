# Quickstart: Hardware Communication Dashboard

## 1. Preconditions

- CM5 + CM5-IO-BASE-B is provisioned and reachable via SSH.
- Required peripherals are connected: MG-A01 GPS, uSDR, MM8108 HaLow, QMC5883L compass.
- Docker Engine is installed and running on CM5.
- Device runtime uses production-like settings (`privileged: true`, `network_mode: host`).

## 2. Build and publish linux-arm64 image

```bash
docker buildx build --platform linux/arm64 -t ghcr.io/bulzi-org/zscout-hw-test:latest --push .
```

## 3. Offline transfer flow (optional)

```bash
docker pull ghcr.io/bulzi-org/zscout-hw-test:latest
docker save ghcr.io/bulzi-org/zscout-hw-test:latest -o zscout-hw-test.tar
scp zscout-hw-test.tar pi@<cm5-ip>:~/
ssh pi@<cm5-ip> "docker load -i ~/zscout-hw-test.tar"
```

## 4. Run in container mode (dashboard + APIs)

```bash
docker run --rm -it \
  --name zscout-hw-test \
  --privileged \
  --network host \
  -v /var/lib/zscout-hw-test:/var/lib/zscout-hw-test \
  ghcr.io/bulzi-org/zscout-hw-test:latest
```

Expected: dashboard available at `http://<cm5-ip>:5000`.

## 5. Validate host mode parity

Run the same suite directly on host (outside container) with equivalent configuration values.

```bash
dotnet run --project src/ZScout.HwTest.Cli -- run --mode host --config /etc/zscout-hw-test/config.json
```

Expected: one run result for host mode and one for container mode available in history.

## 6. Dashboard acceptance path

1. Log in with local account.
2. Confirm peripheral status tiles show live health.
3. Start a full-suite run in container mode.
4. Observe raw stream panels updating for GPS, SDR, HaLow, and compass.
5. Submit manual per-peripheral verdicts (failures require reason text).
6. Verify completed run appears in history.
7. Trigger manual export for an in-range date window.

## 7. Expected failure handling checks

- If a second run is requested during an active run, request is rejected immediately with a clear reason.
- If one dependency service is unavailable, only impacted peripheral checks are flagged unavailable/failed, while other peripherals continue.
- If dashboard reconnects after interruption, current run status and stream data resume.