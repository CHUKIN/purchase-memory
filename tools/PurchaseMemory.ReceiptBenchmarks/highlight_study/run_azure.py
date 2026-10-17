"""Stage 1.3: live Azure runs into the scratchpad only (never into the repo).

Reuses ocr_compare.azure_receipt.AzureClient; one shared ledger for all
models caps analyze POSTs (429s included). Key comes from the environment
(zsh -ic) and is never printed.

Usage: python -I run_azure.py <repo> <out> <cap>
"""
import io
import json
import pathlib
import sys

REPO = pathlib.Path(sys.argv[1])
OUT = pathlib.Path(sys.argv[2])
CAP = int(sys.argv[3])
sys.path.insert(0, str(REPO / "tools" / "PurchaseMemory.ReceiptBenchmarks"))

from ocr_compare import azure_receipt  # noqa: E402
from PIL import Image  # noqa: E402

TR = REPO / "test-data" / "receipts"
LEDGER = OUT / "cache" / "_calls.json"

jobs = []  # (model, id, path)
for p in sorted((TR / "household-2026-10").glob("receipt_*.jpg")):
    jobs.append(("prebuilt-receipt", "h10_" + p.stem.split("_")[1], p))
for name in ["01-fakturaxl-sample.png", "02-sklep-u-asi.jpg", "03-spolem.webp", "04-wielkopolska.jpg"]:
    jobs.append(("prebuilt-receipt", "root_" + name.split(".")[0], TR / name))
for name in ["biedronka-2020-01-09-cc-by-sa.jpg", "biedronka-eparagon-2024-07-official.jpg",
             "lidl-2025-official-sample.png"]:
    jobs.append(("prebuilt-receipt", "pub_" + name.split(".")[0], TR / "public" / name))
for n in ["04", "20", "11"]:
    jobs.append(("prebuilt-receipt", "h08_" + n, TR / "household-2026-08" / f"receipt_{n}.jpg"))
for n in ["04", "20", "11"]:
    jobs.append(("prebuilt-read", "h08_" + n, TR / "household-2026-08" / f"receipt_{n}.jpg"))
if len(sys.argv) > 4:
    jobs = [j for j in jobs if j[1] in sys.argv[4].split(",")]

clients = {}
for model in ("prebuilt-receipt", "prebuilt-read"):
    c = azure_receipt.AzureClient(OUT / "cache" / model, CAP)
    c.ledger_path = LEDGER
    clients[model] = c


def image_bytes(path):
    if path.suffix == ".webp":  # Document Intelligence does not take WebP
        buf = io.BytesIO()
        Image.open(path).convert("RGB").save(buf, "PNG")
        return buf.getvalue()
    return path.read_bytes()


log = []
for model, rid, path in jobs:
    azure_receipt.MODEL_ID = model
    client = clients[model]
    try:
        entry, called = client.cached_or_analyze(rid, image_bytes(path), allow_calls=True)
        res = entry["response"].get("analyzeResult", {})
        page = (res.get("pages") or [{}])[0]
        msg = f"{model:17s} {rid:45s} called={called} lines={len(page.get('lines') or [])} sec={entry['meta']['seconds']}"
    except azure_receipt.AzureError as e:
        msg = f"{model:17s} {rid:45s} ERROR {e}"
    print(msg, flush=True)
    log.append(msg)
print(json.dumps(clients["prebuilt-receipt"].ledger()))
