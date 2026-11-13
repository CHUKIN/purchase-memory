# Haiku 5.5 as primary receipt recognizer (2026-10-11)

Status: implemented, independent review pending. Follows `РЕЗУЛЬТАТЫ_HAIKU_5_5_РАСПОЗНАВАНИЕ.md`.

## What changed
- New `anthropic` pipeline behind the existing contract:
  - `AnthropicStructuredVisionProvider` (`IStructuredVisionProvider`, id `anthropic`, key from env `ANTHROPIC_API_KEY` only).
  - `AnthropicMessagesTransport` is the only class that uses the official SDK (`Anthropic` NuGet 12.55.0, pinned in
    `Directory.Packages.props`). Messages API, adaptive thinking, `output_config.effort`, structured outputs
    (`output_config.format` json_schema). Prompt text first, then the base64 images, as on OpenAI.
  - `AnthropicJsonSchemaAdapter` removes keywords that the Anthropic schema subset rejects (`minimum`, `maximum`,
    `exclusive*`, `multipleOf`, `min/maxLength`, `pattern`, `min/maxItems`, `$schema`, `$id`, `title`). Property names
    are never stripped. The removed limits are still enforced by the shared `ReceiptStructuredOutputParser`: name and
    unit lengths, merchant length, non-negative total and unit price, positive quantity, currency `^[A-Z]{3}$`, at most
    300 items. Out-of-range boxes degrade to null.
  - `AnthropicReceiptExtractor` (`IReceiptRecognitionPipeline`): the shared workflow with the same prompt v3 and schema
    v3, plus one repair retry.
- Same prompt for both providers. No Claude-specific addendum (measured and rejected, see below).
- Preflight size check: a photo over 10 MB after base64 encoding, or over 30 MB per request, is refused before upload.
  The Claude API limits are 10 MB per image and 32 MB per request. The router then falls back to OpenAI.
- Logging: provider, model, effort, latency, message id, input/output tokens, estimated cost (Haiku list price
  $0.10/$0.50 per MTok). Failures log a category (`timeout`, `network`, `unauthorized`, `rate_limited`, `overloaded`,
  `http_NNN`, `refusal:<category>`, `max_tokens`, `stop_reason:*`, `empty`, `not_configured`). The router logs the
  fallback reason. The key, prompts, JSON and images are never logged.
- Benchmark: `tools/PurchaseMemory.ReceiptBenchmarks anthropic` uses the real provider and workflow, accepts any
  dataset folder (`--dataset`), and keeps a spend ledger with a hard stop (`--ledger`, `--stop-at`). It constructs
  only the Anthropic provider, so OpenAI cannot be called. `score_items.py --strict` is the stricter Lidl net-price
  rule from the experiment review. `score_names.py` measures how exactly names are copied.
- Help (ru/en, review-save topic): the issue card shows the whole photo when the line can't be located. This now
  happens more often because Haiku rarely returns boxes.
- Docs: `КОНТЕКСТ_ПРОЕКТА.md`, `docs/ДЕПЛОЙ_КАК_ЭТО_УСТРОЕНО.md`, `docs/ДЕПЛОЙ_RENDER.md`,
  `docs/ПЕРЕМЕННЫЕ_И_СЕКРЕТЫ.md`, `docs/secrets.template.env`.

## Config (appsettings.json = local and production; Render has no override)
`Pipeline=anthropic`, `FallbackPipelines=[openai, ollama]`, `AnthropicModel=claude-haiku-5-5`, `AnthropicEffort=Low`,
`AnthropicRequestTimeoutSeconds=120` (per attempt), `AnthropicMaximumOutputTokens=16384`, `AnthropicMaximumRetries=1`
(SDK retry for 408/409/429/5xx/connection). The validator requires `AnthropicModel` when anthropic is in the route,
checks the ranges, and requires the timeout to allow at least 1 s per 150 output tokens.

## Fallback behaviour (anthropic → openai → ollama)
Each of the following raises `ReceiptExtractionException`, and the router moves to OpenAI:
- refusal;
- `max_tokens` or any stop reason other than `end_turn`;
- empty text;
- invalid JSON after the repair retry;
- timeout or network failure;
- 401/403, 429, 5xx, 529 overloaded, 400;
- unsupported image type or oversized photo;
- missing `ANTHROPIC_API_KEY`.

Without the key the app starts normally. Status reports `openai fallback` with the message "anthropic: Set
ANTHROPIC_API_KEY on the backend". Ollama rules are unchanged. Worst-case delay before the fallback is a hung call:
2 × 120 s.

