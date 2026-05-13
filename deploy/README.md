# Deployment Guide

## Requirements

- Raspberry Pi Compute Module 5 (CM5) with CM5-IO-BASE-B carrier
- Docker Engine 24+ (see **Host OS Setup** below)
- Peripheral drivers installed on the host:
  - `gpsd` running for GPS
  - SoapySDR + Wavelet-Lab driver for uSDR
  - `morse_driver` kernel module loaded for HaLow
  - I²C tools (`i2c-tools`) for compass

---

## Host OS Setup

Run these steps once on a fresh Raspberry Pi OS (Bookworm, 64-bit) install.

### 1 — Install Docker Engine

```bash
curl -fsSL https://get.docker.com | sh
sudo usermod -aG docker $USER
newgrp docker                         # apply group without logout
sudo systemctl enable --now docker
docker --version                      # verify ≥ 24.x
```

### 2 — Enable I²C (compass)

```bash
sudo raspi-config nonint do_i2c 0
# or add "dtparam=i2c_arm=on" to /boot/firmware/config.txt and reboot
```

### 3 — Install and start gpsd (GPS)

```bash
sudo apt-get install -y gpsd gpsd-clients
# Edit /etc/default/gpsd to set DEVICES and GPSD_OPTIONS for your GPS device
sudo systemctl enable --now gpsd
```

### 4 — Install SoapySDR + uSDR driver (SDR)

Follow the Wavelet-Lab driver build instructions for your uSDR hardware.
At minimum:

```bash
sudo apt-get install -y soapysdr-tools libsoapysdr-dev
# then build and install the Wavelet-Lab SoapySDR module
SoapySDRUtil --find           # should list your uSDR device
```

### 5 — Load HaLow kernel module

```bash
sudo modprobe morse_driver
# To persist across reboots:
echo "morse_driver" | sudo tee -a /etc/modules
```

### 6 — Create working directory

```bash
mkdir -p ~/zscout/{data,logs}
cd ~/zscout
curl -fsSL https://raw.githubusercontent.com/Bulzi-Org/zSCOUT-hw-test/main/deploy/docker-compose.yml \
     -o docker-compose.yml
```

---

## Option 1 — Pull from GHCR (online)

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
