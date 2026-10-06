#!/usr/bin/env python3
"""score_items.py against this folder's ground_truth_items.json (household-2026-10).

score_items.py reads the ground truth from a fixed path (household-2026-08); this
wrapper reuses it unchanged and only points it at the 2026-10 labels.
Usage: score_items_2026_10.py [--label NAME] DIR [DIR ...] [--detail]
"""
import importlib.util
import pathlib
import sys

HERE = pathlib.Path(__file__).resolve().parent
SCORER = HERE.parents[2] / "tools" / "PurchaseMemory.ReceiptBenchmarks" / "score_items.py"

spec = importlib.util.spec_from_file_location("score_items", SCORER)
score_items = importlib.util.module_from_spec(spec)
spec.loader.exec_module(score_items)   # loads fold() from household-2026-08/results/score.py
score_items.RESULTS = HERE              # main() reads RESULTS / "ground_truth_items.json"

if __name__ == "__main__":
    score_items.main(sys.argv[1:])
