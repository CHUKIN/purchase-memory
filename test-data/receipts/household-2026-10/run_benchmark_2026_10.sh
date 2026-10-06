#!/usr/bin/env bash
# Runs the ReceiptBenchmarks `openai` or `cloud` command on the household-2026-10
# photos and scores the result with score_items_2026_10.py.
#
# The benchmark tool reads its photos from a fixed folder (household-2026-08) under
# the repository root it finds next to its binary. Instead of changing the tool, this
# script copies the built tool, these photos and ground_truth.json into a throw-away
# "repository root" in the temp directory and runs the copy there. Nothing in the
# repository is written; results go to $OUT (default: a temp folder, printed).
#
# Paid (OpenAI, needs OPENAI_API_KEY from ~/.zshrc) — run only with a named budget:
#   zsh -ic './test-data/receipts/household-2026-10/run_benchmark_2026_10.sh openai --models gpt-6-sol'
# Free (local Ollama daemon, local models only):
#   ./test-data/receipts/household-2026-10/run_benchmark_2026_10.sh cloud --config cloud-photo \
#       --models qwen3-vl:8b-instruct --base-url http://127.0.0.1:11434
# Set NO_BUILD=1 to reuse the existing tool build.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/../../.." && pwd)"
tool_project="$root/tools/PurchaseMemory.ReceiptBenchmarks"
tool_bin="$tool_project/bin/Debug/net10.0"

if [[ $# -lt 1 || ( "$1" != "openai" && "$1" != "cloud" ) ]]; then
  echo "usage: $0 openai|cloud [benchmark arguments, without --out]" >&2
  exit 2
fi

if [[ "${NO_BUILD:-0}" != "1" ]]; then
  "$root/scripts/dotnet.sh" build "$tool_project" -c Debug -nologo -v quiet
fi

work="$(mktemp -d "${TMPDIR:-/tmp}/pm-receipts-2026-10.XXXXXX")"
fake="$work/root"
mkdir -p "$fake/tool" "$fake/test-data/receipts/household-2026-08/results" "$fake/.artifacts/apple-intelligence"
touch "$fake/PurchaseMemory.slnx"
cp -R "$tool_bin/." "$fake/tool/"
cp "$here"/receipt_*.jpg "$fake/test-data/receipts/household-2026-08/"
cp "$here/ground_truth.json" "$fake/test-data/receipts/household-2026-08/results/"
sidecar="$root/.artifacts/apple-intelligence/PurchaseMemory.AppleIntelligence"
[[ -x "$sidecar" ]] && cp "$sidecar" "$fake/.artifacts/apple-intelligence/"

out="${OUT:-$work/out}"
mkdir -p "$out"
echo "Results: $out"
status=0
"$root/scripts/dotnet.sh" "$fake/tool/PurchaseMemory.ReceiptBenchmarks.dll" "$@" --out "$out" || status=$?
python3 "$here/score_items_2026_10.py" "$out" --detail || true
exit "$status"
