"""Scores every run folder and writes report.json + report.md."""
import copy
import statistics
import time

from . import azure_receipt, boxes, hybrid
from .common import RESULTS, load_score_items, read_json, write_json


def reference_words(out_dir, receipt_id, tesseract_reference):
    """{"azure": words or None, "tesseract": words or None} for the box check."""
    refs = {"azure": None, "tesseract": None}
    azure_path = out_dir / "cache" / "azure" / f"receipt_{receipt_id}.json"
    if azure_path.exists():
        refs["azure"] = azure_receipt.words(read_json(azure_path)["response"])
    tess_path = out_dir / "cache" / "tesseract" / tesseract_reference / f"receipt_{receipt_id}.json"
    if tess_path.exists():
        refs["tesseract"] = read_json(tess_path)["words"]
    return refs


def _close(a, b):
    return a is not None and b is not None and abs(float(a) - float(b)) <= 0.01 + 1e-9


def box_verdict(gt_items, gt_index, box, check, words, normalize):
    """"right" / "ambiguous" / "other-line" / "miss" for the plausible box of the line
    matched to gt_items[gt_index], using the reference words of the box check.

    right       the recognised name is printed in the box (nameHit), or its price
                is and no other labelled line's name is;
    ambiguous   only the price matches and another labelled line's name is in the
                box too: the box covers the right row plus a neighbour, or sits on
                a neighbour that happens to share the price;
    other-line  neither the name nor the price is in the box, but another labelled
                line of the same receipt is (fuzzy name >= NAME_MATCH or its price):
                the highlight lands on the wrong row;
    miss        nothing labelled is in the box (header, totals, blank space), or
                the reference layer lacks the text.
    """
    if not words:
        return "right" if check["hit"] else "miss"
    if check["nameHit"]:
        return "right"
    text = boxes.text_in_box(box, words)
    others = [g for k, g in enumerate(gt_items) if k != gt_index]
    other_name = any(boxes.partial_similarity(g["name"], text, normalize) >= boxes.NAME_MATCH for g in others)
    if check["priceHit"]:
        return "ambiguous" if other_name else "right"
    if other_name or any(boxes.price_printed(g["lineTotal"], text) for g in others):
        return "other-line"
    return "miss"


