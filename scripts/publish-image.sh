#!/usr/bin/env bash
# Builds the production image for Render (linux/amd64) and pushes it to the
# private GitHub Container Registry package with a timestamp tag and :latest.
#
#   ./scripts/publish-image.sh
#
# Needs: Docker with buildx, GitHub CLI logged in as the package owner with the
# write:packages scope (gh auth refresh -s write:packages,read:packages).
# The token goes straight from gh to docker login and is never printed.
# Nothing here touches data/ (excluded by .dockerignore) or any secret value.
set -euo pipefail

IMAGE="${IMAGE:-ghcr.io/chukin/purchase-memory}"
REGISTRY_USER="${REGISTRY_USER:-CHUKIN}"
TAG="$(date -u +%Y%m%d-%H%M)"

cd "$(dirname "$0")/.."

# Log out on every exit (success, failure or Ctrl+C) so the write-scoped token
# does not stay in Docker's credential store after the push.
trap 'docker logout ghcr.io >/dev/null 2>&1 || true' EXIT
gh auth token | docker login ghcr.io -u "$REGISTRY_USER" --password-stdin >/dev/null

# --provenance/--sbom off: one plain amd64 manifest, which Render pulls as is.
docker buildx build \
  --platform linux/amd64 \
  --provenance=false \
  --sbom=false \
  --tag "$IMAGE:$TAG" \
  --tag "$IMAGE:latest" \
  --push \
  .

digest="$(docker buildx imagetools inspect "$IMAGE:$TAG" --format '{{json .Manifest.Digest}}' | tr -d '"')"
echo "Pushed $IMAGE:$TAG and :latest"
echo "Digest: $digest"
echo "Render: Manual Deploy -> Deploy latest reference (or point the service at $IMAGE@$digest)."