## Measurements (real .NET provider + workflow; no OpenAI calls)
Production config, 2 runs each. Lines and discounts come from `score_items.py`. Lenient and strict scoring were
identical in every run.

| set | lines ok | discounts | totals | latency median / p90 / max | $/receipt | item boxes | total boxes |
|---|---|---|---|---|---|---|---|
| household-2026-08, 8 labelled | 108; 109 /110 | 5/5; 5/5 | 8/8; 8/8 | 10.8/14.8/14.8 s; 9.8/17.1/17.1 s | 0.00169; 0.00165 | 0/115; 0/115 | 4/8; 0/8 |
| household-2026-10, 10 labelled (+ non-receipt 07) | 24/24; 24/24 | 6/6; 6/6 | 10/10; 10/10 | 2.3/4.7/9.8 s; 2.5/5.6/12.3 s | 0.00087; 0.00093 | 8/30; 8/30 | 9/11; 10/11 |

- No refusals, truncations or invalid JSON. Every receipt took 1 call (no repair).
- 2026-08 errors: receipt 04 (Lidl) again got «Reklamówka» 0.85/0.05 instead of 0.65, and «Lay's Chipsy» 6.29 instead
  of 8.29 in one run. These match the experiment's known hard receipt.
- Receipt 07 of 2026-10 is a card slip, not a receipt. It came back as total 9.96 with 0 items, so it would create
  an empty-items draft for review.
- Comparison with GPT: no stored gpt-6-sol outputs exist for 2026-10, so the numbers above are Haiku only. On 2026-08
  the reference is the experiment's gpt-6-sol baseline (109; 109 /110, 5/5, ~$0.020/receipt, ≥21 s).
- 2026-10 photos are higher resolution: 5,696 input tokens per receipt vs 2,844 on 2026-08.
- Names (`score_names.py`, 2026-08, 110 matched lines). Exact copies: 86 and 89. Diacritics added: 2 and 1. Diacritics
  dropped: 8 and 7. Other differences: 13 and 13. On 2026-10: 18/24 exact in both runs.

### Rejected: Claude-only "copy names exactly as printed" sentence
I measured one Polish sentence appended for Claude only, using the same matrix (2 runs on each set). Prices on
2026-08 were 109 and 110 out of 110 (noise level). Names got worse: exact copies dropped from 86/89 to 61/71, with more
dropped diacritics (17 and 8) and more other edits (32 and 29). On 2026-10 one run lost 2 of 6 discounts (4/6). The
option and the sentence were removed from the code. The prompt is unchanged for both providers.

## Cost
Anthropic spend for this stage: **$0.0971** (77 calls, ledger). OpenAI: $0. Expected production cost is
$0.001–0.002 per receipt, against ~$0.02 with gpt-6-sol.

## Checks
- `./scripts/dotnet.sh build`: 0 warnings, 0 errors.
- `./scripts/dotnet.sh test`: 609 passed, 8 skipped (external services), 0 failed. That includes 38 new Anthropic
  tests and 3 new validator tests. `AuthApiTests.SpaShellHealthAndAuthConfigArePublic` needs `wwwroot`; it passes
  after `npm run build`.
- Web: `npx tsc -b` OK; `npx vitest run` 34 files, 271 tests passed.
- `data/purchases.db` was not touched (mtime and size unchanged).

## Limitations
- Bounding boxes: Haiku filled 0 of 115 item boxes on 2026-08 and 8 of 30 on 2026-10. Issue cards fall back to the
  whole photo, which already worked.
- Haiku sometimes drops or adds diacritics, or misreads letters in names (for example "TVARD", "NOVENPICK"). Names are
  editable in review.
- Photos over 10 MB after encoding (originals up to 15 MB are allowed) or over 8000 px always go to OpenAI and are
  billed there. Sending the display copy (2400 px) to Claude would avoid that, but it changes pipeline input and needs
  its own measurement. Claude downsizes large images server-side anyway.
- Small sample, and there is no paired GPT comparison on 2026-10.

## Deploy steps (owner)
1. Publish the new image (`scripts/publish-image.sh`), then Render → Manual Deploy.
2. Render → `purchase-memory` → Environment → add the secret `ANTHROPIC_API_KEY` (from console.anthropic.com → API keys)
   → Save. The agent never handles the value. Set a spend limit in the Anthropic console.
3. Check the status line: it should say `anthropic · fallback: openai, ollama`. Without the key it says
   `openai fallback`, and everything works as before.

## Next step (candidate)
Optional paired GPT run on 2026-10 (~$0.25 OpenAI). Then measure Claude on the display copy instead of the original
for oversized photos.
