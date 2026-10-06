#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "$0")/.." && pwd)"

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "Apple receipt sidecar is available only on macOS." >&2
  exit 1
fi

source_file="$repository_root/src/PurchaseMemory.AppleIntelligence/AppleReceiptSidecar.swift"
output_directory="$repository_root/.artifacts/apple-intelligence"
output_file="$output_directory/PurchaseMemory.AppleIntelligence"

mkdir -p "$output_directory"

xcrun swiftc \
  -O \
  -parse-as-library \
  -target arm64-apple-macosx26.4 \
  "$source_file" \
  "$repository_root/src/PurchaseMemory.AppleIntelligence/ReceiptGeometry.swift" \
  -o "$output_file"

chmod +x "$output_file"
echo "$output_file"
