# ocr_compare — Azure prebuilt-receipt vs Tesseract vs Haiku

Local benchmark (owner's Mac only, no prod changes) on `test-data/receipts/household-2026-08/`.
Writes per-receipt JSONs in the `score_items.py` shape plus one `report.md` / `report.json`
to `test-data/receipts/household-2026-08/results/ocr-compare/` (override with `--out`).

Photos are not in git: in a worktree pass `--images-dir` pointing at the folder with
`receipt_NN.jpg` (relative to a worktree: `../../../test-data/receipts/household-2026-08`).

```sh
B=tools/PurchaseMemory.ReceiptBenchmarks/run_ocr_compare.py

# Tesseract (free, local). Only eng/osd are installed; pol is missing.
python3 $B tesseract                     # eng, --psm 6  -> tesseract-eng-psm6
python3 $B tesseract --scale 2           # 2x upsampled -> tesseract-eng-psm6-x2
python3 $B tesseract --lang pol+eng --tessdata /path/with/pol   # later, once pol.traineddata exists

# Azure (keys from ~/.zshrc). Responses are cached; reruns never call Azure for a cached receipt.
zsh -ic 'python3 tools/PurchaseMemory.ReceiptBenchmarks/run_ocr_compare.py azure --max-calls 30'
python3 $B azure --offline               # cache only

# Haiku matrix: dry run by default (prints variants x receipts and the estimated cost).
python3 $B haiku
python3 $B haiku --variants haiku-bbox-required-low,haiku-full-structured-low --repeats 2
# Paid, only with an approved budget (hard stop on worst case of the next call):
zsh -ic 'python3 tools/PurchaseMemory.ReceiptBenchmarks/run_ocr_compare.py haiku --run-haiku --budget-usd 0.50'

# Score everything and write the report (also scores the saved 2026-10-11 Haiku prod runs, free,
# and rebuilds the Haiku + Azure-box hybrids from the Azure cache: no network).
python3 $B report

# The production pipeline (.NET, contract v4 by default; --contract v3 for the old one) writes
# the same shape incl. boxes, so a run under runs/<label>/ is scored by `report` too. Paid:
zsh -ic './scripts/dotnet.sh run --project tools/PurchaseMemory.ReceiptBenchmarks -- anthropic --contract v4 --effort medium --label haiku-prod-v4-medium --out test-data/receipts/household-2026-08/results/ocr-compare/runs/haiku-prod-v4-medium --ledger test-data/receipts/household-2026-08/results/out/haiku-5-5/ledger-prod-v4.json --stop-at 0.15'
# (keep the ledger outside runs/: every runs/*/*.json is read as a result record)

# score_items.py works on any run folder as before:
python3 tools/PurchaseMemory.ReceiptBenchmarks/score_items.py test-data/receipts/household-2026-08/results/ocr-compare/runs/azure-prebuilt-receipt

# Unit tests (stdlib unittest, no network):
python3 -m unittest discover -s tools/PurchaseMemory.ReceiptBenchmarks/ocr_compare/tests -t tools/PurchaseMemory.ReceiptBenchmarks
```

Files:
- `azure_receipt.py` — REST client (api-version 2024-11-30), cache + call ledger
  (`cache/azure/_calls.json`, the `--max-calls` cap counts analyze POSTs across runs), F0 spacing.
- `tesseract_ocr.py` — TSV words/lines and a naive line→item parser (documented in the module).
- `haiku_matrix.py` — 3 prompts × 4 thinking levels, cost estimate, budgeted runner.
- `boxes.py` — box geometry and the "box hit" definition.
- `report.py` — metrics (reuses `score_items.score_receipt`) and the report.
- `hybrid.py` — free hybrid: Haiku's items/totals/discounts with boxes taken from the cached Azure
  response (Azure items, then Azure OCR lines as a fallback). Strategies `amount`, `name`,
  `name-amount`, `aligned` (order-preserving), `aligned+lines`, `aligned+lines+own` run for every
  saved Haiku run; new `runs/haiku-*` runs get `aligned+lines` only. Hybrids live in memory (not
  written to `runs/`). The report's "Hybrid" section adds a per-line box verdict
  (right / ambiguous / wrong = other line + miss, see `report.box_verdict`) and two calibration
  rows with deliberately shifted boxes that show how many wrong boxes the check catches.

Caches: `cache/azure/*.json` are raw Azure responses (they contain receipt text, like the other
committed outputs here); `cache/tesseract/` holds words/lines JSON.
