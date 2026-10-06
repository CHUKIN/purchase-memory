#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repository_root"

if [[ "$(uname -s)" == "Darwin" ]]; then
  ./scripts/build-apple-sidecar.sh
fi

npm --prefix src/PurchaseMemory.Web ci
npm --prefix src/PurchaseMemory.Web run build
exec ./scripts/dotnet.sh run --project src/PurchaseMemory.Api
