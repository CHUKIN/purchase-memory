"""Command line: tesseract | azure | haiku | report. See README.md beside this file."""
import argparse
import pathlib
import sys

from . import azure_receipt, haiku_matrix, report, tesseract_ocr
from .common import DATASET, DEFAULT_OUT, image_path, load_runs, load_score_items, read_json, \
    receipt_ids, result_record, write_json, write_run

TESSERACT = "/opt/homebrew/bin/tesseract"
IMAGE_SIZE = (768, 1024)  # only used if Tesseract's TSV lacks a page row


def tesseract_label(args):
    return f"tesseract-{args.lang.replace('+', '-')}-psm{args.psm}" + (f"-x{args.scale}" if args.scale != 1 else "")


def cmd_tesseract(args):
    langs = tesseract_ocr.installed_languages(args.tesseract, args.tessdata)
    missing = [l for l in args.lang.split("+") if l not in langs]
    if missing:
        raise SystemExit(f"Tesseract language(s) not installed: {', '.join(missing)} (installed: {', '.join(langs)})")
    note = f"langs {args.lang}; installed {','.join(langs)}" + ("; pol MISSING" if "pol" not in langs else "")
    label = tesseract_label(args)
    fold = load_score_items().fold
    for receipt in receipt_ids(args.images_dir, args.receipts):
        tsv, seconds = tesseract_ocr.run_tsv(args.tesseract, image_path(args.images_dir, receipt),
                                             args.lang, args.psm, args.scale, args.tessdata)
        words, lines = tesseract_ocr.parse_tsv(tsv, *IMAGE_SIZE)
        write_json(args.out / "cache" / "tesseract" / label / f"receipt_{receipt}.json",
                   {"seconds": round(seconds, 2), "note": note, "words": words, "lines": lines})
        parsed = tesseract_ocr.parse_receipt(lines, fold)
        write_run(args.out, result_record(receipt, label, "tesseract-5", seconds, parsed,
                                          extra={"source": note}))
        print(f"{label} {receipt}: {len(words)} words, {len(lines)} lines, {len(parsed['items'])} items, "
              f"total {parsed['total']}, {seconds:.1f}s", flush=True)


def cmd_azure(args):
    client = azure_receipt.AzureClient(args.out / "cache" / "azure", args.max_calls, args.locale)
    label = "azure-prebuilt-receipt"
    for receipt in receipt_ids(args.images_dir, args.receipts):
        try:
            entry, fresh = client.cached_or_analyze(
                receipt, image_path(args.images_dir, receipt).read_bytes(), allow_calls=not args.offline)
        except azure_receipt.AzureError as error:
            print(f"{receipt}: ERROR {error}", flush=True)
            write_run(args.out, result_record(receipt, label, azure_receipt.API_VERSION, None, None,
                                              status="error", error=str(error)))
            if "cap reached" in str(error):
                break
            continue
        if entry is None:
            print(f"{receipt}: not cached (offline)")
            continue
        parsed = azure_receipt.to_parsed(entry["response"])
        write_run(args.out, result_record(
            receipt, label, azure_receipt.API_VERSION, entry["meta"]["seconds"], parsed,
            cost_usd=0.0, paid_equivalent_usd=azure_receipt.PAID_USD_PER_PAGE,
            extra={"source": "cache" if not fresh else "fresh call", "locale": entry["meta"].get("locale")}))
        boxed = sum(1 for i in parsed["items"] if i["box"])
        print(f"{receipt}: {'called' if fresh else 'cached'} {entry['meta']['seconds']}s, "
              f"{len(parsed['items'])} items ({boxed} boxed), total {parsed['total']} | ledger {client.ledger()}",
              flush=True)


def _selected_variants(spec):
    all_variants = haiku_matrix.variants()
    if spec in (None, "all"):
        return all_variants
    wanted = set(spec.split(","))
    chosen = [v for v in all_variants if haiku_matrix.variant_id(*v) in wanted]
    unknown = wanted - {haiku_matrix.variant_id(*v) for v in chosen}
    if unknown:
        raise SystemExit(f"unknown variants: {', '.join(sorted(unknown))}")
    return chosen


