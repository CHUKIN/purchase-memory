# OCR comparison: Azure prebuilt-receipt vs Tesseract vs Haiku

Generated 2026-10-14T00:10:18+0200. Dataset: household-2026-08 (20 photos; item labels for 8).

| configuration | ok/receipts | total ok | lines sum = total | items ok (8 labelled) | disc | extra | box present | box plausible | box hit (Azure words) | box hit (Tesseract words) | GT lines with hit box | s/receipt (median) | $/receipt | paid-equivalent $/receipt |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| azure-prebuilt-receipt | 20/20 | 19/20 | 14/20 | 110/110 (100.0%) | 2/5 | 3 | 100.0% | 98.9% | 98.9% | 72.1% | 73/110 (66.4%) | 5.75 (4.52) | 0.0 | 0.01 |
| haiku-prod-v3-saved-low-r1 | 20/20 | 20/20 | 18/20 | 108/110 (98.2%) | 5/5 | 0 | 6.7% | 6.7% | 6.7% | 6.2% | 0/110 (0.0%) | 7.51 (6.88) | 0.001225 | 0.001225 |
| haiku-prod-v3-saved-low-r2 | 20/20 | 20/20 | 20/20 | 109/110 (99.1%) | 5/5 | 0 | 5.7% | 5.7% | 5.7% | 5.2% | 0/110 (0.0%) | 7.96 (7.45) | 0.001271 | 0.001271 |
| haiku-prod-v3-saved-medium-r1 | 20/20 | 20/20 | 20/20 | 109/110 (99.1%) | 5/5 | 0 | 16.5% | 16.5% | 16.5% | 15.5% | 18/110 (16.4%) | 9.56 (8.45) | 0.001462 | 0.001462 |
| haiku-prod-v3-saved-medium-r2 | 20/20 | 20/20 | 19/20 | 109/110 (99.1%) | 5/5 | 0 | 16.5% | 16.5% | 16.5% | 13.9% | 0/110 (0.0%) | 9.29 (8.22) | 0.001442 | 0.001442 |
| tesseract-eng-psm6 | 20/20 | 3/20 | 0/20 | 27/110 (24.5%) | 2/5 | 12 | 100.0% | 100.0% | 95.3% | 100.0% | 27/110 (24.5%) | 0.28 (0.26) | 0.0 | — |
| tesseract-eng-psm6-x2 | 20/20 | 2/20 | 1/20 | 28/110 (25.5%) | 1/5 | 5 | 100.0% | 100.0% | 98.0% | 86.9% | 31/110 (28.2%) | 0.46 (0.43) | 0.0 | — |

Notes:
- Box hit is self-referential for a provider checked against its own word layer (Azure vs Azure words, Tesseract vs Tesseract words); read the other column for those rows.
- "GT lines with hit box" uses Azure words as the reference, except for the Azure row, which is checked against Tesseract (eng) words to stay independent (a pessimistic bound: Tesseract misreads a lot).
- `azure-prebuilt-receipt`: api-version 2024-11-30, F0 (free); paid-equivalent = S0 list $10/1000 pages
- `haiku-prod-v3-saved-low-r1`: production prompt v3, saved run low-r1 (effort low); outputs saved 2026-10-11, no new call
- `haiku-prod-v3-saved-low-r2`: production prompt v3, saved run low-r2 (effort low); outputs saved 2026-10-11, no new call
- `haiku-prod-v3-saved-medium-r1`: production prompt v3, saved run medium-r1 (effort medium); outputs saved 2026-10-11, no new call
- `haiku-prod-v3-saved-medium-r2`: production prompt v3, saved run medium-r2 (effort medium); outputs saved 2026-10-11, no new call
- `tesseract-eng-psm6`: langs eng; installed eng,osd,snum; pol MISSING
- `tesseract-eng-psm6-x2`: langs eng; installed eng,osd,snum; pol MISSING
- Azure calls in the ledger (all runs): {'analyzeCalls': 25, 'getCalls': 29}

## Box hit definition

```
Box geometry and the "box hit" check.

Boxes are [x, y, width, height] as fractions of the image (top-left origin).

A recognised line's box is a HIT when all of these hold:
  1. it exists and is geometrically plausible: inside the image (2% slack),
     width >= 1% and 0.3% <= height <= MAX_LINE_HEIGHT of the image, so a box
     around half the receipt does not count;
  2. a reference OCR word layer (Azure words or Tesseract words, both reported)
     has text inside the box: words whose centre falls in the box, slightly
     widened (ROW_MARGIN of the box height up/down, 2% of the width sideways);
  3. that text matches the line: the item name fuzzy-matches it
     (best same-length window, score_items normalisation, >= NAME_MATCH), or the
     line price is printed there ("5,98" / "5.98"; Polish receipts often put
     the price on the row under the name).
The check says "the highlight would land on the right line", not that the box
is tight.
```

## Haiku matrix (planned, not run)

| variant | input tok | output tok (est.) | $/receipt (est.) | calls | $ total (est.) | $ worst case |
|---|---:|---:|---:|---:|---:|---:|
| haiku-prod-v3-off | 2844 | 1542 | 0.00105 | 20 | 0.0211 | 0.1695 |
| haiku-prod-v3-low | 2844 | 1927 | 0.00125 | 20 | 0.0250 | 0.1695 |
| haiku-prod-v3-medium | 2844 | 2336 | 0.00145 | 20 | 0.0290 | 0.1695 |
| haiku-prod-v3-high | 2844 | 3503 | 0.00204 | 20 | 0.0407 | 0.1695 |
| haiku-bbox-required-off | 2903 | 1784 | 0.00118 | 20 | 0.0236 | 0.1696 |
| haiku-bbox-required-low | 2903 | 2170 | 0.00137 | 20 | 0.0275 | 0.1696 |
| haiku-bbox-required-medium | 2903 | 2578 | 0.00158 | 20 | 0.0316 | 0.1696 |
| haiku-bbox-required-high | 2903 | 3746 | 0.00216 | 20 | 0.0433 | 0.1696 |
| haiku-full-structured-off | 2871 | 1697 | 0.00114 | 20 | 0.0227 | 0.1696 |
| haiku-full-structured-low | 2871 | 2083 | 0.00133 | 20 | 0.0266 | 0.1696 |
| haiku-full-structured-medium | 2871 | 2491 | 0.00153 | 20 | 0.0307 | 0.1696 |
| haiku-full-structured-high | 2871 | 3659 | 0.00212 | 20 | 0.0423 | 0.1696 |
| **all** | | | | 240 | 0.3641 | 2.0348 |