def score_configuration(records, out_dir, tesseract_reference, ground_truth, items_truth, own_layer=None):
    """Aggregate metrics for one configuration ({receipt id: record}).

    own_layer ("azure"/"tesseract") names the word layer the provider itself
    produced; the GT box metric then uses the other layer so it is not
    self-referential.
    """
    si = load_score_items()
    labelled = items_truth["_receipts"]
    agg = {"receipts": len(records), "success": 0, "totalOk": 0, "totalChecked": 0, "sumOk": 0,
           "items": 0, "boxPresent": 0, "boxPlausible": 0, "hitAzure": 0, "hitTesseract": 0,
           "itemsWithAzureRef": 0, "itemsWithTessRef": 0,
           "labelledReceipts": 0, "gtItems": 0, "gtCorrect": 0, "gtWrongPrice": 0, "gtMissing": 0,
           "gtExtra": 0, "gtDiscounts": 0, "gtDiscountsOk": 0, "gtBoxHit": 0,
           "gtBoxPlausible": 0, "gtBoxWrong": 0, "gtBoxOtherLine": 0, "gtBoxAmbiguous": 0, "gtBoxHitTess": 0,
           "discLines": 0, "discBoxPlausible": 0, "discBoxHit": 0,
           "seconds": [], "costUsd": [], "paidUsd": []}
    for receipt_id, record in sorted(records.items()):
        parsed = record.get("parsed")
        if record.get("seconds") is not None:
            agg["seconds"].append(record["seconds"])
        agg["costUsd"].append(record.get("costUsd") or 0.0)
        if record.get("paidEquivalentUsd") is not None:
            agg["paidUsd"].append(record["paidEquivalentUsd"])
        if receipt_id in ground_truth:
            agg["totalChecked"] += 1
        if not parsed:
            if receipt_id in labelled:
                gt = items_truth[receipt_id]
                agg["labelledReceipts"] += 1
                agg["gtItems"] += len(gt["items"])
                agg["gtMissing"] += len(gt["items"])
                agg["gtDiscounts"] += len(gt["discounts"])
            continue
        agg["success"] += 1
        if receipt_id in ground_truth and _close(parsed.get("total"), ground_truth[receipt_id]["total"]):
            agg["totalOk"] += 1
        line_totals = [i.get("lineTotal") for i in parsed["items"]]
        if parsed["items"] and None not in line_totals and _close(sum(line_totals), parsed.get("total")):
            agg["sumOk"] += 1  # the app's own consistency check: lines (discounts negative) = total

        refs = reference_words(out_dir, receipt_id, tesseract_reference)
        checks, tess_checks, check_words = [], [], []
        for item in parsed["items"]:
            azure_check = boxes.check_box(item, refs["azure"], si.normalize)
            tess_check = boxes.check_box(item, refs["tesseract"], si.normalize)
            use_azure = refs["azure"] is not None and (own_layer != "azure" or refs["tesseract"] is None)
            if own_layer == "tesseract" and refs["azure"] is not None:
                use_azure = True
            checks.append(azure_check if use_azure else tess_check)
            tess_checks.append(tess_check)
            check_words.append(refs["azure"] if use_azure else refs["tesseract"])
            if item.get("lineTotal") is not None and float(item["lineTotal"]) < 0:
                agg["discLines"] += 1
                agg["discBoxPlausible"] += checks[-1]["plausible"]
                agg["discBoxHit"] += checks[-1]["hit"]
            agg["items"] += 1
            agg["boxPresent"] += azure_check["present"]
            agg["boxPlausible"] += azure_check["plausible"]
            if refs["azure"] is not None:
                agg["itemsWithAzureRef"] += 1
                agg["hitAzure"] += azure_check["hit"]
            if refs["tesseract"] is not None:
                agg["itemsWithTessRef"] += 1
                agg["hitTesseract"] += tess_check["hit"]

        if receipt_id in labelled:
            gt = copy.deepcopy(items_truth[receipt_id])
            scored = si.score_receipt(gt, {"total": si.num(parsed.get("total")),
                                           "items": [{"name": str(i.get("name") or ""),
                                                      "lineTotal": si.num(i.get("lineTotal"))}
                                                     for i in parsed["items"]]})
            agg["labelledReceipts"] += 1
            agg["gtItems"] += scored["gt_items"]
            agg["gtCorrect"] += scored["correct"]
            agg["gtWrongPrice"] += scored["wrong_price"]
            agg["gtMissing"] += scored["missing"]
            agg["gtExtra"] += scored["extra"]
            agg["gtDiscounts"] += scored["disc_total"]
            agg["gtDiscountsOk"] += scored["disc_ok"]
            agg["gtBoxHit"] += sum(1 for _, pred_index in scored["pairs"] if checks[pred_index]["hit"])
            agg["gtBoxHitTess"] += sum(1 for _, pred_index in scored["pairs"] if tess_checks[pred_index]["hit"])
            for gt_index, pred_index in scored["pairs"]:
                if not checks[pred_index]["plausible"]:
                    continue
                agg["gtBoxPlausible"] += 1
                verdict = box_verdict(gt["items"], gt_index, parsed["items"][pred_index]["box"],
                                      checks[pred_index], check_words[pred_index], si.normalize)
                agg["gtBoxWrong"] += verdict in ("other-line", "miss")
                agg["gtBoxAmbiguous"] += verdict == "ambiguous"
                agg["gtBoxOtherLine"] += verdict == "other-line"

    pct = lambda a, b: None if not b else round(100.0 * a / b, 1)
    mean = lambda xs: None if not xs else round(statistics.mean(xs), 6)
    return {
        "receipts": agg["receipts"], "success": agg["success"],
        "totalOk": agg["totalOk"], "totalChecked": agg["totalChecked"], "sumOk": agg["sumOk"],
        "labelledReceipts": agg["labelledReceipts"],
        "gtItems": agg["gtItems"], "gtCorrect": agg["gtCorrect"], "itemAccuracyPct": pct(agg["gtCorrect"], agg["gtItems"]),
        "gtWrongPrice": agg["gtWrongPrice"], "gtMissing": agg["gtMissing"], "gtExtra": agg["gtExtra"],
        "gtDiscounts": agg["gtDiscounts"], "gtDiscountsOk": agg["gtDiscountsOk"],
        "gtBoxHit": agg["gtBoxHit"], "gtBoxHitPct": pct(agg["gtBoxHit"], agg["gtItems"]),
        "gtBoxPlausible": agg["gtBoxPlausible"], "gtBoxWrong": agg["gtBoxWrong"],
        "gtBoxOtherLine": agg["gtBoxOtherLine"], "gtBoxAmbiguous": agg["gtBoxAmbiguous"], "gtBoxHitTess": agg["gtBoxHitTess"],
        "discLines": agg["discLines"], "discBoxPlausible": agg["discBoxPlausible"], "discBoxHit": agg["discBoxHit"],
        "recognisedItems": agg["items"],
        "boxPresentPct": pct(agg["boxPresent"], agg["items"]),
        "boxPlausiblePct": pct(agg["boxPlausible"], agg["items"]),
        "boxHitAzurePct": pct(agg["hitAzure"], agg["itemsWithAzureRef"]),
        "boxHitTesseractPct": pct(agg["hitTesseract"], agg["itemsWithTessRef"]),
        "meanSeconds": None if not agg["seconds"] else round(statistics.mean(agg["seconds"]), 2),
        "medianSeconds": None if not agg["seconds"] else round(statistics.median(agg["seconds"]), 2),
        "costUsdPerReceipt": mean(agg["costUsd"]),
        "paidEquivalentUsdPerReceipt": mean(agg["paidUsd"]),
    }