def cmd_haiku(args):
    receipts = receipt_ids(args.images_dir, args.receipts)
    selected = _selected_variants(args.variants)
    selected_ids = {haiku_matrix.variant_id(*v) for v in selected}
    plan = [row for row in haiku_matrix.estimate(receipts, args.repeats) if row["variant"] in selected_ids]
    print(f"Haiku matrix: {len(selected)} variants x {len(receipts)} receipts x {args.repeats} repeat(s) "
          f"= {len(selected) * len(receipts) * args.repeats} calls")
    for row in plan:
        print(f"  {row['variant']:<34} ~{row['inputTokens']} in / ~{row['outputTokens']} out tok, "
              f"${row['usdPerReceipt']:.5f}/receipt, ${row['estimatedUsd']:.4f} est, ${row['worstCaseUsd']:.4f} worst")
    print(f"  TOTAL estimated ${sum(r['estimatedUsd'] for r in plan):.4f}, "
          f"worst case ${sum(r['worstCaseUsd'] for r in plan):.4f}")
    if not args.run_haiku:
        print("Dry run: no API call made. Add --run-haiku --budget-usd X to spend up to X.")
        return
    spent = haiku_matrix.run(args.images_dir, args.out, receipts, selected, args.repeats, args.budget_usd)
    print(f"Spent this run: ${spent:.4f}")


def cmd_report(args):
    runs = load_runs(args.out)
    notes = {}
    for configuration, records in runs.items():
        first = next(iter(records.values()))
        notes[configuration] = first.get("source", "") if configuration.startswith("tesseract") else ""
    notes["azure-prebuilt-receipt"] = (f"api-version {azure_receipt.API_VERSION}, F0 (free); paid-equivalent "
                                       f"= S0 list ${azure_receipt.PAID_USD_PER_PAGE * 1000:.0f}/1000 pages")
    if not args.no_saved_haiku:
        for record in haiku_matrix.saved_runs():
            runs.setdefault(record["configuration"], {})[record["receiptId"]] = record
            notes[record["configuration"]] = ("production prompt v3, " + record["source"]
                                              + "; outputs saved 2026-10-11, no new call")
    receipts = receipt_ids(args.images_dir) if args.images_dir.exists() else [f"{n:02d}" for n in range(1, 21)]
    matrix = haiku_matrix.estimate(receipts, args.repeats)
    result = report.build(args.out, runs, notes, matrix, args.tesseract_reference)
    print((args.out / "report.md").read_text())
    return result


def main(argv=None):
    parser = argparse.ArgumentParser(prog="run_ocr_compare.py", description=__doc__)
    parser.add_argument("--images-dir", type=pathlib.Path, default=DATASET,
                        help="folder with receipt_NN.jpg (default: the dataset folder)")
    parser.add_argument("--out", type=pathlib.Path, default=DEFAULT_OUT, help="results folder")
    parser.add_argument("--receipts", help="comma list, e.g. 01,04 (default: all found)")
    sub = parser.add_subparsers(dest="command", required=True)

    tess = sub.add_parser("tesseract", help="run local Tesseract (free)")
    tess.add_argument("--tesseract", default=TESSERACT)
    tess.add_argument("--lang", default="eng", help="e.g. eng, pol, pol+eng (pol is not installed today)")
    tess.add_argument("--tessdata", help="tessdata folder, e.g. one that contains pol.traineddata")
    tess.add_argument("--psm", type=int, default=6)
    tess.add_argument("--scale", type=int, default=1, help="upsample factor before OCR")
    tess.set_defaults(func=cmd_tesseract)

    azure = sub.add_parser("azure", help="Azure prebuilt-receipt (cached; F0 quota)")
    azure.add_argument("--max-calls", type=int, default=30, help="cap on analyze POSTs in the ledger")
    azure.add_argument("--offline", action="store_true", help="use the cache only, never call Azure")
    azure.add_argument("--locale", help="optional locale hint, e.g. pl-PL (default: auto)")
    azure.set_defaults(func=cmd_azure)

    haiku = sub.add_parser("haiku", help="Haiku variant matrix (dry run unless --run-haiku)")
    haiku.add_argument("--variants", default="all", help="comma list of variant ids or 'all'")
    haiku.add_argument("--repeats", type=int, default=1)
    haiku.add_argument("--run-haiku", action="store_true", help="really call Claude (paid)")
    haiku.add_argument("--budget-usd", type=float, help="hard stop for --run-haiku")
    haiku.set_defaults(func=cmd_haiku)

    rep = sub.add_parser("report", help="score all runs, write report.md/report.json")
    rep.add_argument("--tesseract-reference", default="tesseract-eng-psm6",
                     help="Tesseract cache used as the second reference word layer")
    rep.add_argument("--repeats", type=int, default=1, help="repeats assumed in the Haiku cost plan")
    rep.add_argument("--no-saved-haiku", action="store_true")
    rep.set_defaults(func=cmd_report)

    args = parser.parse_args(argv)
    if args.command == "haiku" and args.run_haiku and not args.budget_usd:
        parser.error("--run-haiku needs --budget-usd")
    args.out = args.out.resolve()
    args.func(args)


if __name__ == "__main__":
    sys.exit(main())
