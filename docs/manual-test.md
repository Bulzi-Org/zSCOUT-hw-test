# Manual Hardware Test Guide

Step-by-step commands for verifying each peripheral from both the **Base OS
shell** and the **container shell**, on a CM5 running `zSCOUT-image-CM5`.

---

## Context and terminology

| Term | Meaning |
|------|---------|
| **Base OS shell** | SSH session directly on the CM5 host (`zSCOUT-image-CM5` or vanilla RPi OS) — no container involved |
| **Container shell** | Shell obtained via `docker exec` inside the running `zscout-hw-test` container |
| **PASS** | The command exits 0 and its output matches the expected pattern shown below |
| **FAIL** | The command exits non-zero, produces empty output, or the expected pattern is absent |

The container uses `network_mode: host` and `privileged: true`, so it shares
the host network stack and has full access to `/dev` and `/sys`. Most commands
produce identical output from both contexts; exceptions are noted per section.

---

## 0 — Getting into the container shell

```bash
# Verify the container is running
docker ps --filter name=zscout-hw-test

# Open an interactive shell inside the container
docker exec -it zscout-hw-test /bin/bash
```

All commands in the **"Container shell"** sections below assume you have run
the `docker exec` above and are at the container's `bash` prompt.

---

## 1 — GPS (MicoAir MG-A01 via Waveshare FT232 USB-to-TTL → gpsd)

### Physical check

Confirm the FT232 USB-to-TTL adapter is plugged into a USB port on the
CM5-IO-BASE-B and that the GPS module's TX pin is connected to the adapter's RX
pin.

```bash
# Both contexts — verify the USB serial adapter is enumerated by the kernel
lsusb | grep -i "ftdi\|ft232\|future technology"
# Expected: one line like: Bus 001 Device 003: ID 0403:6001 Future Technology Devices International, Ltd FT232 Serial (UART) IC

# Identify the assigned serial device
ls /dev/ttyUSB* /dev/ttyAMA* 2>/dev/null
# Expected: at least /dev/ttyUSB0 (FT232) or /dev/ttyAMA0 (UART)
```

### Base OS shell

```bash
# 1. Confirm gpsd is running
pgrep -x gpsd && echo PASS || echo FAIL
# Expected: a PID number, then PASS

# 2. Verify gpsd is listening on its socket
ss -lnp | grep gpsd
# Expected: one line showing LISTEN on *:2947 and/or /tmp/gpsd.sock

# 3. Capture 5 raw NMEA sentences (10-second timeout)
gpspipe -r -n 5 -w
# Expected: lines starting with '$' such as:
#   $GPRMC,123456.00,A,1234.5678,N,09876.5432,W,0.01,0.00,130526,,,A*XX
#   $GPGGA,123456.00,1234.5678,N,09876.5432,W,1,08,1.00,100.0,M,...
# FAIL indicators: empty output, "gpspipe: Unable to connect to gpsd" after timeout

# 4. Check /etc/default/gpsd for the correct device assignment
grep DEVICES /etc/default/gpsd
# Expected: DEVICES="/dev/ttyUSB0"  (or whichever port you configured)
```

### Container shell

```bash
# The container connects to the host's gpsd daemon over localhost:2947
# (shared via network_mode: host), so all gpsd commands work identically.

# 1. Confirm gpsd process is visible (container sees host processes)
pgrep -x gpsd && echo PASS || echo FAIL

# 2. Capture 5 NMEA sentences
gpspipe -r -n 5 -w
# Expected: same '$'-prefixed sentences as above
# FAIL: "gpspipe: Unable to connect to gpsd" — gpsd is not running on the host

# 3. Read one raw NMEA sentence (quick spot-check)
gpspipe -r -n 1 -w
# Expected: one line starting with '$'
```

### What the app tests (reference)

The app runs `pgrep -x gpsd` and, if the daemon is found, `gpspipe -r -n 5 -w`.
It counts lines starting with `$`. Status is `Ready` when at least one NMEA
sentence is captured; `Degraded` when gpsd is running but returns no sentences
(antenna/fix issue); `Unavailable` when gpsd is not running.

---

## 2 — SDR (Wavelet-Lab uSDR LMS6002D via M.2 A+E → M-key adapter)

### Physical check

