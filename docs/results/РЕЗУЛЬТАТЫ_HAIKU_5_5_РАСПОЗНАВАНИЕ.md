# Experiment: Claude Haiku 5.5 for receipt recognition (2026-10-11)

Status: experiment done, independent review pending. No production code or config changed.

## Method
- Same prompt (`BasePrompt`, prompt v3) and the same schema v3 as production, read at runtime from
  `ReceiptAnalysisContracts.cs`. Same image bytes (household-2026-08 photos, 768x1024 JPEG), prompt text
  first and then the image, as `OpenAiResponsesTransport` sends them.
- Anthropic Messages API, `claude-haiku-5-5`, structured outputs (`output_config.format` json_schema),
  adaptive thinking, `max_tokens` 16384. The Anthropic schema subset does not allow numeric/length/pattern
  keywords (`minimum`, `maxLength`, `maxItems`...), so those were removed (`pattern` was removed too, unnecessarily; it only applies to currency). All outputs were then
  checked against the removed constraints: 0 violations in 82 responses.
- Configs: effort `low` (closest to GPT reasoning Low) and effort `medium` (Haiku default). Each ran twice
  on all 20 photos. 2 more calls were the step-1 smoke test.
- Scoring: `tools/PurchaseMemory.ReceiptBenchmarks/score_items.py` (unchanged) on the 8 receipts with
  line labels (`ground_truth_items.json`: 110 lines, 5 discounts). "Sum of lines = total" was checked the
  same way the prompt lab did it. Totals and dates were checked on all 20 against `ground_truth.json`.
- Baseline, with no new OpenAI calls: production `gpt-6-sol` + prompt v3 (= v9), 2 runs, from
  `РЕЗУЛЬТАТЫ_ЭКСПЕРИМЕНТ_ИНСТРУКЦИИ_ЧЕК_04.md` ("v9 on 8 labelled receipts"). Only these aggregate numbers
  were kept. The raw GPT outputs no longer exist, so receipts cannot be compared one by one.

## Results (8 labelled receipts, run 1; run 2)
| metric | gpt-6-sol v9 (prod) | Haiku 5.5 low | Haiku 5.5 medium |
|---|---|---|---|
| lines with correct price /110 (scorer) | 109; 109 | 108; 109 | 109; 109 |
| same, strict (review correction) | ? | 108; 108 | 108; 109 |
| discounts /5 | 5; 5 | 5; 5 | 5; 5 |
| sum of lines = total /8 | 8; 7 | 6; 8 | 8; 7 |
| total correct /8 | 8 (earlier run) | 8; 8 | 8; 8 |
| $ per receipt (8 labelled) | 0.020 | 0.0016 | 0.0019 |
| latency per receipt, median (8 labelled) | ~21 s* | 9.0; 10.2 s | 12.3; 11.0 s |

Strict row: `score_items.py:138,154` accepts Lidl's after-discount price as correct even when the separate discount line was also matched. In low-r2 and medium-r1 Haiku returned «Ogórki» at 1.60 plus the -0.20 discount line (double-counted), so those runs are really 108. The GPT baseline may share this leniency; it cannot be checked without its raw outputs. Honest reading: Haiku is 0–2 lines below GPT per run.

\* GPT latency for prompt v3 was not recorded. 21 s is the gpt-6-sol mean with the old prompt
(`РЕЗУЛЬТАТЫ_СРАВНЕНИЕ_OPENAI_ЦЕНЫ_ПОЗИЦИЙ.md`), and v3 produces more output, so 21 s is a lower bound.
`РЕЗУЛЬТАТЫ_ХОСТИНГ.md` reports ~26 s for one receipt in production.

All 20 photos (Haiku only; no GPT baseline exists for this set):
| | low r1 | low r2 | medium r1 | medium r2 |
|---|---|---|---|---|
| total correct /20 | 20 | 20 | 20 | 20 |
| date correct /20 | 20 | 20 | 20 | 20 |
| sum of lines = total /20 | 18 | 20 | 20 | 19 |
| latency median / p90 / max, s | 6.9 / 9.9 / 15.8 | 7.4 / 11.2 / 15.3 | 8.4 / 14.4 / 17.2 | 8.2 / 13.0 / 19.5 |
| $ per receipt (mean) | 0.00123 | 0.00127 | 0.00146 | 0.00144 |

