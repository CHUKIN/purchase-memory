# OCR comparison: Azure prebuilt-receipt vs Tesseract vs Haiku (2026-10-14)

Status: step 1 of "Free/cheap OCR services" (`ЧТО_ДАЛЬШЕ.md`) is implemented and run for Azure and Tesseract. Haiku has a dry run only: no paid budget has been approved. Independent review is pending. No production code or config changed.

## How to run
The tool is in `tools/PurchaseMemory.ReceiptBenchmarks/ocr_compare/` (Python, stdlib only, plus Pillow for `--scale`). The launcher is `run_ocr_compare.py`, and `ocr_compare/README.md` has every option. Photos are not in git, so a worktree needs `--images-dir`.
```sh
B=tools/PurchaseMemory.ReceiptBenchmarks/run_ocr_compare.py
python3 $B tesseract                 # eng, psm 6
python3 $B tesseract --scale 2       # 2x upsampled
zsh -ic 'python3 tools/PurchaseMemory.ReceiptBenchmarks/run_ocr_compare.py azure --max-calls 30'   # cached per receipt
python3 $B haiku                     # dry run: prints the matrix and its cost
zsh -ic 'python3 tools/PurchaseMemory.ReceiptBenchmarks/run_ocr_compare.py haiku --run-haiku --budget-usd X'   # paid, needs an approved X
python3 $B report                    # writes results/ocr-compare/report.md + report.json
python3 -m unittest discover -s tools/PurchaseMemory.ReceiptBenchmarks/ocr_compare/tests -t tools/PurchaseMemory.ReceiptBenchmarks
```
Output goes to `test-data/receipts/household-2026-08/results/ocr-compare/`: `report.md`, `report.json`, `runs/<config>/` (the `score_items.py` format, so the old scorer works on these folders), `cache/azure/` (raw responses plus the `_calls.json` ledger), and `cache/tesseract/`.

## What was measured
- **Item accuracy:** `score_items.score_receipt` (reused, not copied) on the 8 labelled receipts: 110 lines and 5 discounts. `score_items.py` gained one field, `pairs`, which records which predicted line matched each GT line. Its printed output is unchanged (re-checked: Haiku low-r1 is still 108/110).
- **Totals:** checked on all 20 receipts against `ground_truth.json`. "lines sum = total" is the app's own consistency check.
- **Box hit:** a line's box counts as a hit when it exists, is plausible (inside the image, height ≤ 15% of the image), and the reference OCR words inside it fuzzy-match the line name (≥ 0.6) or contain the line price. Two reference layers are reported: Azure words and Tesseract words. "GT lines with hit box" = labelled lines that were recognised and whose box hits. The Azure row is checked against Tesseract words so it does not grade itself. That makes its number pessimistic. The full definition is in `ocr_compare/boxes.py`.
- **Time** is wall time per receipt. **Cost** is actual spend. Azure also gets a paid-equivalent price at the S0 list rate of $10 per 1,000 pages.
- **Haiku:** the free baseline is the saved 2026-10-11 production runs (prompt v3, `results/out/haiku-5-5/*-r*`), re-scored here. No new call was made.

## Results (20 receipts; items on the 8 labelled)
| config | total ok | lines sum = total | items ok /110 | disc /5 | extra | box present | box hit (Azure words) | box hit (Tess words) | GT lines with hit box | s/receipt mean (median) | $/receipt |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Azure prebuilt-receipt | 19/20 | 14/20 | **110 (100%)** | 2 | 3 | 100% | (98.9% self) | 72.1% | **73/110 (66%)**¹ | 5.75 (4.52)² | 0 on F0; 0.010 paid |
| Haiku prod-v3 low r1 / r2 (saved) | 20/20 | 18 / 20 | 108 / 109 | 5 | 0 | 6.7% / 5.7% | 6.7% / 5.7% | 6.2% / 5.2% | 0/110 | 7.5 / 8.0 | 0.0012–0.0013 |
| Haiku prod-v3 medium r1 / r2 (saved) | 20/20 | 20 / 19 | 109 / 109 | 5 | 0 | 16.5% | 16.5% | 15.5% / 13.9% | 18/110 / 0/110 | 9.6 / 9.3 | 0.0014–0.0015 |
| Tesseract eng psm6 | 3/20 | 0/20 | 27 (25%) | 2 | 12 | 100% | 95.3% | (self) | 27/110 (25%) | 0.28 | 0 |
| Tesseract eng psm6 x2 | 2/20 | 1/20 | 28 (25%) | 1 | 5 | 100% | 98.0% | (self) | 31/110 (28%) | 0.46 | 0 |

¹ Against Tesseract words, which misread a lot, so this is a lower bound. Against Azure's own words it is 110/110.
² Includes the tool's fixed 3 s wait before the first poll and the F0 request spacing. Azure's own processing time (`lastUpdated − created`) was 2.7 s on average (median 2.5 s).

