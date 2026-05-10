#!/usr/bin/env bash
set -euo pipefail

IMAGE_NAME="${1:-ghcr.io/bulzi-org/zscout-hw-test:latest}"
OUTPUT_TAR="${2:-zscout-hw-test.tar}"

echo "Saving ${IMAGE_NAME} to ${OUTPUT_TAR}"
docker save "${IMAGE_NAME}" -o "${OUTPUT_TAR}"
echo "Done: ${OUTPUT_TAR}"
