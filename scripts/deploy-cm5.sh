#!/usr/bin/env bash
# ──────────────────────────────────────────────────────────────────────────────
# zSCOUT Hardware Test Suite — CM5 Deployment Script
#
# Downloads the docker-compose.yml and starts all Tier 2 services + the
# hw-test dashboard on a Raspberry Pi CM5 running the zSCOUT base image.
#
# Usage (from CM5 via SSH):
#   curl -fsSL https://raw.githubusercontent.com/Bulzi-Org/zSCOUT-hw-test/main/scripts/deploy-cm5.sh | bash
#
# Or download and run manually:
#   curl -fsSL -o deploy-cm5.sh https://raw.githubusercontent.com/Bulzi-Org/zSCOUT-hw-test/main/scripts/deploy-cm5.sh
#   chmod +x deploy-cm5.sh
#   ./deploy-cm5.sh
#
# What this script does:
#   1. Creates /opt/zscout/hw-test/ deployment directory
#   2. Downloads the latest docker-compose.yml from GitHub
#   3. Pulls all container images (hw-test + gps-svc + compass-svc + sdr-svc)
#   4. Starts all services with health-check ordering
#   5. Waits for the dashboard to become healthy
#   6. Prints the dashboard URL
#
# To update to the latest images later, re-run this script or:
#   cd /opt/zscout/hw-test && docker compose pull && docker compose up -d
# ──────────────────────────────────────────────────────────────────────────────

DEPLOY_DIR="/opt/zscout/hw-test"
COMPOSE_URL="https://raw.githubusercontent.com/Bulzi-Org/zSCOUT-hw-test/main/deploy/docker-compose.yml"
DASHBOARD_PORT=5000

info()  { printf '\033[1;34m[INFO]\033[0m  %s\n' "$1"; }
ok()    { printf '\033[1;32m[OK]\033[0m    %s\n' "$1"; }
warn()  { printf '\033[1;33m[WARN]\033[0m  %s\n' "$1"; }
error() { printf '\033[1;31m[ERROR]\033[0m %s\n' "$1"; }

# ── Pre-flight checks ────────────────────────────────────────────────────────

if ! command -v docker &>/dev/null; then
    error "Docker is not installed. Install Docker first:"
    echo "  curl -fsSL https://get.docker.com | sh && sudo usermod -aG docker \$USER"
    exit 1
fi

if ! docker compose version &>/dev/null; then
    error "Docker Compose plugin is not available."
    echo "  sudo apt-get update && sudo apt-get install -y docker-compose-plugin"
    exit 1
fi

if ! docker info &>/dev/null 2>&1; then
    error "Cannot connect to Docker daemon. Are you root or in the docker group?"
    echo "  sudo usermod -aG docker \$USER && newgrp docker"
    exit 1
fi

info "Pre-flight checks passed"

# ── Ensure overlay2 storage driver ────────────────────────────────────────────
# Docker 29.x with the containerd snapshotter fails to compose overlay layers
# on the RPi kernel 6.12 shipped with the CM5 base image.  Explicitly setting
# the storage-driver to overlay2 avoids this issue.

DAEMON_JSON="/etc/docker/daemon.json"
if [ -f "${DAEMON_JSON}" ] && grep -q '"overlay2"' "${DAEMON_JSON}" 2>/dev/null; then
    info "overlay2 storage driver already configured — skipping"
else
    info "Configuring Docker to use overlay2 storage driver"
    sudo mkdir -p /etc/docker
    printf '{"storage-driver": "overlay2"}\n' | sudo tee "${DAEMON_JSON}" >/dev/null
    ok "Created ${DAEMON_JSON}"

    info "Restarting Docker daemon..."
    sudo systemctl restart docker
    sleep 5

    if docker info &>/dev/null 2>&1; then
        ok "Docker daemon restarted successfully"
    else
        warn "Docker daemon may still be starting — continuing anyway"
    fi
fi

# ── Create deployment directory ───────────────────────────────────────────────

info "Setting up deployment directory: ${DEPLOY_DIR}"
sudo mkdir -p "${DEPLOY_DIR}/data" "${DEPLOY_DIR}/logs"
sudo chown -R "$(id -u):$(id -g)" "${DEPLOY_DIR}"

# ── Download docker-compose.yml ───────────────────────────────────────────────

info "Downloading docker-compose.yml from GitHub"
if ! curl -fsSL -o "${DEPLOY_DIR}/docker-compose.yml" "${COMPOSE_URL}"; then
    error "Failed to download docker-compose.yml"
    exit 1
fi
ok "docker-compose.yml downloaded"

# ── Pull all images ───────────────────────────────────────────────────────────

info "Pulling container images (this may take a few minutes on first run)..."
if ! docker compose -f "${DEPLOY_DIR}/docker-compose.yml" pull; then
    error "Failed to pull one or more images"
    exit 1
fi
ok "All images pulled"

# ── Stop existing containers (if any) ─────────────────────────────────────────

if docker compose -f "${DEPLOY_DIR}/docker-compose.yml" ps -q 2>/dev/null | grep -q .; then
    info "Stopping existing containers..."
    docker compose -f "${DEPLOY_DIR}/docker-compose.yml" down
fi

# ── Start services ────────────────────────────────────────────────────────────

info "Starting all services..."
if ! docker compose -f "${DEPLOY_DIR}/docker-compose.yml" up -d; then
    error "Failed to start services"
    docker compose -f "${DEPLOY_DIR}/docker-compose.yml" logs --tail=20
    exit 1
fi
ok "All services started"

# ── Wait for dashboard health ─────────────────────────────────────────────────

info "Waiting for dashboard to become healthy..."
MAX_WAIT=120
ELAPSED=0
while [ $ELAPSED -lt $MAX_WAIT ]; do
    if curl -fsS "http://localhost:${DASHBOARD_PORT}/health" &>/dev/null; then
        ok "Dashboard is healthy"
        break
    fi
    sleep 5
    ELAPSED=$((ELAPSED + 5))
    printf '.'
done
echo ""

if [ $ELAPSED -ge $MAX_WAIT ]; then
    warn "Dashboard did not become healthy within ${MAX_WAIT}s"
    warn "Check logs: docker compose -f ${DEPLOY_DIR}/docker-compose.yml logs"
fi

# ── Summary ───────────────────────────────────────────────────────────────────

IP=$(hostname -I 2>/dev/null | awk '{print $1}')
echo ""
echo "╔══════════════════════════════════════════════════════════════════╗"
echo "║  zSCOUT Hardware Test Suite — Deployed                          ║"
echo "╠══════════════════════════════════════════════════════════════════╣"
echo "║                                                                 ║"
printf "║  Dashboard:  http://%-43s ║\n" "${IP:-localhost}:${DASHBOARD_PORT}"
echo "║                                                                 ║"
echo "║  Useful commands:                                               ║"
echo "║    Status:  cd ${DEPLOY_DIR} && docker compose ps               ║"
echo "║    Logs:    cd ${DEPLOY_DIR} && docker compose logs             ║"
echo "║    Stop:    cd ${DEPLOY_DIR} && docker compose down             ║"
echo "║    Update:  re-run this script                                  ║"
echo "║                                                                 ║"
echo "╚══════════════════════════════════════════════════════════════════╝"
