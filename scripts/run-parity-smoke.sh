#!/usr/bin/env bash
# scripts/run-parity-smoke.sh
#
# T025: Parity smoke test — runs the hardware test suite in both host and container
# mode, then diffs peripheral pass/fail status between the two runs.
# Exit 0 = parity confirmed; exit 1 = mismatch detected or run failed.
#
# Usage:
#   ./scripts/run-parity-smoke.sh
#   ./scripts/run-parity-smoke.sh --container-image ghcr.io/bulzi-org/zscout-hw-test:latest
#
# Dependencies: jq (for JSON diffing), dotnet CLI or docker (depending on mode)
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
CLI_PROJECT="${REPO_ROOT}/src/ZScout.HwTest.Cli"
CONTAINER_IMAGE="${1:-}"
SHIFT=0
for arg in "$@"; do
  if [[ "${arg}" == "--container-image" ]]; then
    SHIFT=1
  elif [[ "${SHIFT}" == "1" ]]; then
    CONTAINER_IMAGE="${arg}"
    SHIFT=0
  fi
done

HOST_JSON="${TMPDIR:-/tmp}/zscout-parity-host.json"
CONTAINER_JSON="${TMPDIR:-/tmp}/zscout-parity-container.json"

echo "=== zSCOUT Hardware Parity Smoke Test ==="
echo "Repo root : ${REPO_ROOT}"
echo ""

# ──────────────────────────────────────────────────────────────────────────────
# 1. Run in HOST mode
# ──────────────────────────────────────────────────────────────────────────────
echo "[1/3] Running in HOST mode..."
dotnet run --project "${CLI_PROJECT}" -- --mode host --format json \
  >"${HOST_JSON}" 2>/dev/null || {
    echo "ERROR: Host-mode run failed. Check dotnet CLI and peripheral drivers."
    exit 1
  }

echo "      Host run complete. Output: ${HOST_JSON}"

# ──────────────────────────────────────────────────────────────────────────────
# 2. Run in CONTAINER mode
# ──────────────────────────────────────────────────────────────────────────────
echo "[2/3] Running in CONTAINER mode..."

if [[ -z "${CONTAINER_IMAGE}" ]]; then
  # Build the image locally if not provided
  CONTAINER_IMAGE="zscout-hw-test:parity-smoke"
  echo "      No --container-image provided; building locally..."
  docker build -q -t "${CONTAINER_IMAGE}" "${REPO_ROOT}/deploy" \
    --build-arg BUILD_CONFIGURATION=Release \
    >/dev/null 2>&1 || {
      echo "ERROR: Docker build failed."
      exit 1
    }
fi

docker run --rm \
  --privileged \
  --network host \
  -v /dev:/dev:ro \
  -v /sys:/sys:ro \
  -v "${REPO_ROOT}/data:/app/data" \
  "${CONTAINER_IMAGE}" \
  dotnet ZScout.HwTest.Cli.dll --mode container --format json \
  >"${CONTAINER_JSON}" 2>/dev/null || {
    echo "ERROR: Container-mode run failed. Check image and device pass-throughs."
    exit 1
  }

echo "      Container run complete. Output: ${CONTAINER_JSON}"

# ──────────────────────────────────────────────────────────────────────────────
# 3. Diff peripheral statuses
# ──────────────────────────────────────────────────────────────────────────────
echo "[3/3] Comparing results..."

if ! command -v jq &>/dev/null; then
  echo "WARNING: jq not found. Cannot diff JSON results. Install jq for comparison."
  echo "Host JSON   : ${HOST_JSON}"
  echo "Container JSON : ${CONTAINER_JSON}"
  exit 0
fi

MISMATCHES=0

# Extract peripheral IDs from host run
mapfile -t PERIPHERALS < <(jq -r '.evidence[].peripheralId' "${HOST_JSON}" 2>/dev/null || echo "")

if [[ ${#PERIPHERALS[@]} -eq 0 ]]; then
  echo "ERROR: No peripheral evidence found in host run output."
  cat "${HOST_JSON}"
  exit 1
fi

printf "\n%-14s %-20s %-20s %s\n" "PERIPHERAL" "HOST" "CONTAINER" "PARITY"
printf "%s\n" "$(printf '%0.s-' {1..70})"

for pid in "${PERIPHERALS[@]}"; do
  host_status=$(jq -r ".evidence[] | select(.peripheralId == \"${pid}\") | .healthSnapshot.values.status // \"unknown\"" "${HOST_JSON}")
  ctr_status=$(jq -r ".evidence[] | select(.peripheralId == \"${pid}\") | .healthSnapshot.values.status // \"unknown\"" "${CONTAINER_JSON}")

  if [[ "${host_status}" == "${ctr_status}" ]]; then
    parity="OK"
  else
    parity="MISMATCH"
    MISMATCHES=$((MISMATCHES + 1))
  fi

  printf "%-14s %-20s %-20s %s\n" "${pid}" "${host_status}" "${ctr_status}" "${parity}"
done

echo ""
if [[ ${MISMATCHES} -gt 0 ]]; then
  echo "RESULT: FAIL — ${MISMATCHES} peripheral(s) show status mismatch between host and container."
  echo ""
  echo "Investigate:"
  echo "  Host JSON      : ${HOST_JSON}"
  echo "  Container JSON : ${CONTAINER_JSON}"
  exit 1
else
  echo "RESULT: PASS — all peripherals show identical status in host and container mode."
  exit 0
fi