Azure calls: **25 analyze POSTs** (20 accepted, 5 rejected with HTTP 429 and retried after `Retry-After`) and **29 result GETs**, all within the ≤ 30 analyze cap. The cap counts analyze POSTs only. Polling GETs are needed for every receipt, so 20 receipts cannot be done in 30 requests counting GETs. Reruns read the cache and make no call.

## Findings
- **Azure:** every recognised line has a box, and the boxes land on the right row. Line prices were the best measured (110/110), including Lidl receipt 04 "Reklamówka 0.65", which Haiku got wrong in all 4 runs. Weak points:
  - Discounts. On Lidl, Azure returns "OPUST Fairy …" as a *positive* line at the after-discount price instead of a negative line (3 extra lines, discounts 2/5).
  - It drops the receipt-level OPUST on Dodo (01).
  - It misread one total (05: 0.80 instead of 70.80).
  - It returned 2 of 5 lines on 13.

  These break "lines sum = total" on 6/20 receipts. The merchant is the legal name ("IMMOMOK Sp. z o.o." for E.Leclerc). There is no currency field; the date comes from `TransactionDate`.
- **Haiku prod-v3:** the best structure (totals 20/20, discounts 5/5, sum check 18–20/20), but boxes are nearly absent: 6–17% of lines have one, and 0–18 of 110 labelled lines. Every box Haiku did return was a hit.
- **Tesseract (eng only, pol missing):** not usable as a recogniser on these 768×1024 phone photos (25% of lines, 2–3/20 totals). The line parser is a simple, honest heuristic, documented in `tesseract_ocr.py`. As a box source it is weak too: GT-line coverage is only 25–28%, because many rows are not read at all (receipts 03, 04, 18). Upscaling 2x barely helps.

## Haiku matrix (planned, NOT run: no budget)
3 prompts × 4 thinking levels = **12 variants**:
- Prompts: `prod-v3` (production), `bbox-required` (prod-v3 with a required box per item), `full-structured` (items with qty/prices/box, discounts as their own rows with box, subtotal/tax/total with boxes, no rawLines).
- Thinking levels: `off` (thinking disabled, effort low), `low`, `medium`, `high`.

Over 20 receipts that is **240 calls**: **≈ $0.36 estimated, $2.03 worst case** (every call at max_tokens 16,384). Per variant the estimate is $0.021–0.043 (≈ $0.0011–0.0022 per receipt), from 2,844 measured input tokens and the saved runs' output tokens. `off` = 0.8 × low and `high` = 1.5 × medium are assumptions, plus about 25 tokens per required box. The table for each variant is in `report.md`. The runner stops before any call whose worst case would push the run over `--budget-usd`, and keeps a ledger in `ocr-compare/haiku_ledger.json`.

## Limitations
- Small set: 20 photos, 8 with line labels. Azure ran once (it is close to deterministic), and the Haiku numbers are the 2026-10-11 runs.
- The box hit check tests "the highlight lands on the right row", not how tight the box is. The reference layers are themselves OCR output (Azure is good; Tesseract eng is poor).
- Tesseract ran without Polish data (`pol` is not installed and was not downloaded); `--lang pol+eng --tessdata DIR` is ready for when it is. Its numbers are a floor.
- The Azure locale was auto-detected (`--locale pl-PL` was not tried). The F0 limit is ~500 pages/month; 20 were used.
- The Azure paid-equivalent price is the list S0 price for prebuilt models ($10/1k pages, first tier); check it on the pricing page before relying on it.

## Recommendation / next step (candidate)
1. **Hybrid looks the most promising:** Haiku keeps recognition (structure, discounts, totals); Azure (or Azure Read, if cheaper) supplies the boxes, by fuzzy-matching each Haiku line to an Azure item or line. Azure has a box for every line and gets the prices right. Measure it next, at no cost: match the cached Azure responses to the saved Haiku outputs and report GT lines with hit box.
2. If the owner prefers Haiku-only boxes, run a small slice of the matrix first, e.g. `bbox-required-low` + `full-structured-low` × 20 receipts × 2 repeats = 80 calls, ≈ $0.11 estimated, budget $0.20. Run the full matrix ($0.36 estimated, $2.10 budget) only if that slice shows promise.
3. Drop Tesseract unless `pol` data plus preprocessing (deskew, binarisation) changes the picture. It is not worth Render RAM at today's numbers.

## Files
- New: `tools/PurchaseMemory.ReceiptBenchmarks/run_ocr_compare.py`, `tools/PurchaseMemory.ReceiptBenchmarks/ocr_compare/` (`cli.py`, `common.py`, `boxes.py`, `azure_receipt.py`, `tesseract_ocr.py`, `haiku_matrix.py`, `report.py`, `README.md`, `tests/test_ocr_compare.py`, 14 tests), `test-data/receipts/household-2026-08/results/ocr-compare/` (they contain receipt text, as the other committed outputs do; no images).
- Changed: `score_items.py` (adds `pairs` to the per-receipt result; output unchanged), `.gitignore` (`__pycache__/`, `*.pyc`).
