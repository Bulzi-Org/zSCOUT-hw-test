# Hardware Communication Dashboard — CM5 Validation Evidence

**Feature**: 001-hardware-comm-dashboard  
**Target**: Raspberry Pi CM5 + CM5-IO-BASE-B  
**Date**: _fill in_  
**Operator**: _fill in_  
**zSCOUT image tag**: _fill in_ (e.g. `ghcr.io/bulzi-org/zscout-hw-test:latest`)  
**Host OS / kernel**: _fill in_ (e.g. `Linux cm5 6.12.0-rpi-2712 #1 SMP`)

---

## Pre-flight checklist

- [ ] `gpsd` is running (`systemctl status gpsd`)
- [ ] GPS antenna has sky view (or simulator connected)
- [ ] uSDR is plugged in via USB (`lsusb | grep -i wavelet`)
- [ ] `SoapySDRUtil --find` returns at least one device
- [ ] `morse_driver` is loaded (`lsmod | grep morse`)
- [ ] HaLow interface visible (`ip link | grep wlan`)
- [ ] QMC5883L detectable (`i2cdetect -y 1 | grep 0d`)
- [ ] Docker is running (`docker info`)
- [ ] `/dev` and `/sys` accessible inside container (`docker run --privileged ...`)

---

## 1. Image load / build

```
Command: docker pull ghcr.io/bulzi-org/zscout-hw-test:latest
         # OR: bash deploy/export-image.sh && gunzip -c *.tar.gz | docker load

Output:
[paste docker pull / load output here]

Image ID:
[paste docker image ls output here]
```

---

## 2. Health check

```
Command: curl -s http://localhost:5000/health

Expected: Healthy
Actual:   [paste response here]
Result:   [ ] PASS  [ ] FAIL
```

---

## 3. Host-mode CLI run

```
Command:
  docker run --rm --privileged --network host \
    -v /dev:/dev:ro -v /sys:/sys:ro \
    ghcr.io/bulzi-org/zscout-hw-test:latest \
    dotnet ZScout.HwTest.Cli.dll --mode Host --format json

Exit code: [0 = pass / 1 = fail]

Output (JSON):
[paste full JSON output here]
```

### Per-peripheral results (host mode)

| Peripheral | DependencyAvailable | Status | SampleCount | Notes |
|---|---|---|---|---|
| GPS | | | | |
| SDR | | | | |
| HaLow | | | | |
| Compass | | | | |

---

## 4. Container-mode CLI run

```
Command:
  docker run --rm --privileged --network host \
    -v /dev:/dev:ro -v /sys:/sys:ro \
    ghcr.io/bulzi-org/zscout-hw-test:latest \
    dotnet ZScout.HwTest.Cli.dll --mode Container --format json

Exit code: [0 = pass / 1 = fail]

Output (JSON):
[paste full JSON output here]
```

### Per-peripheral results (container mode)

| Peripheral | DependencyAvailable | Status | SampleCount | Notes |
|---|---|---|---|---|
| GPS | | | | |
| SDR | | | | |
| HaLow | | | | |
| Compass | | | | |

---

## 5. Parity smoke test

```
Command: bash scripts/run-parity-smoke.sh

Output:
[paste output here]

Diff (host vs container peripheral statuses):
[paste diff here — should be empty / "No differences"]

Result: [ ] PASS  [ ] FAIL
```

---

## 6. Dashboard UI validation

```
URL: http://<cm5-ip>:5000

[ ] Login page renders at /login
[ ] Invalid credentials show error
[ ] Valid admin login redirects to /
[ ] Control page shows 4 peripheral status cards
[ ] Start Run (Host mode) → status transitions: Queued → Running → AwaitingVerdict
[ ] All 4 verdict assignment panels appear after run completes
[ ] Assigning Pass/Fail for each peripheral → run status → Completed
[ ] /streams shows telemetry records for the completed run
[ ] /history shows the completed run in the table
[ ] Run row expand shows evidence and verdicts
[ ] Export ZIP downloads from /history export panel
[ ] /settings page accessible (Operator role)
[ ] Logout clears session → redirected to /login
```

---

## 7. API spot-checks

```
# Auth
curl -c cookies.txt -X POST http://localhost:5000/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"zscout"}'

Expected: 200, JSON with userId/username/role
Actual:   [paste]

# GET /api/peripherals
curl -b cookies.txt http://localhost:5000/api/peripherals
Expected: 200, array of 4 peripheral status objects
Actual:   [paste]

# GET /api/history
curl -b cookies.txt http://localhost:5000/api/history
Expected: 200, paginated run list
Actual:   [paste]
```

---

## 8. Retention & export

```
[ ] /api/exports POST with valid date range → 200 + ExportJob with status=Ready
[ ] /api/exports/{jobId}/download returns a .zip file
[ ] ZIP contains: runs.json, evidence.json, verdicts.json, telemetry.json
[ ] /api/exports POST with out-of-retention date range → 400

Notes:
[paste any notes here]
```

---

## 9. Correlation ID verification

```
Command: curl -v -H 'X-Correlation-ID: test-corr-001' http://localhost:5000/api/health

Expected response header: X-Correlation-ID: test-corr-001
Actual: [paste curl -v output here]

Result: [ ] PASS  [ ] FAIL
```

---

## 10. Overall outcome

| Check | Result |
|---|---|
| Image loads / starts | |
| Health endpoint | |
| Host-mode CLI | |
| Container-mode CLI | |
| Host == Container parity | |
| Dashboard UI | |
| API endpoints | |
| Retention / Export | |
| Correlation ID echo | |

**Overall**: [ ] PASS  [ ] FAIL  [ ] PARTIAL

---

## Remediation notes

_Document any failures, root causes, and fixes applied:_

```
[paste remediation notes here]
```

---

## Sign-off

| Role | Name | Date | Signature |
|---|---|---|---|
| Operator | | | |
| Reviewer | | | |
