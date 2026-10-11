#!/usr/bin/env python3
"""How exactly item names are copied: benchmark output vs the labelled names.

Pairs each labelled item with a predicted line by score_items.py's name similarity
(greedy, in label order) and counts, per model label:
  * exact      - same text (whitespace collapsed, a trailing VAT letter such as
                 " C" ignored; case-sensitive);
  * case-only  - differs only in letter case;
  * diacritics - same after removing diacritics and case, different before;
                 split into "added" (the model printed more diacritic letters
                 than the label, e.g. "Ogórki" -> "Ógórki") and "dropped"
                 ("MAŚLANKA" -> "MASLANKA");
  * other      - any other difference (typos, truncation, expanded abbreviations).
The labels are names as printed on the receipt.

Usage: score_names.py [--gt PATH/ground_truth_items.json] DIR [DIR ...] [--detail]
Default --gt: household-2026-08/results/ground_truth_items.json. Writes nothing.
"""
import importlib.util
import json
import pathlib
import re
import sys
import unicodedata

HERE = pathlib.Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("score_items", HERE / "score_items.py")
score_items = importlib.util.module_from_spec(spec)
spec.loader.exec_module(score_items)


def core(text):
    """Whitespace collapsed, trailing VAT letter (" C", " A") removed."""
    return re.sub(r"\s[A-G]$", "", " ".join(str(text).split()))


def plain(text):
    decomposed = unicodedata.normalize("NFD", text.replace("ł", "l").replace("Ł", "L"))
    return "".join(ch for ch in decomposed if not unicodedata.combining(ch)).lower()


def diacritic_letters(text):
    return sum(1 for ch in text if ch in "łŁ" or plain(ch) != ch.lower())


def classify(expected, got):
    e, g = core(expected), core(got)
    if e == g:
        return "exact"
    if e.lower() == g.lower():
        return "case-only"
    if plain(e) == plain(g):
        return "diacritics-added" if diacritic_letters(g) > diacritic_letters(e) else "diacritics-dropped"
    return "other"


def main(argv):
    detail = "--detail" in argv
    argv = [a for a in argv if a != "--detail"]
    gt_path = score_items.RESULTS / "ground_truth_items.json"
    if len(argv) >= 2 and argv[0] == "--gt":
        gt_path, argv = pathlib.Path(argv[1]), argv[2:]
    if not argv:
        sys.exit(__doc__)
    gt_all = json.loads(gt_path.read_text())
    receipts = gt_all["_receipts"]
    totals = {}
    for run_dir in argv:
        for path in sorted(pathlib.Path(run_dir).glob("*.json")):
            adapted = score_items.adapt(path, None)
            if adapted is None or adapted[1] not in receipts or adapted[2] is None:
                continue
            model, receipt, parsed, _ = adapted
            counts = totals.setdefault(model, {"matched": 0, "exact": 0, "case-only": 0, "diacritics-added": 0,
                                               "diacritics-dropped": 0, "other": 0})
            preds = list(enumerate(parsed["items"]))
            used = set()
            for item in gt_all[receipt]["items"]:
                candidates = [(score_items.similar(item["name"], p["name"]), i, p)
                              for i, p in preds if i not in used]
                candidates = [c for c in candidates if c[0] >= score_items.NAME_THRESHOLD]
                if not candidates:
                    continue
                _, index, pred = max(candidates, key=lambda c: c[0])
                used.add(index)
                kind = classify(item["name"], pred["name"])
                counts["matched"] += 1
                counts[kind] += 1
                if detail and kind != "exact":
                    print(f"{model:<40} {receipt} {kind:<10} {item['name']!r} -> {pred['name']!r}")
    print(f"{'model':<40} {'matched':>7} {'exact':>6} {'case':>5} {'+diacr':>7} {'-diacr':>7} {'other':>6}")
    for model, c in sorted(totals.items()):
        print(f"{model:<40} {c['matched']:>7} {c['exact']:>6} {c['case-only']:>5} "
              f"{c['diacritics-added']:>7} {c['diacritics-dropped']:>7} {c['other']:>6}")


if __name__ == "__main__":
    main(sys.argv[1:])
