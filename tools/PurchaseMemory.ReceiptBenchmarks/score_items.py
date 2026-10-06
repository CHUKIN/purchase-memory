#!/usr/bin/env python3
"""Score line items (prices) of benchmark output against ground_truth_items.json.

Ground truth: test-data/receipts/household-2026-08/results/ground_truth_items.json
(hand-labelled items, discounts and totals for a subset of receipts).

Accepted inputs (each argument is a directory; subdirectories are not searched):
  * per-receipt JSONs of the `cloud` / `openai` benchmark commands
    (`<config>__<model>__<NN>.json` with a `parsed` block, camelCase keys);
  * the legacy `results/out/NN_A.json` / `NN_B.json` files (snake_case keys),
    labelled `legacy-<A|B>` unless --label is given.

Usage: score_items.py [--label NAME] DIR [DIR ...] [--detail]
Prints a per-receipt and per-model table. Writes nothing.

Matching rules
  * names are compared after score.py's fold() (lowercase, no diacritics) with VAT
    prefixes ("C_", "A#"), trailing product codes and punctuation removed;
    a pair is a name match when difflib similarity >= NAME_THRESHOLD or one
    folded name contains the other's first 12 characters (score.py's rule);
  * a line price is correct when |lineTotal - expected| <= 0.01; for an item that
    carries a discount, the net price (gross + discount) is accepted as well;
  * a discount is recognised when the model returned a negative line of that
    amount, or returned the discounted item at its net price;
  * predicted negative lines matched to discounts are not counted as extra.
"""
import ast
import difflib
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
RESULTS = ROOT / "test-data" / "receipts" / "household-2026-08" / "results"
TOLERANCE = 0.01 + 1e-9
NAME_THRESHOLD = 0.6


def load_fold():
    tree = ast.parse((RESULTS / "score.py").read_text())
    wanted = [node for node in tree.body
              if isinstance(node, (ast.Import, ast.ImportFrom))
              or (isinstance(node, ast.FunctionDef) and node.name == "fold")]
    namespace = {}
    exec(compile(ast.Module(body=wanted, type_ignores=[]), "score.py", "exec"), namespace)
    return namespace["fold"]


fold = load_fold()


def normalize(name):
    s = fold(name)
    s = re.sub(r"^[a-g][_#]", "", s)            # VAT-group prefixes: C_, A#
    s = re.sub(r"\s\d{5,}[a-g]?$", "", s)        # trailing product code (Auchan)
    s = re.sub(r"[^\w%]+", " ", s)
    return " ".join(s.split())


def similar(a, b):
    na, nb = normalize(a), normalize(b)
    if not na or not nb:
        return 0.0
    if na[:12] in nb or nb[:12] in na:
        return 1.0
    return difflib.SequenceMatcher(None, na, nb).ratio()


def num(value):
    try:
        return None if value is None else float(value)
    except (TypeError, ValueError):
        return None


def close(a, b):
    return a is not None and b is not None and abs(a - b) <= TOLERANCE


# ---------- input adapters -------------------------------------------------

def adapt(path, label):
    """Return (model_label, receipt_id, parsed_dict_or_None, meta) or None."""
    data = json.loads(path.read_text())
    if "receiptId" in data:  # cloud / openai benchmark result
        model = f"{data.get('configuration', '?')}/{data.get('model', '?')}"
        receipt = data["receiptId"]
        meta = {"seconds": data.get("seconds"), "status": data.get("status"),
                "inputTokens": (data.get("usage") or {}).get("inputTokens"),
                "outputTokens": (data.get("usage") or {}).get("outputTokens")}
    else:  # legacy results/out/NN_X.json
        match = re.fullmatch(r"(\d{2})_([AB])\.json", path.name)
        if not match:
            return None
        receipt = match.group(1)
        model = label or f"legacy-{match.group(2)}"
        meta = {"seconds": data.get("seconds"), "status": "error" if data.get("error") else "success"}
    parsed = data.get("parsed")
    if parsed is not None:
        parsed = {"total": num(parsed.get("total")),
                  "items": [{"name": str(i.get("name", "")),
                             "lineTotal": num(i.get("lineTotal", i.get("line_total")))}
                            for i in (parsed.get("items") or []) if isinstance(i, dict)]}
    return model, receipt, parsed, meta


# ---------- scoring --------------------------------------------------------

