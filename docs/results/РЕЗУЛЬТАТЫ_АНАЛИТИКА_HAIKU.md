# Analytics planner on Haiku — results (14.10.2026)

Status: **done. OpenAI `gpt-6-luna` stays the primary planner (owner's decision);
Claude Haiku 5.5 is the fallback when OpenAI fails.** On the measured set Haiku was
faster but less accurate and, per question, more expensive.

## What changed

- `AnalyticsPlanContract` (Infrastructure/Analytics) holds the provider-neutral
  prompt, strict JSON schema, codebook and parser. They were moved unchanged out of
  `OpenAiAnalyticsQueryPlanner`, and both planners send the identical prompt and schema.
- `AnthropicAnalyticsQueryPlanner` calls Claude Haiku 5.5 over the existing
  `AnthropicMessagesTransport` (structured output, adaptive thinking, effort Low).
  - These failures become `AnalyticsPlanningException` with a log-safe category:
    a stop reason other than `end_turn`, a refusal, an empty or malformed answer,
    and transport failures (429, 529, 5xx, timeout, network, unexpected SDK errors).
  - Logs carry tokens and estimated cost (events 3421/3422), never the question or the key.
- `OpenAiAnalyticsQueryPlanner` now also wraps unexpected exceptions into
  `AnalyticsPlanningException` (`unexpected:<Type>`), so the fallback covers
  any OpenAI failure. Cancellation by the caller still propagates.
- `AnalyticsQueryPlannerRouter` is the `IAnalyticsQueryPlanner` the app sees.
  - It tries `Analytics:Provider`, then `Analytics:FallbackProviders` in order.
  - It skips a planner without a key, and a failure hands the question to the next planner.
  - A plan with `Understood = false` is an answer and is never retried elsewhere
    (events 3423/3424).
- `AnalyticsOptions` + `AnalyticsOptionsValidator` (ValidateOnStart).
  - New keys: `Provider`, `FallbackProviders`, `AnthropicModel`, `AnthropicEffort`,
    `AnthropicMaximumOutputTokens`, `AnthropicMaximumRetries`.
  - Renamed OpenAI keys: `Model` → `OpenAiModel`, `MaximumOutputTokens` →
    `OpenAiMaximumOutputTokens`, `ReasoningEffort` → `OpenAiReasoningEffort`.
  - Render has no `Analytics__*` env vars, so the rename is safe there.
- `appsettings.json`: `Provider: openai`, `FallbackProviders: ["anthropic"]`.
- `OpenAiTextTransport` takes an optional `maximumRetries`. The default (null)
  keeps the SDK policy, so categorization is unchanged.
- Benchmark tool `tools/PurchaseMemory.AnalyticsPlannerBenchmark` with the fixed set
  `test-data/analytics/planner_questions.json`.
  - 21 Russian questions: totals by period, average/max price, by store, month
    comparison, top-N, tags, discounts, a follow-up and 2 unsupported.
  - Synthetic vocabulary: seeded category tree, 10 stores, 20 brands, 5 tags; no database.
  - Each plan is validated with `AnalyticsPlanValidator` and scored on every
    expected field. `anyOf` allows alternative expectations.
- `docs/ПЕРЕМЕННЫЕ_И_СЕКРЕТЫ.md`: the `Analytics__*` row is updated. Help texts
  don't name a provider, so they are unchanged.

## Worst-case wait

With a fallback configured (`HasFallback`), every planner makes a single attempt:
no OpenAI SDK retries and Anthropic retries = 0. The fallback is the retry. Each
attempt is bounded by `RequestTimeoutSeconds` (30 s), so the worst case is
OpenAI 30 s + Haiku 30 s ≈ 60 s. Without a fallback, the SDK defaults apply
(OpenAI SDK retry policy, `AnthropicMaximumRetries` = 1).

## Measurement (root, 14.10.2026, 21 questions, synthetic household)

| Planner | Valid | Correct (old scorer) | Correct (adjusted) | Median latency | Cost / question |
|---|---|---|---|---|---|
| gpt-6-luna, reasoning Low | 21/21 | 20/21 | **21/21** | 3138 ms | $0.000259 |
| claude-haiku-5-5, effort Low | 21/21 | 18/21 | **17/21** | 1522 ms | $0.000413 |
| claude-haiku-5-5, effort Medium | 21/21 | 19/21 | **19/21** | 1957 ms | $0.000457 |

How the old scorer was adjusted:
- `by-store-all`: the old scorer wrongly rejected a valid answer (OpenAI). Top-1 by
  merchant is now accepted too.
- `beer-frequency`: category `alcohol` is now wrong, because categories and name
  terms are OR-ed and wine and spirits would be counted too.
- The question file already contains both changes, so the next run scores this way.

Haiku Low misses:
- `coffee-brand`: metric Top by spend instead of purchase count.
- `milka-two-stores`: no chocolate category and no matching term.
- `beer-frequency`: over-counts with category `alcohol`.

Input tokens are ≈ 3.49k for Haiku vs ≈ 1.87k for OpenAI on the same prompt. The
gap is the Anthropic structured-output overhead and tokenizer, and it was not
optimised. List prices are equal ($0.10 / $0.50 per 1M), so Haiku costs more per
question here.

Total spend: ≈ $0.024 (root's runs). The implementation agent spent $0.

## Decision

The owner keeps OpenAI as the primary planner. Haiku answers only when OpenAI fails
(no key, error, timeout, refusal, malformed answer). Possible follow-ups:
- Tune the prompt for Haiku (coffee-brand, Milka).
- Cut Haiku input tokens.

## Tests

- `AnthropicAnalyticsPlannerTests` covers:
  - request shape and answer mapping;
  - the schema is unchanged by the Claude adapter (100-tag enum);
  - stop reasons, refusal and malformed JSON;
  - 429, 529, timeout and unexpected errors;
  - caller cancellation and whether the planner counts as configured;
  - router fallback, skipping a planner without a key, no retry of an answer, the last failure;
  - settings validation, `HasFallback`, host wiring and rejection of bad settings at startup.
- `AnalyticsPlanningTests` now targets `AnalyticsPlanContract`. It also covers
  OpenAI unexpected-exception wrapping and caller cancellation.

## Limitations

- Claude compiles each new schema once (cached 24 h). The schema changes when
  the household's categories, stores, brands or tags change, so the first
  fallback question after such a change may be slower.
- The scorer still accepts several readings for ambiguous wording (periods,
  tag «акция» vs the discount filter).
