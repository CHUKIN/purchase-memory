#!/usr/bin/env python3
"""Track B research driver: approach A (direct vision LLM) vs approach B (Apple Vision OCR -> text LLM).

Everything runs against the LOCAL Ollama daemon on this machine only. Receipt images and their
text never leave localhost.

Usage: run_compare.py A|B <receipt_number> [<receipt_number> ...]
Writes results/out/<NN>_<A|B>.json
"""
import base64
import json
import os
import pathlib
import subprocess
import sys
import time
import urllib.request

HERE = pathlib.Path(__file__).resolve().parent
RECEIPTS = HERE.parent
OUT = HERE / "out"
OUT.mkdir(exist_ok=True)

OLLAMA = "http://127.0.0.1:11434/api/generate"
VISION_MODEL = "qwen3-vl:8b-instruct"
TEXT_MODEL = "qwen3.5:9b"

SCHEMA_BLOCK = """Return ONLY a JSON object, no prose, no markdown fences, with exactly this shape:
{
  "merchant": "<customer-facing store/brand name>",
  "date": "<YYYY-MM-DD>",
  "currency": "<ISO code, e.g. PLN or EUR>",
  "total": <number, the grand total actually paid (SUMA PLN / Summe / Total)>,
  "items": [
    {"name": "<product name EXACTLY as printed, keep original spelling and diacritics>",
     "quantity": <number or null>,
     "unit_price": <number or null>,
     "line_total": <number or null>}
  ]
}

Rules:
- Keep product names in the original language with correct Polish/German diacritics (ą ć ę ł ń ó ś ź ż, ä ö ü ß). Do NOT translate.
- List ONLY real purchased products. Discount lines (OPUST, RABAT, OPUSTY LACZNIE), subtotals
  (Podsuma), tax rows (PTU, SUMA PTU, MwSt), payment rows (KARTA, GOTOWKA, RESZTA) and the grand
  total are NOT products.
- If a product has a discount, use the final amount paid for that product as line_total.
- Never invent a product that is not there. Never merge two products into one.
- Use a decimal point for numbers. Use null when a value is not printed."""

VISION_PROMPT = "This is a photo of a retail receipt (Polish or German). Read it and extract the structured data.\n\n" + SCHEMA_BLOCK

def _balanced_objects(text):
    """Yield every top-level balanced {...} span in text."""
    depth = start = 0
    in_str = esc = False
    for i, c in enumerate(text):
        if in_str:
            if esc:
                esc = False
            elif c == "\\":
                esc = True
            elif c == '"':
                in_str = False
            continue
        if c == '"':
            in_str = True
        elif c == "{":
            if depth == 0:
                start = i
            depth += 1
        elif c == "}" and depth:
            depth -= 1
            if depth == 0:
                yield text[start:i + 1]


def extract_json(text):
    """Pull the receipt JSON out of a possibly fenced / prose-wrapped / thinking-wrapped reply.

    Prefers the LAST parseable object that carries an "items" key, because when the
    payload is recovered from a <think> block the final restatement is the intended answer.
    """
    if not text:
        raise ValueError("empty response")
    best = None
    for span in _balanced_objects(text):
        try:
            obj = json.loads(span)
        except Exception:
            continue
        if isinstance(obj, dict):
            if "items" in obj:
                best = obj
            elif best is None:
                best = obj
    if best is None:
        raise ValueError("no parseable JSON object in response")
    return best


def ollama(payload):
    req = urllib.request.Request(
        OLLAMA,
        data=json.dumps(payload).encode(),
        headers={"Content-Type": "application/json"},
    )
    with urllib.request.urlopen(req, timeout=1800) as r:
        return json.loads(r.read())


def run_a(img):
    b64 = base64.b64encode(img.read_bytes()).decode()
    t0 = time.time()
    resp = ollama({
        "model": VISION_MODEL,
        "prompt": VISION_PROMPT,
        "images": [b64],
        "stream": False,
        # NOTE 1: qwen3-vl:8b returns an EMPTY response when Ollama's structured
        # "format": "json" mode is enabled, so approach A asks for JSON in the
        # prompt and the reply is extracted with extract_json() below.
        # NOTE 2: the plain "qwen3-vl:8b" tag is the THINKING build and does NOT
        # honour "think": False -- it still emits a <think> block that Ollama routes
        # into a separate "thinking" field, and on dense receipts it loops there until
        # the token cap without ever emitting an answer. The "8b-instruct" tag is the
        # non-thinking build of the same model and is used instead.
        "options": {"temperature": 0, "num_ctx": 16384, "num_predict": 4000},
    })
    text = resp.get("response", "") or ""
    if "{" not in text:
        text = resp.get("thinking", "") or text
    return text, time.time() - t0, None


def run_b(img, langs):
    t0 = time.time()
    ocr = subprocess.run(
        [str(HERE / "ocr_only"), str(img)] + langs,
        capture_output=True, text=True, check=True,
    ).stdout
    prompt = (
        "Below is raw OCR text of a retail receipt (Polish or German), one line per detected row. "
        "The OCR may have merged columns or mangled characters; use your judgement, but do not invent data.\n\n"
        "=== OCR TEXT ===\n" + ocr + "\n=== END OCR TEXT ===\n\n" + SCHEMA_BLOCK
    )
    resp = ollama({
        "model": TEXT_MODEL,
        "prompt": prompt,
        "stream": False,
        "think": False,
        "format": "json",
        "options": {"temperature": 0, "num_ctx": 16384, "num_predict": 3000},
    })
    return resp.get("response", ""), time.time() - t0, ocr


def main():
    approach = sys.argv[1].upper()
    langs_env = os.environ.get("OCR_LANGS", "pl-PL").split(",")
    for num in sys.argv[2:]:
        nn = f"{int(num):02d}"
        img = RECEIPTS / f"receipt_{nn}.jpg"
        try:
            if approach == "A":
                raw, secs, ocr = run_a(img)
            else:
                raw, secs, ocr = run_b(img, langs_env)
            try:
                parsed = extract_json(raw)
                err = None
            except Exception as e:
                parsed, err = None, f"json parse: {e}"
        except Exception as e:
            raw, secs, ocr, parsed, err = "", 0.0, None, None, str(e)

        rec = {"receipt": nn, "approach": approach, "seconds": round(secs, 1),
               "error": err, "parsed": parsed, "raw": raw}
        if ocr is not None:
            (OUT / f"{nn}_ocr.txt").write_text(ocr)
        (OUT / f"{nn}_{approach}.json").write_text(json.dumps(rec, ensure_ascii=False, indent=2))
        n_items = len(parsed.get("items", [])) if parsed else -1
        total = parsed.get("total") if parsed else None
        print(f"{nn} {approach}: {secs:6.1f}s  total={total}  items={n_items}  {err or ''}", flush=True)


if __name__ == "__main__":
    main()
