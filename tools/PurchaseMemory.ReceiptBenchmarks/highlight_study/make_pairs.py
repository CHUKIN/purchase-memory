"""Ground-truth item -> Haiku item pairs for the stage 1.3 offline matching test.

Usage: python3 -I make_pairs.py <repo> <out.json>
"""
import copy
import json
import sys
from pathlib import Path

REPO = Path(sys.argv[1])
sys.path.insert(0, str(REPO / "tools" / "PurchaseMemory.ReceiptBenchmarks"))
from ocr_compare.common import load_score_items  # noqa: E402

SI = load_score_items()
H08 = REPO / "test-data" / "receipts" / "household-2026-08" / "results"
GT = json.loads((H08 / "ground_truth_items.json").read_text())
RUN = H08 / "ocr-compare" / "runs" / "haiku-prod-v4-medium"
out = {"source": "score_items.score_receipt(ground_truth_items.json, haiku-prod-v4-medium)", "receipts": {}}
for rid in GT["_receipts"]:
    gt = GT[rid]
    parsed = json.loads((RUN / f"haiku-prod-v4-medium__claude-haiku-5-5__{rid}.json").read_text())["parsed"]
    res = SI.score_receipt(copy.deepcopy(gt), parsed)
    out["receipts"][rid] = {
        "groundTruth": [{"name": g["name"], "lineTotal": g["lineTotal"]} for g in gt["items"]],
        "pairs": [{"groundTruthIndex": k, "haikuIndex": i} for k, i in res["pairs"]],
    }
Path(sys.argv[2]).write_text(json.dumps(out, ensure_ascii=False, indent=1) + "\n")
print({rid: len(r["pairs"]) for rid, r in out["receipts"].items()}, sum(len(r["pairs"]) for r in out["receipts"].values()))
