#!/usr/bin/env bash
# Golden check for ReceiptGeometry — NOT a unit test.
#
# There is no Swift test infrastructure in this repository, and stage 1 does not add
# one. This script is the cheap substitute: it compiles
# src/PurchaseMemory.AppleIntelligence/ReceiptGeometry.swift together with a tiny
# driver and runs ReceiptGeometry.analyze over a FROZEN fixture —
# research/ocr-geometry/fixtures/frag_4.json, the 190 OCR fragments of the E.Leclerc
# Ursynów receipt (48 items, total 423,46, the only tilted photo in the research set).
#
# It asserts: skew -18.0, 80 rows, 48 rows with exactly one price cell, 0 rows with
# several. Re-based in stage 2a (РЕЗУЛЬТАТЫ_КОНВЕЙЕР_ЭТАП_2A_СЛИПАНИЕ_СТРОК.md), which
# replaced the greedy `rows.last` join with a nearest-anchor join that never stacks two
# fragments of one column in a row, and tightened the price-cell rule. The old numbers
# (75 rows, 47/0) were measured with the old rules. On THIS fixture the glued
# PAPRYKA / PAPRYK CORNO pair happened to be split already (the rounding luck
# described below); the defect showed on the live photo. The new numbers are:
#   48 single — every product line of the receipt; the `1 ×2,29` line now counts too
#               (Vision returned the multiplication sign, the old pattern missed it);
#   80 rows   — five rows the old rules merged are now separate: the weight line
#               `0,446 x157,49 70,24` under POLĘDWICA (printed on its own line),
#               `-0,6ZA` pulled off the first OPUST row, one tax-summary line and
#               one card-slip line (`Numer kartu`), both stacked in one column, and
#               the tall garbled box `OPUS A ES A ANE- 1 D2` (Vision merged
#               PAPRYK CORNO with the first OPUST line) is no longer glued to the
#               PAPRYK CORNO row it overlaps.
# It covers the geometry only — the Vision OCR step is fixed by the fixture, so this
# says nothing about recognition quality. Exits non-zero on any mismatch.
#
# Live runs vs this fixture: the fixture coordinates are rounded to whole per-mille
# units. Before stage 2a that rounding decided the result (±0.5‰ jitter moved it
# across 43…47 single / 0…2 multiple). After stage 2a 100 of 100 jittered runs give
# 48/0; the skew itself stays loose (−16.5…−32.5, the objective has a wide plateau)
# and the row count moves 79…81. The live sidecar run on the same photo gives
# skew −16, 78 rows, 48/0. Treat the skew and row count here as a frozen-input
# regression guard, 48/0 as the expected quality on this receipt.
set -euo pipefail

repository_root="$(cd "$(dirname "$0")/.." && pwd)"
fixture="$repository_root/research/ocr-geometry/fixtures/frag_4.json"
expected_skew="-18.0"
expected_rows=80
expected_single=48
expected_multiple=0

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "ReceiptGeometry golden check needs Swift on macOS." >&2
  exit 1
fi

if [[ ! -f "$fixture" ]]; then
  echo "Missing golden fixture: $fixture" >&2
  exit 1
fi

work_directory="$(mktemp -d)"
trap 'rm -rf "$work_directory"' EXIT

cat > "$work_directory/main.swift" <<'SWIFT'
import CoreGraphics
import Foundation

// Fixture fragment: 0..1000 page units, y pointing DOWN (0 = top of the receipt).
// Converted back to the Vision box (0..1, y pointing UP) that ReceiptGeometry expects.
struct FixtureFragment: Decodable, ReceiptGeometryFragment {
    let t: String
    let x: Double
    let y: Double
    let w: Double
    let h: Double

    var text: String { t }
    var bounds: CGRect {
        CGRect(x: x / 1000, y: 1 - (y + h) / 1000, width: w / 1000, height: h / 1000)
    }
}

private let fixturePath = CommandLine.arguments[1]
private let data = try Data(contentsOf: URL(fileURLWithPath: fixturePath))
private let fragments = try JSONDecoder().decode([FixtureFragment].self, from: data)
private let result = ReceiptGeometry.analyze(fragments)
print("\(result.skew) \(result.rows.count) \(result.rowsWithSinglePriceCell) \(result.rowsWithMultiplePriceCells)")
SWIFT

xcrun swiftc -O \
  "$repository_root/src/PurchaseMemory.AppleIntelligence/ReceiptGeometry.swift" \
  "$work_directory/main.swift" \
  -o "$work_directory/geometry-golden"

# Run through a file, not a process substitution: `read` would swallow a fatalError
# in the driver and leave `set -e` to kill the script on an empty line instead.
if ! "$work_directory/geometry-golden" "$fixture" > "$work_directory/result.txt"; then
  echo "ReceiptGeometry golden driver failed to run; its output was:" >&2
  cat "$work_directory/result.txt" >&2
  exit 1
fi

read -r skew rows single multiple < "$work_directory/result.txt"
if [[ -z "${multiple:-}" ]]; then
  echo "ReceiptGeometry golden driver printed an unexpected result: $(cat "$work_directory/result.txt")" >&2
  exit 1
fi

failures=0
check() {
  local label="$1" actual="$2" expected="$3"
  if [[ "$actual" == "$expected" ]]; then
    printf 'ok   %-24s %s\n' "$label" "$actual"
  else
    printf 'FAIL %-24s %s (expected %s)\n' "$label" "$actual" "$expected"
    failures=$((failures + 1))
  fi
}

echo "fixture: $fixture"
check "skew" "$skew" "$expected_skew"
check "rows" "$rows" "$expected_rows"
check "rows, one price cell" "$single" "$expected_single"
check "rows, several prices" "$multiple" "$expected_multiple"

if (( failures > 0 )); then
  echo "ReceiptGeometry golden check failed: $failures mismatch(es)." >&2
  exit 1
fi

echo "ReceiptGeometry golden check passed."
