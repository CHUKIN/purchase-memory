#!/usr/bin/env python3
"""Score approach A / B output against the manual ground truth."""
import json
import pathlib
import unicodedata

HERE = pathlib.Path(__file__).resolve().parent
OUT = HERE / "out"
GT = json.loads((HERE / "ground_truth.json").read_text())


def fold(s):
    """Lowercase + strip diacritics, for diacritic-insensitive matching."""
    s = unicodedata.normalize("NFD", str(s).lower())
    return "".join(c for c in s if unicodedata.category(c) != "Mn")


def load(nn, ap):
    p = OUT / f"{nn}_{ap}.json"
    if not p.exists():
        return None
    return json.loads(p.read_text())


def diacritic_score(gt_words, names):
    """For each ground-truth word that carries diacritics, find the matching output
    name (diacritic-insensitively) and check whether the diacritics survived.
    Returns (matched, correct, examples_of_loss)."""
    matched = correct = 0
    losses = []
    for w in gt_words:
        fw = fold(w)
        # accept a hit if a decent chunk of the folded gt word occurs in a folded name
        key = fw[:12]
        hit = next((n for n in names if key and key in fold(n)), None)
        if hit is None:
            continue
        matched += 1
        # compare the diacritic-bearing characters present in gt vs output
        gt_marks = {c for c in unicodedata.normalize("NFD", w) if unicodedata.category(c) == "Mn"}
        gt_special = set(c for c in w.lower() if c in "łż")
        out_marks = {c for c in unicodedata.normalize("NFD", hit) if unicodedata.category(c) == "Mn"}
        out_special = set(c for c in hit.lower() if c in "łż")
        if gt_marks <= out_marks and gt_special <= out_special:
            correct += 1
        else:
            losses.append(f"{w!r}->{hit!r}")
    return matched, correct, losses


rows = []
for nn in sorted(GT):
    if nn.startswith("_"):
        continue
    g = GT[nn]
    row = {"nn": nn, "merchant": g["merchant"], "quality": g["print_quality"],
           "gt_total": g["total"], "gt_items": g["items"]}
    for ap in ("A", "B"):
        d = load(nn, ap)
        p = (d or {}).get("parsed") or {}
        items = p.get("items") or []
        names = [str(i.get("name", "")) for i in items if isinstance(i, dict)]
        total = p.get("total")
        try:
            ok_total = total is not None and abs(float(total) - g["total"]) < 0.005
        except (TypeError, ValueError):
            ok_total = False
        m, c, losses = diacritic_score(g["diacritic_words"], names)
        row[ap] = {
            "total": total, "ok_total": ok_total, "items": len(items),
            "secs": (d or {}).get("seconds"), "error": (d or {}).get("error"),
            "dia_matched": m, "dia_correct": c, "dia_losses": losses,
            "merchant": p.get("merchant"), "date": p.get("date"), "names": names,
        }
    rows.append(row)

(OUT / "scored.json").write_text(json.dumps(rows, ensure_ascii=False, indent=2))

print(f"{'##':>3} {'merchant':<28} {'qual':<7} {'total':>9} | A tot  A#it  A dia | B tot  B#it  B dia")
agg = {"A": [0, 0, 0, 0, 0.0], "B": [0, 0, 0, 0, 0.0]}  # tot_ok, items_ok, dia_m, dia_c, secs
for r in rows:
    line = f"{r['nn']:>3} {r['merchant'][:28]:<28} {r['quality']:<7} {r['gt_total']:>9.2f} |"
    for ap in ("A", "B"):
        a = r[ap]
        line += f" {'Y' if a['ok_total'] else 'n'}  {a['items']:>3}/{r['gt_items']:<3} {a['dia_correct']}/{a['dia_matched']:<3}|"
        agg[ap][0] += int(a["ok_total"])
        agg[ap][1] += int(a["items"] == r["gt_items"])
        agg[ap][2] += a["dia_matched"]
        agg[ap][3] += a["dia_correct"]
        agg[ap][4] += a["secs"] or 0
    print(line)

n = len(rows)
print()
for ap in ("A", "B"):
    t, i, dm, dc, s = agg[ap]
    print(f"{ap}: total ok {t}/{n} | exact item count {i}/{n} | diacritics {dc}/{dm} "
          f"({100*dc/dm if dm else 0:.0f}%) | mean {s/n:.0f}s")
