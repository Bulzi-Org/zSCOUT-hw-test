# Deployment Guide

## Host OS options

This container is designed to run on top of one of two host OS configurations:

| Host OS | All dependencies pre-installed? | Recommended for |
|---|---|---|
| **`zSCOUT-image-CM5`** (custom pi-gen build) | ✅ Yes — Docker Engine, gpsd, SoapySDR, `morse_driver`, I²C, and optionally the image tarball are all baked in | Production CM5 deployment |
| **Vanilla Raspberry Pi OS** (Bookworm, 64-bit) | ❌ No — follow the **Host OS Setup** section below | Development / first-time setup on an unprovisioned board |

> **If you are using `zSCOUT-image-CM5`**, skip directly to
> [Option 1 — Pull from GHCR](#option-1--pull-from-ghcr-online) or
> [Option 3 — SD-card bake-in](#option-3--sd-card-bake-in) — all host
> dependencies are already present.

---

## Requirements

- Raspberry Pi Compute Module 5 (CM5) with CM5-IO-BASE-B carrier
- Docker Engine 24+ — provided by `zSCOUT-image-CM5`, or install manually (see below)
- Peripheral drivers on the host:
  - `gpsd` running for GPS
  - SoapySDR + Wavelet-Lab uSDR driver
  - `morse_driver` kernel module for HaLow
  - I²C enabled for compass

---

## Host OS Setup (vanilla Raspberry Pi OS only)

> **Skip this section if you are using `zSCOUT-image-CM5`** — every step
> below is already handled by the pi-gen build.

> **Why can't these steps be inside the container image?**
>
> The container shares the host's Linux kernel — it does not bring its own.
> Anything that requires a kernel module, a device tree overlay, or exclusive
> ownership of a hardware port must therefore be configured on the **host OS**
> before the container starts. The container then accesses the resulting device
> nodes (e.g. `/dev/i2c-1`, `/dev/ttyAMA0`) through the read-only `/dev` bind
> mount and talks to host-side daemons (e.g. gpsd) over `localhost` via
> `network_mode: host`.

Run these steps once on a fresh Raspberry Pi OS (Bookworm, 64-bit) install.

---

### 1 — Install Docker Engine

**Why on the host, not in the image?**
Docker itself is the runtime that launches the container. It must exist on the
host OS before any image can be pulled or run. There is no way to install the
container runtime inside the container it is meant to run.

```bash
curl -fsSL https://get.docker.com | sh
sudo usermod -aG docker $USER
newgrp docker                         # apply group without logout
sudo systemctl enable --now docker
docker --version                      # verify ≥ 24.x
```

---

### 2 — Enable I²C (compass)

**Why on the host, not in the image?**
I²C on the CM5 is controlled by a **device tree overlay** (`dtparam=i2c_arm=on`)
that the bootloader reads before the kernel starts. This overlay tells the
kernel to load the `i2c-bcm2835` driver and expose the bus as `/dev/i2c-1`.
A container cannot modify the device tree or trigger a boot-time kernel
reconfiguration — by the time Docker starts, the kernel is already running and
either has or hasn't created `/dev/i2c-1`. The container accesses the device
node only because the host OS created it first and the compose file bind-mounts
`/dev` into the container.

```bash
sudo raspi-config nonint do_i2c 0
# or manually add "dtparam=i2c_arm=on" to /boot/firmware/config.txt and reboot
```

Verify:
```bash
ls /dev/i2c-*       # should show /dev/i2c-1
i2cdetect -y 1      # scan for connected devices (QMC5883L appears at 0x0D)
```

---

### 3 — Install and start gpsd (GPS)

**Why on the host, not in the image?**
`gpsd` is a **system daemon** that takes exclusive ownership of the GPS serial
port (e.g. `/dev/ttyAMA0`) to configure baud rate, enable NMEA sentences, and
multiplex the data stream to multiple clients over a TCP socket on port 2947.
The compose file mounts `/dev` **read-only** into the container, which means
the container cannot open the serial port with write access to configure it.
Instead, `gpspipe` (installed in the image) connects to the host's gpsd socket
at `localhost:2947` — which is reachable because `network_mode: host` makes the
container share the host network stack. Running a second gpsd inside the
container would conflict with the host's gpsd over the same serial device.

```bash
sudo apt-get install -y gpsd gpsd-clients

# Configure the GPS device — edit /etc/default/gpsd:
#   DEVICES="/dev/ttyAMA0"        (adjust to your port)
#   GPSD_OPTIONS="-n"
#   START_DAEMON="true"
sudo nano /etc/default/gpsd

sudo systemctl enable --now gpsd

# Verify GPS data is flowing:
gpsmon                  # live NMEA view; Ctrl-C to exit
```

---

### 4 — Install SoapySDR + uSDR driver (SDR)

**Why on the host, not in the image?**
The Wavelet-Lab uSDR driver is a **kernel module** (or a USB kernel driver
binding). Kernel modules must be compiled against the running host kernel and
loaded into it with `insmod`/`modprobe`. A container shares the host kernel and
cannot load a module that isn't already present in the host's kernel module
tree — even with `--privileged`. The module creates the USB or character device
node that the container then accesses via the `/dev` bind-mount. The SoapySDR
**userspace library** (which talks to the device node) is included in the
container image; only the kernel-level driver half must live on the host.

```bash
sudo apt-get install -y soapysdr-tools libsoapysdr-dev

# Build and install the Wavelet-Lab SoapySDR kernel module/plugin
# following the instructions at https://github.com/wavelet-lab/usdr-lib
# (steps vary by uSDR hardware revision)

# Verify the SDR is visible to SoapySDR:
SoapySDRUtil --find           # should list your uSDR device
```

---

### 5 — Load HaLow kernel module

**Why on the host, not in the image?**
`morse_driver` is the **kernel module** for the Morse Micro HaLow (802.11ah)
radio. Like any kernel module it must be loaded into the **host kernel** with
`modprobe`. Once loaded, the driver registers a network interface (e.g.
`wlan0`) and/or character device that the container can use through
`network_mode: host` (which gives the container full visibility of the host
network interfaces) and the `/dev` bind-mount. Because the container shares the
host kernel it will automatically see any interface or device node the module
creates — but only after the host has loaded the module.

```bash
sudo modprobe morse_driver

# Verify the interface appeared:
ip link show | grep -i morse

# Persist across reboots:
echo "morse_driver" | sudo tee -a /etc/modules
```

---

### 6 — Create working directory

The compose file bind-mounts `./data` and `./logs` from the directory where
you run `docker compose`. Create them before the first run or Docker will
create them as root-owned and the container may not be able to write to them.

```bash
mkdir -p ~/zscout/{data,logs}
cd ~/zscout
curl -fsSL https://raw.githubusercontent.com/Bulzi-Org/zSCOUT-hw-test/main/deploy/docker-compose.yml \
     -o docker-compose.yml
```

---

```bash
cd ~/zscout
docker pull ghcr.io/bulzi-org/zscout-hw-test:latest
docker compose up -d
```

The compose file pulls the pre-built arm64 image, binds to the host network,
and mounts `/dev` and `/sys` with `privileged: true` so the container can
reach all hardware paths. The dashboard is then available at
`http://<cm5-ip>:5000`.

---

## Option 2 — Offline / air-gapped deployment

### Step 1: Save the image on a connected machine

```bash
# On a machine with internet access:
docker pull ghcr.io/bulzi-org/zscout-hw-test:latest
bash export-image.sh
# Produces: zscout-hw-test-latest.tar.gz
```

### Step 2: Transfer the archive

```bash
scp zscout-hw-test-latest.tar.gz pi@<cm5-ip>:~/
```

### Step 3: Load on CM5

```bash
# On the CM5:
gunzip -c zscout-hw-test-latest.tar.gz | docker load
```

### Step 4: Run

```bash
docker compose up -d
```

---

## Option 3 — SD-card bake-in

Pre-load the image into the SD card image so the CM5 boots with the container
ready to run without any network access.

```bash
# 1. Build or export the image archive (see Option 2, Step 1)

# 2. Mount the SD card partition (adjust /dev/sdX2 as needed):
sudo mount /dev/sdX2 /mnt/sd

# 3. Copy the image archive into the overlay
sudo cp zscout-hw-test-latest.tar.gz /mnt/sd/home/pi/

# 4. Add a systemd one-shot unit to load on first boot:
sudo tee /mnt/sd/etc/systemd/system/zscout-load-image.service > /dev/null <<'EOF'
[Unit]
Description=Load zSCOUT hardware test image
After=docker.service
Requires=docker.service
ConditionPathExists=/home/pi/zscout-hw-test-latest.tar.gz

[Service]
Type=oneshot
ExecStart=/bin/bash -c 'gunzip -c /home/pi/zscout-hw-test-latest.tar.gz | docker load'
ExecStartPost=/bin/rm -f /home/pi/zscout-hw-test-latest.tar.gz
RemainAfterExit=yes

[Install]
WantedBy=multi-user.target
EOF

sudo ln -s /etc/systemd/system/zscout-load-image.service \
           /mnt/sd/etc/systemd/system/multi-user.target.wants/zscout-load-image.service

# 5. Optionally enable docker compose to auto-start:
sudo cp docker-compose.yml /mnt/sd/home/pi/zscout/
sudo tee /mnt/sd/etc/systemd/system/zscout.service > /dev/null <<'EOF'
[Unit]
Description=zSCOUT hardware test dashboard
After=zscout-load-image.service docker.service
Requires=docker.service

[Service]
Type=simple
WorkingDirectory=/home/pi/zscout
ExecStart=/usr/bin/docker compose up
ExecStop=/usr/bin/docker compose down
Restart=on-failure

[Install]
WantedBy=multi-user.target
EOF

sudo ln -s /etc/systemd/system/zscout.service \
           /mnt/sd/etc/systemd/system/multi-user.target.wants/zscout.service

# 6. Unmount
sudo umount /mnt/sd
```

After flashing the SD card and booting the CM5, the dashboard will be
accessible at `http://<cm5-ip>:5000` within ~30 seconds.

---

## Data persistence

Run data, evidence, verdicts, and exports are stored under `./data/` on the
host (bind-mounted into the container at `/app/data`). Back up this directory
to preserve test history.

Retention window: **30 days** (configured via `appsettings.json:Retention:Days`).

---

## Environment variables

| Variable | Default | Description |
|---|---|---|
| `ASPNETCORE_URLS` | `http://+:5000` | Listening address |
| `Retention__Days` | `30` | Data retention window |
| `Peripherals__Compass__I2cBus` | `1` | I²C bus number for QMC5883L |

---

## Updating the image

```bash
# Pull latest
docker pull ghcr.io/bulzi-org/zscout-hw-test:latest

# Restart compose
docker compose down && docker compose up -d
```
