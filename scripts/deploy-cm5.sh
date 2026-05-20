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
#   1. Authenticates to GHCR (GitHub Container Registry)
#   2. Creates /opt/zscout/hw-test/ deployment directory
#   3. Downloads the latest docker-compose.yml from GitHub
#   4. Pulls all container images (hw-test + gps-svc + compass-svc + sdr-svc)
#   5. Starts all services with health-check ordering
#   6. Waits for the dashboard to become healthy
#   7. Prints the dashboard URL
#
# GHCR Authentication:
#   The container images are hosted on GitHub Container Registry and require
#   authentication to pull. You need a GitHub Personal Access Token (PAT) with
#   read:packages scope. Set it before running:
#
#     export GHCR_TOKEN="ghp_your_token_here"
#     curl -fsSL https://raw.githubusercontent.com/Bulzi-Org/zSCOUT-hw-test/main/scripts/deploy-cm5.sh | bash
#
#   Or pass your GitHub username and token interactively (the script will prompt).
#
#   To create a PAT: https://github.com/settings/tokens → "Generate new token (classic)"
#   → select "read:packages" scope.
#
# To update to the latest images later, re-run this script or:
#   cd /opt/zscout/hw-test && docker compose pull && docker compose up -d
# ──────────────────────────────────────────────────────────────────────────────

DEPLOY_DIR="/opt/zscout/hw-test"
COMPOSE_URL="https://raw.githubusercontent.com/Bulzi-Org/zSCOUT-hw-test/main/deploy/docker-compose.yml"
DASHBOARD_PORT=5000
GHCR_REGISTRY="ghcr.io"

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

# ── GHCR Authentication ──────────────────────────────────────────────────────

# Check if already logged in to GHCR
if ! docker pull "${GHCR_REGISTRY}/bulzi-org/zscout-hw-test:latest" --quiet &>/dev/null 2>&1; then
    info "GHCR authentication required"

    if [ -n "${GHCR_TOKEN:-}" ]; then
        # Token provided via environment variable
        GHCR_USER="${GHCR_USER:-zscout-deploy}"
        info "Logging in to GHCR with provided token..."
        if ! echo "${GHCR_TOKEN}" | docker login "${GHCR_REGISTRY}" -u "${GHCR_USER}" --password-stdin 2>/dev/null; then
            error "GHCR login failed. Check your token has read:packages scope."
            exit 1
        fi
        ok "Logged in to GHCR"
    else
        # Prompt interactively
        echo ""
        echo "  The zSCOUT container images require GitHub authentication to pull."
        echo "  You need a GitHub Personal Access Token (PAT) with read:packages scope."
        echo "  Create one at: https://github.com/settings/tokens"
        echo ""
        read -rp "  GitHub username: " GHCR_USER
        read -rsp "  Personal Access Token (read:packages): " GHCR_TOKEN
        echo ""

        if [ -z "${GHCR_USER}" ] || [ -z "${GHCR_TOKEN}" ]; then
            error "Username and token are required."
            exit 1
        fi

        if ! echo "${GHCR_TOKEN}" | docker login "${GHCR_REGISTRY}" -u "${GHCR_USER}" --password-stdin 2>/dev/null; then
            error "GHCR login failed. Check your username and token."
            exit 1
        fi
        ok "Logged in to GHCR"
    fi
else
    ok "Already authenticated to GHCR"
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
