#!/usr/bin/env python3
"""Score `cloud` benchmark output against the household ground truth.

Reuses the matching rules of test-data/receipts/household-2026-08/results/score.py
without modifying or executing it as a script: only its function definitions
(fold, diacritic_score) are loaded, so the two scorers cannot drift apart.

Usage: score_cloud.py RUN_DIR [RUN_DIR ...]
Prints one row per (configuration, model) and per receipt. Writes nothing.
"""
import ast
import json
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
RESULTS = ROOT / "test-data" / "receipts" / "household-2026-08" / "results"
GT = json.loads((RESULTS / "ground_truth.json").read_text())


def load_score_functions():
    tree = ast.parse((RESULTS / "score.py").read_text())
    wanted = [node for node in tree.body
              if isinstance(node, (ast.Import, ast.ImportFrom))
              or (isinstance(node, ast.FunctionDef) and node.name in ("fold", "diacritic_score"))]
    namespace = {}
    exec(compile(ast.Module(body=wanted, type_ignores=[]), "score.py", "exec"), namespace)
    return namespace["diacritic_score"]


diacritic_score = load_score_functions()


def score(result):
    g = GT[result["receiptId"]]
    p = result.get("parsed") or {}
    items = p.get("items") or []
    names = [str(i.get("name", "")) for i in items if isinstance(i, dict)]
    total = p.get("total")
    try:
        ok_total = total is not None and abs(float(total) - g["total"]) < 0.005  # same rule as score.py
    except (TypeError, ValueError):
        ok_total = False
    matched, correct, _ = diacritic_score(g["diacritic_words"], names)
    return {"ok_total": ok_total, "items": len(items), "gt_items": g["items"],
            "items_ok": len(items) == g["items"], "dia_m": matched, "dia_c": correct}


def main(run_dirs):
    results = []
    for run_dir in run_dirs:
        for path in sorted(pathlib.Path(run_dir).glob("*__*__*.json")):
            results.append(json.loads(path.read_text()))
    if not results:
        sys.exit("No per-receipt cloud results found.")

    print(f"{'config/model':<42} {'rcpt':>4} {'status':<14} {'total':>5} {'items':>9} {'dia':>7} {'secs':>6} {'bytes':>9} input")
    groups = {}
    for r in sorted(results, key=lambda r: (r["configuration"], r["model"], r["receiptId"])):
        key = f"{r['configuration']}/{r['model']}"
        s = score(r) if r["status"] == "success" else None
        groups.setdefault(key, []).append((r, s))
        inp = r.get("input") or {}
        print(f"{key[:42]:<42} {r['receiptId']:>4} {r['status'][:14]:<14} "
              f"{('Y' if s['ok_total'] else 'n') if s else '-':>5} "
              f"{(str(s['items']) + '/' + str(s['gt_items'])) if s else '-':>9} "
              f"{(str(s['dia_c']) + '/' + str(s['dia_m'])) if s else '-':>7} "
              f"{r['seconds']:>6.1f} {r['requestBytes']:>9} {inp.get('kind', '-')}:{(inp.get('sha256') or '-')[:12]}")

    print()
    print(f"{'config/model':<42} {'n':>3} {'ok':>3} {'total ok':>8} {'items ok':>8} {'diacritics':>11} {'mean s':>7} {'mean bytes':>10}")
    for key, rows in groups.items():
        ok = [(r, s) for r, s in rows if s]
        dm = sum(s["dia_m"] for _, s in ok)
        dc = sum(s["dia_c"] for _, s in ok)
        mean_s = sum(r["seconds"] for r, _ in ok) / len(ok) if ok else 0
        mean_b = sum(r["requestBytes"] for r, _ in ok) / len(ok) if ok else 0
        print(f"{key[:42]:<42} {len(rows):>3} {len(ok):>3} "
              f"{sum(s['ok_total'] for _, s in ok):>8} {sum(s['items_ok'] for _, s in ok):>8} "
              f"{f'{dc}/{dm}':>11} {mean_s:>7.1f} {mean_b:>10.0f}")

    # Variant B is only a fair comparison if every model parsed the same rows.
    by_receipt = {}
    for r in results:
        if r.get("input"):
            by_receipt.setdefault((r["configuration"], r["receiptId"]), set()).add(r["input"]["sha256"])
    mismatched = [k for k, v in by_receipt.items() if len(v) > 1]
    print()
    print("Input identical across models: " + ("yes" if not mismatched else f"NO for {mismatched}"))


if __name__ == "__main__":
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    main(sys.argv[1:])