Tokens: 2,844 input per receipt (prompt + schema + one image). Output averaged ~1.9k at low and ~2.3k at
medium; the long receipt 03 took ~4.6–5k. Prices used: $0.10 / $0.50 per MTok (Haiku 5.5, prompt ≤100K).
**Total spent: $0.111** (82 calls). OpenAI: $0.

## Errors
- Receipt 04 (Lidl, shifted price column): "Reklamówka 50µ 0.65" came back wrong in all 4 runs (0.05 or
  0.85), and the low-r1 run also failed the sum check there. This is the same hard receipt that tripped GPT
  before.
- Receipt 03 (27 lines), low r1: one price misread (2.99 → 2.59). The sum check caught it (216.86 vs 217.26).
- Most wrong prices broke "sum of lines = total", so the app's sum-mismatch warning flags them, but not all: in
  low-r2 and medium-r1, receipt 04 had two wrong lines (1.60 instead of 1.80, 0.85 instead of 0.65) that cancel
  out to the correct total 136.99. Likely coincidence (0.85 also appears in medium-r2 where the sum fails), but
  the sum warning is not a complete safety net.
- Names: Haiku sometimes "corrects" item names toward proper Polish even when its own `rawLines` show the
  printed text without diacritics. Example: Lidl "Ogorki" became "Ógórki", which is wrong. On Carrefour/Auchan
  it does the opposite: it keeps printed codes without diacritics ("C_MASLANKA" where the label says
  "MAŚLANKA"). Diacritics matched the labels 39–47 of 50–57 matched words. There is no GPT number for this.
- Bounding boxes: Haiku left most of them null (88 of 792 item boxes filled, total box 44 of 82). The "photo
  fragment" feature would show far fewer crops. GPT's box rate and box accuracy were not measured.
- No refusals, no truncation, no invalid JSON.

## Recommendation
**Adopt Haiku 5.5 at effort `low` as the primary recognizer, in a separate integration stage. Keep OpenAI as
the fallback.** Line, discount and total quality was within 0–2 lines of gpt-6-sol on the labelled set
(strict scoring). Haiku was about 2x faster or more and about 12x cheaper ($0.0016 vs $0.020 per receipt). Medium effort
cost more and was slower without measurably better results.

## Limitations
- Small sample: 8 labelled receipts, 2 runs each. The 1-line differences are noise.
- The GPT baseline is aggregate numbers from 2026-09-24. There are no paired per-receipt GPT outputs, and GPT
  latency for prompt v3 is an estimate.
- Images went straight to the API. They did not pass through the production workflow (parser, repair retry,
  postprocessing). Measured: line prices, discounts, totals, dates. Not measured: merchant normalisation,
  box accuracy.
- Haiku 5.5 safety refusals have no server-side fallback, so the integration must route refusals to OpenAI.

## Files
- Harness: `test-data/receipts/household-2026-08/results/run_haiku.py` (prompt and schema read from source;
  spend ledger with a hard stop).
- Raw outputs: `test-data/receipts/household-2026-08/results/out/haiku-5-5/{step1-low,low-r1,low-r2,medium-r1,medium-r2}/`.
  They contain the owner's receipt contents (no images). `.gitignore` explicitly allows JSON there and 61 earlier
  output files in `out/` are already committed, so committing them is consistent with existing practice.

## Next step (candidate)
1. Integration stage: an `anthropic` `IStructuredVisionProvider` behind the existing contract, with key
   `ANTHROPIC_API_KEY`, effort `low`, the strip-unsupported-keywords schema adapter plus server-side
   validation, and `openai` as the fallback.
2. GATE before switching (review): re-measure on household-2026-10 (10 labelled receipts) with both providers for a
   paired comparison, about $0.25 of OpenAI; also compare bounding-box fill rate (photo fragments risk).
3. Optional prompt tweak to keep names exactly as printed. Measure it.

## Review (2026-10-11)
Independent review re-ran the scorer on saved outputs (no paid calls): numbers reproduce; cost recomputed from usage matches ($0.1108, 82 calls); pricing $0.10/$0.50 per MTok confirmed; no secrets in harness/outputs. Two corrections applied above (strict line score; sum check is not a complete safety net). Recommendation supported, with the paired re-measurement as a gate.
