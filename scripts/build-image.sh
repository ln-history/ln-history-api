#!/usr/bin/env bash
#
# Build and push the ln-history-api container image, tagged with the version managed by
# commitizen. Typical release flow:
#
#   cz bump                # bump version from conventional commits + create git tag
#   git push --follow-tags
#   ./scripts/build-image.sh
#
# The version and image repository come from the single source of truth
# (Directory.Build.props <Version> / <DockerImageRepository>), so nothing is hardcoded here.
#
set -euo pipefail
cd "$(dirname "$0")/.."

PROPS="Directory.Build.props"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$PROPS" | head -1)"
IMAGE="${LN_HISTORY_IMAGE:-$(sed -n 's:.*<DockerImageRepository>\(.*\)</DockerImageRepository>.*:\1:p' "$PROPS" | head -1)}"

if [[ -z "$VERSION" || -z "$IMAGE" ]]; then
  echo "Could not resolve VERSION ('$VERSION') or IMAGE ('$IMAGE') from $PROPS" >&2
  exit 1
fi

echo "Building and pushing ${IMAGE}:${VERSION} (+ :latest)"
docker buildx build \
  --platform linux/amd64 \
  -f LN-history.Startup/Dockerfile \
  -t "${IMAGE}:${VERSION}" \
  -t "${IMAGE}:latest" \
  --push \
  .
