#!/usr/bin/env python3
"""Experiment harness: production receipt prompt + schema on Claude (Anthropic Messages API).

Not production code. Sends the SAME prompt (BasePrompt) and the SAME v3 JSON schema
that the API sends to OpenAI today, read at runtime from
src/PurchaseMemory.Application/Receipts/ReceiptAnalysisContracts.cs, with the same
image bytes (prompt text first, then the image, as OpenAiResponsesTransport does).
The schema goes through structured outputs (output_config.format); keywords the
Anthropic schema subset does not accept (minimum, maxLength, pattern, ...) are
stripped and the remaining shape is unchanged.

Output: one JSON per receipt in --out, in the shape score_items.py reads
(receiptId / configuration / model / seconds / usage / parsed{total, items[lineTotal]}).
Spend is tracked from response usage in --ledger; the run stops before a call that
could push the cumulative spend over --stop-at (worst case = full max_tokens).

The key is read by the SDK from ANTHROPIC_API_KEY; this script never reads it.
Usage:
  zsh -ic 'python3 run_haiku.py --receipts 04,01 --effort low --label low-r1 \
      --out out/haiku-5-5/low-r1 --ledger /path/spend.json'
"""
import argparse
import base64
import json
import pathlib
import re
import sys
import textwrap
import time

import anthropic

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[3]
CONTRACT = ROOT / "src" / "PurchaseMemory.Application" / "Receipts" / "ReceiptAnalysisContracts.cs"
IMAGES = HERE.parent

MODEL = "claude-haiku-5-5"
# Official Claude Haiku 5.5 rates for prompts <= 100K tokens (USD per token).
PRICE_IN = 0.10 / 1_000_000
PRICE_OUT = 0.50 / 1_000_000
MAX_TOKENS = 16384  # same ceiling as OpenAiMaximumOutputTokens

UNSUPPORTED = {"minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "multipleOf",
               "minLength", "maxLength", "pattern", "maxItems", "minItems",
               "$schema", "$id", "title"}


def raw_literal(source, name):
    """Return the text of a C# raw string literal `name = \"\"\" ... \"\"\";`."""
    match = re.search(name + r'\s*=\s*"""\n(.*?)\n(\s*)""";', source, re.S)
    if not match:
        sys.exit(f"{name} not found in {CONTRACT}")
    return textwrap.dedent(match.group(1) + "\n" + match.group(2)).rstrip("\n")


def strip_schema(node):
    if isinstance(node, dict):
        return {k: strip_schema(v) for k, v in node.items() if k not in UNSUPPORTED}
    if isinstance(node, list):
        return [strip_schema(v) for v in node]
    return node


def load_contract():
    source = CONTRACT.read_text()
    prompt = raw_literal(source, "BasePrompt")
    schema = strip_schema(json.loads(raw_literal(source, "JsonSchema")))
    return prompt, schema


def ledger_read(path):
    return json.loads(path.read_text()) if path.exists() else {"spent_usd": 0.0, "calls": []}


def to_parsed(data):
    """Map the v3 contract fields onto score.py / score_items.py's `parsed` shape."""
    return {
        "merchant": data.get("merchantName"),
        "date": data.get("purchasedAt"),
        "currency": data.get("currency"),
        "total": data.get("receiptTotal"),
        "items": [{"name": i.get("name"), "quantity": i.get("quantity"),
                   "unitPrice": i.get("unitPrice"), "lineTotal": i.get("lineTotal")}
                  for i in data.get("items") or [] if isinstance(i, dict)],
    }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--receipts", required=True, help="comma list, e.g. 01,03 or 'all'")
    ap.add_argument("--effort", default="low", choices=["low", "medium", "high"])
    ap.add_argument("--label", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--ledger", required=True)
    ap.add_argument("--stop-at", type=float, default=1.80)
    args = ap.parse_args()

    prompt, schema = load_contract()
    receipts = ([f"{n:02d}" for n in range(1, 21)] if args.receipts == "all"
                else args.receipts.split(","))
    out = pathlib.Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    ledger_path = pathlib.Path(args.ledger)
    client = anthropic.Anthropic(max_retries=2, timeout=300)
    worst_case = MAX_TOKENS * PRICE_OUT + 10_000 * PRICE_IN

    for nn in receipts:
        ledger = ledger_read(ledger_path)
        if ledger["spent_usd"] + worst_case > args.stop_at:
            print(f"STOP: spent ${ledger['spent_usd']:.4f}; next call could exceed ${args.stop_at}")
            sys.exit(3)
        image = (IMAGES / f"receipt_{nn}.jpg").read_bytes()
        request = dict(
            model=MODEL,
            max_tokens=MAX_TOKENS,
            thinking={"type": "adaptive"},
            output_config={"effort": args.effort,
                           "format": {"type": "json_schema", "schema": schema}},
            messages=[{"role": "user", "content": [
                {"type": "text", "text": prompt},
                {"type": "image", "source": {"type": "base64", "media_type": "image/jpeg",
                                             "data": base64.standard_b64encode(image).decode()}},
            ]}],
        )
        started = time.monotonic()
        result = {"receiptId": nn, "configuration": f"haiku-{args.label}", "model": MODEL,
                  "effort": args.effort}
        try:
            with client.messages.stream(**request) as stream:
                message = stream.get_final_message()
            seconds = time.monotonic() - started
            usage = message.usage
            cost = usage.input_tokens * PRICE_IN + usage.output_tokens * PRICE_OUT
            text = next((b.text for b in message.content if b.type == "text"), None)
            result.update(seconds=round(seconds, 2), stopReason=message.stop_reason,
                          requestId=getattr(message, "_request_id", None) or message.id,
                          usage={"inputTokens": usage.input_tokens, "outputTokens": usage.output_tokens},
                          costUsd=round(cost, 6))
            if message.stop_reason != "end_turn" or text is None:
                result.update(status="error", error=f"stop_reason={message.stop_reason}", parsed=None)
            else:
                data = json.loads(text)
                result.update(status="success", parsed=to_parsed(data), raw=data)
        except anthropic.APIStatusError as e:
            seconds, cost = time.monotonic() - started, 0.0
            result.update(status="error", seconds=round(seconds, 2),
                          error=f"http {e.status_code}: {str(e)[:300]}", parsed=None)
        except anthropic.APIConnectionError as e:
            seconds, cost = time.monotonic() - started, 0.0
            result.update(status="error", seconds=round(seconds, 2),
                          error=f"connection: {str(e)[:300]}", parsed=None)
        except json.JSONDecodeError as e:
            result.update(status="error", error=f"invalid json: {e}", parsed=None)

        ledger["spent_usd"] = round(ledger["spent_usd"] + cost, 6)
        ledger["calls"].append({"label": args.label, "receipt": nn, "cost": round(cost, 6),
                                "usage": result.get("usage")})
        ledger_path.write_text(json.dumps(ledger, indent=1))
        (out / f"haiku-{args.label}__{MODEL}__{nn}.json").write_text(
            json.dumps(result, ensure_ascii=False, indent=2))
        p = result.get("parsed") or {}
        print(f"{nn}: {result['status']} {result.get('seconds')}s "
              f"tok {result.get('usage')} ${cost:.5f} total={p.get('total')} "
              f"items={len(p.get('items') or [])} {result.get('error') or ''} "
              f"| cum ${ledger['spent_usd']:.4f}", flush=True)


if __name__ == "__main__":
    main()