Confirm the uSDR is seated in the M.2 slot and that the M.2 A+E → M-key
adapter is fitted. On the CM5-IO-BASE-B the slot is accessible without the
case.

```bash
# Both contexts — verify USB or PCIe enumeration
lsusb | grep -i "lime\|lms\|wavelet\|usdr"
# OR for PCIe-attached variants:
lspci | grep -i "lime\|wavelet\|usdr"
# Expected: one matching line (exact ID varies by uSDR revision)
```

### Base OS shell

```bash
# 1. List SoapySDR devices
SoapySDRUtil --find
# Expected output contains lines like:
#   Found device 0
#     driver = lime
#     label  = LimeSDR ...
# FAIL: "No devices found" — driver not loaded or device not seated

# 2. Probe device capabilities (takes ~5 s)
SoapySDRUtil --probe
# Expected: long listing of RX/TX channels, sample rates, gains, antennas
# FAIL: exits non-zero or "No devices found"

# 3. Confirm the SoapySDR Lime plugin is installed
SoapySDRUtil --info | grep -i lime
# Expected: one line referencing the Lime module path

# 4. Quick USB device deep-dive (if USB-attached)
lsusb -v 2>/dev/null | grep -A5 -i "lime\|wavelet"
```

### Container shell

```bash
# The container has SoapySDRUtil installed and /dev bind-mounted (privileged).
# USB device nodes (/dev/bus/usb/...) are visible inside the container.

# 1. List SoapySDR devices
SoapySDRUtil --find
# Expected: same output as host — "Found device 0, driver = lime ..."
# FAIL: "No devices found" — check lsusb on the host; kernel driver not bound

# 2. Probe device capabilities
SoapySDRUtil --probe
# Expected: same channel / sample-rate listing as on host

# 3. Verify /dev/bus/usb is accessible
ls /dev/bus/usb/
# Expected: bus directories 001 002 ...
```

### What the app tests (reference)

The app runs `SoapySDRUtil --find` and checks that stdout contains the word
`driver`. If found, it runs `SoapySDRUtil --probe`. Status is `Ready` when
probe exits 0; `Degraded` when found but probe fails; `Unavailable` when no
SoapySDR device is enumerated.

In Host/Run mode the app (SdrAdapter.ProbeAsync + SdrCaptureValidator) now also
exercises the full sdr-svc REST surface including the new raw capture path
(`/api/rx/capture?center_freq_hz=...&bandwidth_hz=...`) with graceful 404s.
See the "/manual-tests" dashboard page for the "SDR Auto-Discover + Raw Waveform
Capture Validator" panel (on-demand button that auto-selects an active uplink
candidate, returns HEX prefix of the captured float IQ, tx count + RSSI/SNR
range stats computed client-side, and GPS+time per burst).

---

## 3 — HaLow (Morse Micro MM8108 via morse_driver kernel module)

### Physical check

The MM8108 is integrated onto the CM5-IO-BASE-B. No physical insertion is
needed, but confirm the board revision supports HaLow and that the antenna is
attached.

### Base OS shell

```bash
# 1. Check the morse kernel module is loaded
lsmod | grep morse
# Expected: a line like:
#   morse_driver        XXXXX  0
# FAIL: no output — module not loaded

# 2. If not loaded, check it is available in the kernel module tree
modinfo morse_driver
# Expected: filename, description, license, etc.
# FAIL: "ERROR: Module morse_driver not found" — driver not installed

# 3. If available but not loaded, load it
sudo modprobe morse_driver
lsmod | grep morse   # should now appear

# 4. Confirm a HaLow network interface was registered
ip link show
# Expected: an entry containing 'morse' or 'wlan' created by the driver, e.g.:
#   3: wlan0: <BROADCAST,MULTICAST> mtu 1500 qdisc noop state DOWN ...
#      link/ether aa:bb:cc:dd:ee:ff brd ff:ff:ff:ff:ff:ff

# 5. Check sysfs device tree for MM8108
find /sys/bus/sdio/devices /sys/bus/pci/devices -name '*morse*' 2>/dev/null
# Expected: one or more sysfs paths, e.g. /sys/bus/sdio/devices/mmc0:0001:1
```

### Tier B — Mesh + internet via MeshGate (dashboard)

