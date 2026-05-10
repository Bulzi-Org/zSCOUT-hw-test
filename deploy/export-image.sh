#!/usr/bin/env bash
# Helper script to export and transfer Docker image for offline deployment
# Usage: ./deploy/export-image.sh [image-name] [remote-host] [remote-user]

set -euo pipefail

IMAGE_NAME="${1:-zscout-hw-test:latest}"
REMOTE_HOST="${2:-cm5.local}"
REMOTE_USER="${3:-pi}"
EXPORT_FILE="zscout-hw-test-image.tar.gz"

echo "=== zSCOUT Hardware Test Dashboard Image Export ==="
echo "Image: $IMAGE_NAME"
echo "Export file: $EXPORT_FILE"
echo ""

# Check if image exists locally
if ! docker image inspect "$IMAGE_NAME" >/dev/null 2>&1; then
  echo "ERROR: Image '$IMAGE_NAME' not found locally"
  exit 1
fi

# Export image
echo "Exporting image to $EXPORT_FILE..."
docker save "$IMAGE_NAME" | gzip > "$EXPORT_FILE"
SIZE_MB=$(du -h "$EXPORT_FILE" | cut -f1)
echo "✓ Image exported: $SIZE_MB"
echo ""

# Transfer to remote if host provided
if [[ -n "$REMOTE_HOST" ]]; then
  echo "Transferring image to $REMOTE_USER@$REMOTE_HOST..."
  scp "$EXPORT_FILE" "$REMOTE_USER@$REMOTE_HOST:/tmp/$EXPORT_FILE"
  echo "✓ Transfer complete"
  echo ""
  
  echo "Next steps on $REMOTE_HOST:"
  echo "  1. SSH to target: ssh $REMOTE_USER@$REMOTE_HOST"
  echo "  2. Load image: docker load < /tmp/$EXPORT_FILE"
  echo "  3. Clean up: rm /tmp/$EXPORT_FILE"
  echo "  4. Run container: docker compose -f deploy/docker-compose.yml up -d"
  echo ""
fi

echo "✓ Export complete!"