def _hybrid_order(configuration):
    """Hybrids grouped by strategy (hybrid.STRATEGIES order), calibration rows last."""
    matches = [s for s in hybrid.STRATEGIES if configuration.startswith(f"hybrid-{s}-")]
    strategy = max(matches, key=len) if matches else None
    return (strategy is None, hybrid.STRATEGIES.index(strategy) if strategy else 0, configuration)


def build(out_dir, runs, notes, matrix, tesseract_reference, hybrid_runs=None):
    ground_truth = {k: v for k, v in read_json(RESULTS / "ground_truth.json").items() if not k.startswith("_")}
    items_truth = read_json(RESULTS / "ground_truth_items.json")
    rows = []
    for configuration, records in sorted(runs.items()):
        own_layer = "azure" if configuration.startswith("azure") else (
            "tesseract" if configuration.startswith("tesseract") else None)
        metrics = score_configuration(records, out_dir, tesseract_reference, ground_truth, items_truth, own_layer)
        rows.append({"configuration": configuration, "note": notes.get(configuration, ""), **metrics})
    hybrid_rows = []
    for configuration, records in sorted((hybrid_runs or {}).items(), key=lambda kv: _hybrid_order(kv[0])):
        metrics = score_configuration(records, out_dir, tesseract_reference, ground_truth, items_truth)
        hybrid_rows.append({"configuration": configuration, **metrics})
    azure_ledger_path = out_dir / "cache" / "azure" / "_calls.json"
    report = {
        "generatedAt": time.strftime("%Y-%m-%dT%H:%M:%S%z"),
        "boxHitDefinition": boxes.__doc__.strip(),
        "tesseractReference": tesseract_reference,
        "azureCalls": read_json(azure_ledger_path) if azure_ledger_path.exists() else None,
        "configurations": rows,
        "hybridDefinition": hybrid.__doc__.strip(),
        "boxVerdictDefinition": box_verdict.__doc__.strip(),
        "hybrids": hybrid_rows,
        "haikuMatrixPlan": matrix,
    }
    write_json(out_dir / "report.json", report)
    (out_dir / "report.md").write_text(markdown(report))
    return report


def _fmt(value, suffix=""):
    return "—" if value is None else f"{value}{suffix}"


