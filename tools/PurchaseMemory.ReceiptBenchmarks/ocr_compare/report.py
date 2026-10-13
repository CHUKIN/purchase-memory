"""Scores every run folder and writes report.json + report.md."""
import copy
import statistics
import time

from . import azure_receipt, boxes
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
        checks = []
        for item in parsed["items"]:
            azure_check = boxes.check_box(item, refs["azure"], si.normalize)
            tess_check = boxes.check_box(item, refs["tesseract"], si.normalize)
            use_azure = refs["azure"] is not None and (own_layer != "azure" or refs["tesseract"] is None)
            if own_layer == "tesseract" and refs["azure"] is not None:
                use_azure = True
            checks.append(azure_check if use_azure else tess_check)
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


def build(out_dir, runs, notes, matrix, tesseract_reference):
    ground_truth = {k: v for k, v in read_json(RESULTS / "ground_truth.json").items() if not k.startswith("_")}
    items_truth = read_json(RESULTS / "ground_truth_items.json")
    rows = []
    for configuration, records in sorted(runs.items()):
        own_layer = "azure" if configuration.startswith("azure") else (
            "tesseract" if configuration.startswith("tesseract") else None)
        metrics = score_configuration(records, out_dir, tesseract_reference, ground_truth, items_truth, own_layer)
        rows.append({"configuration": configuration, "note": notes.get(configuration, ""), **metrics})
    azure_ledger_path = out_dir / "cache" / "azure" / "_calls.json"
    report = {
        "generatedAt": time.strftime("%Y-%m-%dT%H:%M:%S%z"),
        "boxHitDefinition": boxes.__doc__.strip(),
        "tesseractReference": tesseract_reference,
        "azureCalls": read_json(azure_ledger_path) if azure_ledger_path.exists() else None,
        "configurations": rows,
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
    lines += ["", "## Box hit definition", "", "```", report["boxHitDefinition"], "```", "",
              "## Haiku matrix (planned, not run)", "",
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