When `zscout-mesh` is running (hw-test deploy stack), the HaLow adapter probes
`GET http://localhost:5102/api/status` for Tier B mesh connectivity.

**Success criteria (Tier B PASS):**

- `mesh_service_available` = true
- `associated` = true
- `peer_count` >= 1
- `internet_reachable` = true (probe bound through `bat0`, not management WiFi)

Tier A may show **Degraded** if the RF scan does not see nodes independently;
Tier B PASS is the milestone for mesh-backhaul internet validation.

**Per-node mesh config** (`/opt/zscout/hw-test/.env`):

```bash
MESH_KEY=zMesh-01
MESH_NODE_IP=10.41.0.2/16    # unique per CM5 (.3, .4, ...)
MESH_DEFAULT_ROUTE_METRIC=0  # mesh-only backhaul (no Ethernet)
```

**Cross-check on CM5:**

```bash
curl -s http://localhost:5102/api/status | jq .
ip route show default
# Expected default route: via 10.41.0.1 dev bat0
```

### Container shell

```bash
# The container shares the host kernel, host /sys, and host network interfaces
# via network_mode: host and NET_ADMIN (HaLow scan only).

# 1. Check module is loaded (reads /proc/modules — shared with host)
lsmod | grep morse
# Expected: same output as host
# FAIL: no output — module not loaded on HOST (cannot load from container)

# 2. Confirm interface is visible
ip link show | grep -E "morse|wlan"
# Expected: same interface line as on host

# 3. Inspect sysfs from inside the container
find /sys/bus/sdio/devices /sys/bus/pci/devices -name '*morse*' 2>/dev/null
# Expected: same sysfs paths as on host

# NOTE: If morse_driver is not loaded, you cannot fix it from inside the
# container. Log out and run 'sudo modprobe morse_driver' on the host.
```

### What the app tests (reference)

The app runs `lsmod` and checks for `morse` in the output. If found, it runs
`ip link show` and looks for a `morse` or `wlan` interface. It also queries
`find /sys/bus/sdio/devices /sys/bus/pci/devices -name '*morse*'`. Status is
`Ready` when the interface is found; `Degraded` when the module is loaded but
no interface appears; `Unavailable` when the module is not loaded.

---

## 4 — Compass (QMC5883L magnetometer via I²C bus 1)

### Physical check

The QMC5883L is connected to the CM5-IO-BASE-B I²C header. Confirm SDA and
SCL lines are wired to GPIO 2 (SDA) and GPIO 3 (SCL) with 4.7 kΩ pull-ups.
The chip's default address is `0x0D`.

### Base OS shell

```bash
# 1. Confirm the I2C bus device node exists
ls /dev/i2c-*
# Expected: /dev/i2c-1  (I²C bus 1 on CM5)
# FAIL: no output — I²C not enabled (run: sudo raspi-config nonint do_i2c 0)

# 2. Scan the bus for connected devices
i2cdetect -y 1
# Expected: a grid where address 0d is marked (shown as '0d' or 'UU'):
#      0  1  2  3  4  5  6  7  8  9  a  b  c  d  e  f
# 00:          -- -- -- -- -- -- -- -- -- -- -- 0d -- --
# FAIL: '0d' absent — wiring issue or chip not powered

# 3. Read the Status register (0x06) — proof of I²C communication
i2cget -y 1 0x0d 0x06
# Expected: a hex byte, e.g.: 0x00 or 0x04 (DRDY bit)
# FAIL: "Error: Read failed" — chip not responding

# 4. Read the X-axis LSB (0x00) for a live magnetometer sample
i2cget -y 1 0x0d 0x00
# Expected: a hex byte representing the low byte of the X-axis reading

# 5. (Optional) Read all six data registers in a burst
i2cdump -y 1 0x0d b 0x00 0x05
# Expected: six bytes of raw magnetometer data
```

### Container shell

```bash
# The container has access to /dev/i2c-* through the /dev bind-mount (privileged).
# All i2c-tools commands work identically to the host.

# 1. Verify the I2C device node is visible inside the container
ls /dev/i2c-*
# Expected: /dev/i2c-1
# FAIL: missing — host does not have I²C enabled, or /dev mount is not working

# 2. Scan the bus
i2cdetect -y 1
# Expected: 0d visible in the grid (same as host)

# 3. Read Status register
i2cget -y 1 0x0d 0x06
# Expected: hex byte (e.g. 0x00)

# 4. Read X-axis LSB
i2cget -y 1 0x0d 0x00
# Expected: hex byte
```