def markdown(report):
    lines = ["# OCR comparison: Azure prebuilt-receipt vs Tesseract vs Haiku", "",
             f"Generated {report['generatedAt']}. Dataset: household-2026-08 (20 photos; item labels for 8).", "",
             "| configuration | ok/receipts | total ok | lines sum = total | items ok (8 labelled) | disc | extra | box present | box plausible | box hit (Azure words) | box hit (Tesseract words) | GT lines with hit box | s/receipt (median) | $/receipt | paid-equivalent $/receipt |",
             "|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|"]
    for r in report["configurations"]:
        lines.append(
            f"| {r['configuration']} | {r['success']}/{r['receipts']} | {r['totalOk']}/{r['totalChecked']} | {r['sumOk']}/{r['success']} "
            f"| {r['gtCorrect']}/{r['gtItems']} ({_fmt(r['itemAccuracyPct'], '%')}) "
            f"| {r['gtDiscountsOk']}/{r['gtDiscounts']} | {r['gtExtra']} "
            f"| {_fmt(r['boxPresentPct'], '%')} | {_fmt(r['boxPlausiblePct'], '%')} "
            f"| {_fmt(r['boxHitAzurePct'], '%')} | {_fmt(r['boxHitTesseractPct'], '%')} "
            f"| {r['gtBoxHit']}/{r['gtItems']} ({_fmt(r['gtBoxHitPct'], '%')}) "
            f"| {_fmt(r['meanSeconds'])} ({_fmt(r['medianSeconds'])}) "
            f"| {_fmt(r['costUsdPerReceipt'])} | {_fmt(r['paidEquivalentUsdPerReceipt'])} |")
    lines += ["", "Notes:",
              "- Box hit is self-referential for a provider checked against its own word layer "
              "(Azure vs Azure words, Tesseract vs Tesseract words); read the other column for those rows.",
              "- \"GT lines with hit box\" uses Azure words as the reference, except for the Azure row, which is "
              "checked against Tesseract (eng) words to stay independent (a pessimistic bound: Tesseract misreads a lot)."]
    lines += [f"- `{r['configuration']}`: {r['note']}" for r in report["configurations"] if r["note"]]
    if report["azureCalls"]:
        lines.append(f"- Azure calls in the ledger (all runs): {report['azureCalls']}")
    if report.get("hybrids"):
        lines += hybrid_markdown(report)
    lines += ["", "## Box hit definition", "", "```", report["boxHitDefinition"], "```", "",
              "## Haiku matrix cost estimate (actual spend: haiku_ledger.json)", "",
              "| variant | input tok | output tok (est.) | $/receipt (est.) | calls | $ total (est.) | $ worst case |",
              "|---|---:|---:|---:|---:|---:|---:|"]
    total = worst = calls = 0
    for m in report["haikuMatrixPlan"]:
        lines.append(f"| {m['variant']} | {m['inputTokens']} | {m['outputTokens']} | {m['usdPerReceipt']:.5f} "
                     f"| {m['calls']} | {m['estimatedUsd']:.4f} | {m['worstCaseUsd']:.4f} |")
        total += m["estimatedUsd"]
        worst += m["worstCaseUsd"]
        calls += m["calls"]
    lines.append(f"| **all** | | | | {calls} | {total:.4f} | {worst:.4f} |")
    return "\n".join(lines) + "\n"


def hybrid_markdown(report):
    """Hybrid section: Haiku items + Azure boxes, next to plain Haiku and plain Azure."""
    baselines = [r for r in report["configurations"]
                 if r["configuration"].startswith(("haiku-prod-v3-saved", "azure"))]
    lines = ["", "## Hybrid: Haiku items + Azure boxes (free, from cache)", "",
             "Items, totals and discounts are Haiku's (so those columns equal the Haiku run); "
             "only boxes change. Box columns are over the 110 labelled lines (8 receipts), matched "
             "to recognised lines by `score_items.pairs`; the reference word layer is Azure's "
             "(Tesseract for the plain Azure row, as above, so its right/wrong mostly reflect Tesseract misreads). "
             "right / ambiguous / wrong: see the box verdict definition below; wrong = other line + miss; "
             "\"of which other line\" = wrong boxes that land on another labelled line. "
             "\"hit (Tess.)\" re-checks the same boxes against Tesseract eng words (independent of Azure, "
             "but Tesseract misreads Polish text, so it is a lower bound). "
             "Discount rows = recognised negative lines on all 20 receipts with a hit box.", "",
             "| configuration | items ok | total ok | disc | GT lines with box | right | ambiguous | wrong | of which other line | hit (Tess.) | box present (all 20) | box hit, Azure words (all 20) | discount rows with hit box |",
             "|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|"]
    for r in baselines + report["hybrids"]:
        right = r["gtBoxPlausible"] - r["gtBoxWrong"] - r["gtBoxAmbiguous"]
        lines.append(
            f"| {r['configuration']} | {r['gtCorrect']}/{r['gtItems']} | {r['totalOk']}/{r['totalChecked']} "
            f"| {r['gtDiscountsOk']}/{r['gtDiscounts']} | {r['gtBoxPlausible']}/{r['gtItems']} "
            f"| {right}/{r['gtItems']} | {r['gtBoxAmbiguous']} | {r['gtBoxWrong']} | {r['gtBoxOtherLine']} "
            f"| {r['gtBoxHitTess']}/{r['gtItems']} | {_fmt(r['boxPresentPct'], '%')} | {_fmt(r['boxHitAzurePct'], '%')} "
            f"| {r['discBoxHit']}/{r['discLines']} |")
    lines += ["", "Calibration rows take the `hybrid-aligned-medium-r1` boxes and give every line the box of "
              "the line 1 (or 3) positions later on the same receipt, so every box is wrong by construction. "
              "Their \"right\" count is how many wrong boxes the check misses: neighbouring rows are close, "
              "Azure item boxes on Polish receipts span the name row and the price row, and the check widens "
              "boxes by 25% of their height, so an off-by-one box is caught only about half the time; "
              "a box several rows away is almost always caught.",
              "", "Strategies:", "", "```", report["hybridDefinition"], "```", "",
              "Box verdict (per labelled line):", "", "```", report["boxVerdictDefinition"], "```"]
    return lines