def score_receipt(gt, parsed):
    res = {"gt_items": len(gt["items"]), "correct": 0, "wrong_price": 0, "missing": 0,
           "extra": 0, "disc_total": len(gt["discounts"]), "disc_ok": 0,
           "total_ok": False, "problems": []}
    if parsed is None:
        res["missing"] = len(gt["items"])
        res["problems"].append("no result")
        return res
    res["total_ok"] = close(parsed["total"], gt["total"])
    preds = list(enumerate(parsed["items"]))
    used = set()

    # Discounts first: a negative predicted line of the discount's amount.
    net_by_item = {}
    for d in gt["discounts"]:
        hit = next((i for i, p in preds if i not in used
                    and p["lineTotal"] is not None and p["lineTotal"] < 0
                    and close(p["lineTotal"], d["amount"])), None)
        if hit is not None:
            used.add(hit)
            d["_ok"] = True
        if d.get("appliesTo"):
            net_by_item.setdefault(d["appliesTo"], []).append(d)

    # Items: best name match, preferring a candidate with the right price.
    for g in gt["items"]:
        expected = g["lineTotal"]
        discounts = [d for d in net_by_item.get(g["name"], []) if not d.get("_claimed")]
        net = round(expected + sum(d["amount"] for d in discounts), 2) if discounts else None
        candidates = [(similar(g["name"], p["name"]), i, p) for i, p in preds if i not in used]
        candidates = [c for c in candidates if c[0] >= NAME_THRESHOLD]
        if not candidates:
            res["missing"] += 1
            res["problems"].append(f"missing: {g['name']} {expected:.2f}")
            continue

        def rank(c):
            price_ok = close(c[2]["lineTotal"], expected) or (net is not None and close(c[2]["lineTotal"], net))
            return (price_ok, c[0])

        sim, index, pred = max(candidates, key=rank)
        used.add(index)
        if close(pred["lineTotal"], expected):
            res["correct"] += 1
        elif net is not None and close(pred["lineTotal"], net):
            res["correct"] += 1
            for d in discounts:  # discount applied inside the line
                d["_ok"] = True
                d["_claimed"] = True
        else:
            res["wrong_price"] += 1
            got = "—" if pred["lineTotal"] is None else f"{pred['lineTotal']:.2f}"
            res["problems"].append(f"price: {g['name']} {expected:.2f} -> {got} ({pred['name']})")

    res["disc_ok"] = sum(1 for d in gt["discounts"] if d.get("_ok"))
    for d in gt["discounts"]:
        if not d.get("_ok"):
            res["problems"].append(f"discount missed: {d['label']} {d['amount']:.2f}")
    res["extra"] = len(preds) - len(used)
    for i, p in preds:
        if i not in used:
            got = "—" if p["lineTotal"] is None else f"{p['lineTotal']:.2f}"
            res["problems"].append(f"extra: {p['name']} {got}")
    return res


def main(argv):
    label = None
    detail = "--detail" in argv
    argv = [a for a in argv if a != "--detail"]
    if len(argv) >= 2 and argv[0] == "--label":
        label, argv = argv[1], argv[2:]
    if not argv:
        sys.exit(__doc__)
    gt_all = json.loads((RESULTS / "ground_truth_items.json").read_text())
    receipts = gt_all["_receipts"]

    rows = {}
    for run_dir in argv:
        for path in sorted(pathlib.Path(run_dir).glob("*.json")):
            adapted = adapt(path, label)
            if adapted is None or adapted[1] not in receipts:
                continue
            model, receipt, parsed, meta = adapted
            gt = json.loads(json.dumps(gt_all[receipt]))  # fresh copy: scoring marks discounts
            rows.setdefault(model, {})[receipt] = (score_receipt(gt, parsed), meta)
    if not rows:
        sys.exit("No results for labelled receipts found.")

    print(f"{'model':<40} {'rcpt':>4} {'total':>5} {'ok/gt':>7} {'price':>5} {'miss':>4} {'extra':>5} {'disc':>5} {'secs':>6}")
    summary = []
    for model in sorted(rows):
        agg = {"n": 0, "total_ok": 0, "correct": 0, "gt": 0, "wrong": 0, "miss": 0, "extra": 0,
               "disc_ok": 0, "disc": 0, "secs": [], "in": [], "out": [], "missing_receipts": []}
        for receipt in receipts:
            if receipt not in rows[model]:
                agg["missing_receipts"].append(receipt)
                continue
            r, meta = rows[model][receipt]
            agg["n"] += 1
            agg["total_ok"] += r["total_ok"]
            agg["correct"] += r["correct"]
            agg["gt"] += r["gt_items"]
            agg["wrong"] += r["wrong_price"]
            agg["miss"] += r["missing"]
            agg["extra"] += r["extra"]
            agg["disc_ok"] += r["disc_ok"]
            agg["disc"] += r["disc_total"]
            for key, field in (("secs", "seconds"), ("in", "inputTokens"), ("out", "outputTokens")):
                if meta.get(field) is not None:
                    agg[key].append(meta[field])
            secs = "—" if meta.get("seconds") is None else f"{meta['seconds']:.1f}"
            print(f"{model:<40} {receipt:>4} {'yes' if r['total_ok'] else 'NO':>5} "
                  f"{r['correct']:>3}/{r['gt_items']:<3} {r['wrong_price']:>5} {r['missing']:>4} "
                  f"{r['extra']:>5} {r['disc_ok']:>2}/{r['disc_total']:<2} {secs:>6}")
            if detail:
                for problem in r["problems"]:
                    print(f"{'':<46}{problem}")
        summary.append((model, agg))

    print()
    print(f"{'model':<40} {'n':>2} {'total':>6} {'lines':>12} {'price':>5} {'miss':>4} {'extra':>5} {'disc':>6} {'secs':>6} {'in tok':>8} {'out tok':>8}")
    for model, a in summary:
        mean = lambda xs: sum(xs) / len(xs) if xs else None
        acc = 100 * a["correct"] / a["gt"] if a["gt"] else 0
        fmt = lambda v, f: "—" if v is None else format(v, f)
        print(f"{model:<40} {a['n']:>2} {a['total_ok']:>2}/{a['n']:<3} "
              f"{a['correct']:>3}/{a['gt']:<3} {acc:>4.0f}% {a['wrong']:>5} {a['miss']:>4} {a['extra']:>5} "
              f"{a['disc_ok']:>2}/{a['disc']:<3} {fmt(mean(a['secs']), '6.1f')} "
              f"{fmt(mean(a['in']), '8.0f')} {fmt(mean(a['out']), '8.0f')}"
              + (f"  (no result: {','.join(a['missing_receipts'])})" if a["missing_receipts"] else ""))


if __name__ == "__main__":
    main(sys.argv[1:])