### What the app tests (reference)

The app checks that `/dev/i2c-1` exists (configurable via
`Peripherals:Compass:I2cBus`), runs `i2cdetect -y 1` and looks for `0d` in
the output, then runs `i2cget -y 1 0x0d 0x06`. Status is `Ready` when the
register read succeeds; `Unavailable` when the bus device is absent or the chip
is not detected; `Degraded` when detected but `i2cget` fails.

---

## 5 — Full automated test run via CLI

These commands exercise all four peripherals in sequence and produce the same
verdicts the dashboard shows.

### From the Base OS shell (host mode)

```bash
# Human-readable output
docker exec -it zscout-hw-test \
  dotnet ZScout.HwTest.Cli.dll --mode container

# JSON output (for scripting / CI)
docker exec -it zscout-hw-test \
  dotnet ZScout.HwTest.Cli.dll --mode container --format json
```

### Meaning of --mode

| Value | When to use |
|-------|-------------|
| `host` | Run the CLI directly on the Base OS (outside container) — uses host tool paths |
| `container` | Run the CLI inside the container — verifies Docker parity |

> `--mode host` is only meaningful when running the CLI binary natively on the
> Base OS, not via `docker exec`. Use `--mode container` for all `docker exec`
> invocations.

### Expected human output (all pass)

```
[zSCOUT] Run a1b2c3d4e5f6  mode=Container  2026-05-13T21:00:00Z
──────────────────────────────────────────────────────────────────
  GPS       ✓ READY    gpsd process found | Captured 5 NMEA sentence(s)
  SDR       ✓ READY    SoapySDR device enumerated | --probe succeeded
  HaLow     ✓ READY    morse kernel module loaded | HaLow interface found
  Compass   ✓ READY    QMC5883L detected at 0x0d | Register 0x06: 0x00
──────────────────────────────────────────────────────────────────
RESULT: PASS  (4/4 peripherals ready)
```

### Checking logs after a run

```bash
# Last 50 lines of structured JSON logs
docker compose logs --tail=50 zscout-hw-test

# Filter for run lifecycle events only
docker compose logs zscout-hw-test 2>&1 | grep -E '"RunId"|"EventType"'

# Filter for any errors or warnings
docker compose logs zscout-hw-test 2>&1 | grep -E '"logLevel":"Warning|Error"'
```

---

## 6 — Dashboard smoke test

After `docker compose up -d`:

1. Open `http://<cm5-ip>:5000` in a browser.
2. Navigate to **Control** → click **Start Run** (Container mode).
4. Watch the live status tiles update in real time via SignalR.
5. Navigate to **Streams** → select the completed run → verify NMEA, SDR info, and I²C rows are populated.
6. Navigate to **History** → confirm the run appears with a `Pass` or `Fail` verdict.
7. Check `http://<cm5-ip>:5000/health` returns `{"status":"Healthy"}`.

---

## 7 — Quick-reference pass/fail table

| Check | Command | PASS condition |
|-------|---------|----------------|
| GPS daemon | `pgrep -x gpsd` | exits 0, prints a PID |
| GPS data | `gpspipe -r -n 5 -w` | ≥1 line starting with `$` |
| SDR detected | `SoapySDRUtil --find` | stdout contains `driver` |
| SDR probe | `SoapySDRUtil --probe` | exits 0 |
| HaLow module | `lsmod \| grep morse` | non-empty output |
| HaLow interface | `ip link show` | line containing `morse` or `wlan` |
| I²C bus | `ls /dev/i2c-1` | device node exists |
| Compass detected | `i2cdetect -y 1` | `0d` present in grid |
| Compass register | `i2cget -y 1 0x0d 0x06` | exits 0, prints hex byte |
| Health endpoint | `curl -sf http://localhost:5000/health` | `{"status":"Healthy"}` |
| Container CLI | `docker exec zscout-hw-test dotnet ZScout.HwTest.Cli.dll --mode container` | exits 0, `RESULT: PASS` |
