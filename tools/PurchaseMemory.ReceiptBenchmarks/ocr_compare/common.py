"""Shared paths, receipt discovery and the per-receipt result shape.

Every provider writes one JSON per receipt in the shape score_items.py already
reads (`receiptId`, `configuration`, `model`, `seconds`, `status`, `parsed`
with `total` and `items[].lineTotal`), so `score_items.py <run dir>` works on
these folders too. Items additionally carry `box`: [x, y, width, height] as
fractions of the image (top-left origin) or null.
"""
import importlib.util
import json
import pathlib
import sys

TOOL_DIR = pathlib.Path(__file__).resolve().parent
BENCH_DIR = TOOL_DIR.parent
ROOT = BENCH_DIR.parents[1]
DATASET = ROOT / "test-data" / "receipts" / "household-2026-08"
RESULTS = DATASET / "results"
DEFAULT_OUT = RESULTS / "ocr-compare"


def load_score_items():
    """Import tools/PurchaseMemory.ReceiptBenchmarks/score_items.py as a module."""
    if "score_items" in sys.modules:
        return sys.modules["score_items"]
    spec = importlib.util.spec_from_file_location("score_items", BENCH_DIR / "score_items.py")
    module = importlib.util.module_from_spec(spec)
    sys.modules["score_items"] = module
    spec.loader.exec_module(module)
    return module


def receipt_ids(images_dir, selection=None):
    """`receipt_NN.jpg` ids present in images_dir, optionally filtered ("01,03")."""
    found = sorted(p.stem.split("_", 1)[1] for p in pathlib.Path(images_dir).glob("receipt_*.jpg"))
    if selection:
        wanted = [s.strip() for s in selection.split(",") if s.strip()]
        missing = [w for w in wanted if w not in found]
        if missing:
            raise SystemExit(f"receipts not found in {images_dir}: {','.join(missing)}")
        return wanted
    return found


def image_path(images_dir, receipt_id):
    return pathlib.Path(images_dir) / f"receipt_{receipt_id}.jpg"


def result_record(receipt_id, configuration, model, seconds, parsed, status="success",
                  error=None, cost_usd=0.0, paid_equivalent_usd=None, usage=None, extra=None):
    record = {
        "receiptId": receipt_id,
        "configuration": configuration,
        "model": model,
        "seconds": None if seconds is None else round(seconds, 2),
        "status": status,
        "error": error,
        "costUsd": round(cost_usd, 6),
        "paidEquivalentUsd": None if paid_equivalent_usd is None else round(paid_equivalent_usd, 6),
        "usage": usage,
        "parsed": parsed,
    }
    if extra:
        record.update(extra)
    return record


def write_json(path, data):
    path = pathlib.Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=1))


def read_json(path):
    return json.loads(pathlib.Path(path).read_text())


def write_run(out_dir, record):
    """runs/<configuration>/<configuration>__<model>__<NN>.json (score_items naming)."""
    folder = pathlib.Path(out_dir) / "runs" / record["configuration"]
    write_json(folder / f"{record['configuration']}__{record['model']}__{record['receiptId']}.json", record)


def load_runs(out_dir):
    """{configuration: {receipt_id: record}} for every run folder under out_dir/runs."""
    runs = {}
    for path in sorted((pathlib.Path(out_dir) / "runs").glob("*/*.json")):
        record = read_json(path)
        runs.setdefault(record["configuration"], {})[record["receiptId"]] = record
    return runs
