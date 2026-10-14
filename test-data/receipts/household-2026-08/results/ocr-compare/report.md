# OCR comparison: Azure prebuilt-receipt vs Tesseract vs Haiku

Generated 2026-10-14T14:34:02+0200. Dataset: household-2026-08 (20 photos; item labels for 8).

| configuration | ok/receipts | total ok | lines sum = total | items ok (8 labelled) | disc | extra | box present | box plausible | box hit (Azure words) | box hit (Tesseract words) | GT lines with hit box | s/receipt (median) | $/receipt | paid-equivalent $/receipt |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| azure-prebuilt-receipt | 20/20 | 19/20 | 14/20 | 110/110 (100.0%) | 2/5 | 3 | 100.0% | 98.9% | 98.9% | 72.1% | 73/110 (66.4%) | 5.75 (4.52) | 0.0 | 0.01 |
| haiku-bbox-required-high | 20/20 | 20/20 | 20/20 | 110/110 (100.0%) | 5/5 | 0 | 100.0% | 100.0% | 94.8% | 73.7% | 104/110 (94.5%) | 16.28 (13.73) | 0.002418 | 0.002418 |
| haiku-bbox-required-low | 20/20 | 20/20 | 19/20 | 108/110 (98.2%) | 5/5 | 0 | 100.0% | 100.0% | 95.3% | 74.6% | 104/110 (94.5%) | 10.43 (9.77) | 0.001628 | 0.001628 |
| haiku-bbox-required-medium | 20/20 | 20/20 | 19/20 | 109/110 (99.1%) | 5/5 | 0 | 100.0% | 100.0% | 92.3% | 71.6% | 102/110 (92.7%) | 13.01 (11.63) | 0.001933 | 0.001933 |
| haiku-bbox-required-off | 20/20 | 20/20 | 17/20 | 107/110 (97.3%) | 4/5 | 0 | 100.0% | 100.0% | 33.9% | 29.2% | 34/110 (30.9%) | 4.9 (4.95) | 0.000929 | 0.000929 |
| haiku-full-structured-high | 20/20 | 20/20 | 20/20 | 110/110 (100.0%) | 5/5 | 0 | 100.0% | 100.0% | 99.0% | 73.2% | 109/110 (99.1%) | 14.5 (14.17) | 0.002116 | 0.002116 |
| haiku-full-structured-low | 20/20 | 20/20 | 20/20 | 110/110 (100.0%) | 5/5 | 0 | 100.0% | 100.0% | 94.8% | 73.7% | 102/110 (92.7%) | 10.31 (8.98) | 0.001527 | 0.001527 |
| haiku-full-structured-medium | 20/20 | 20/20 | 20/20 | 110/110 (100.0%) | 5/5 | 0 | 100.0% | 100.0% | 97.9% | 74.7% | 108/110 (98.2%) | 12.02 (11.04) | 0.001776 | 0.001776 |
| haiku-full-structured-off | 20/20 | 20/20 | 18/20 | 108/110 (98.2%) | 5/5 | 0 | 100.0% | 100.0% | 49.0% | 37.1% | 38/110 (34.5%) | 4.16 (4.06) | 0.000787 | 0.000787 |
| haiku-prod-v3-high | 20/20 | 20/20 | 20/20 | 110/110 (100.0%) | 5/5 | 0 | 67.0% | 67.0% | 67.0% | 62.9% | 64/110 (58.2%) | 14.05 (12.88) | 0.002117 | 0.002117 |
| haiku-prod-v3-low | 20/20 | 20/20 | 19/20 | 109/110 (99.1%) | 5/5 | 0 | 5.7% | 5.7% | 4.6% | 4.1% | 0/110 (0.0%) | 8.02 (7.52) | 0.001269 | 0.001269 |
| haiku-prod-v3-medium | 20/20 | 20/20 | 20/20 | 110/110 (100.0%) | 5/5 | 0 | 25.8% | 25.8% | 25.8% | 23.7% | 10/110 (9.1%) | 9.82 (9.26) | 0.00154 | 0.00154 |
| haiku-prod-v3-off | 20/20 | 20/20 | 18/20 | 107/110 (97.3%) | 5/5 | 0 | 2.1% | 2.1% | 0.0% | 0.0% | 0/110 (0.0%) | 4.29 (4.07) | 0.000779 | 0.000779 |
| haiku-prod-v3-saved-low-r1 | 20/20 | 20/20 | 18/20 | 108/110 (98.2%) | 5/5 | 0 | 6.7% | 6.7% | 6.7% | 6.2% | 0/110 (0.0%) | 7.51 (6.88) | 0.001225 | 0.001225 |
| haiku-prod-v3-saved-low-r2 | 20/20 | 20/20 | 20/20 | 109/110 (99.1%) | 5/5 | 0 | 5.7% | 5.7% | 5.7% | 5.2% | 0/110 (0.0%) | 7.96 (7.45) | 0.001271 | 0.001271 |
| haiku-prod-v3-saved-medium-r1 | 20/20 | 20/20 | 20/20 | 109/110 (99.1%) | 5/5 | 0 | 16.5% | 16.5% | 16.5% | 15.5% | 18/110 (16.4%) | 9.56 (8.45) | 0.001462 | 0.001462 |
| haiku-prod-v3-saved-medium-r2 | 20/20 | 20/20 | 19/20 | 109/110 (99.1%) | 5/5 | 0 | 16.5% | 16.5% | 16.5% | 13.9% | 0/110 (0.0%) | 9.29 (8.22) | 0.001442 | 0.001442 |
| haiku-prod-v4-medium | 20/20 | 20/20 | 20/20 | 109/110 (99.1%) | 5/5 | 0 | 100.0% | 100.0% | 95.9% | 73.7% | 104/110 (94.5%) | 12.28 (10.78) | 0.001773 | — |
| tesseract-eng-psm6 | 20/20 | 3/20 | 0/20 | 27/110 (24.5%) | 2/5 | 12 | 100.0% | 100.0% | 95.3% | 100.0% | 27/110 (24.5%) | 0.28 (0.26) | 0.0 | — |
| tesseract-eng-psm6-x2 | 20/20 | 2/20 | 1/20 | 28/110 (25.5%) | 1/5 | 5 | 100.0% | 100.0% | 98.0% | 86.9% | 31/110 (28.2%) | 0.46 (0.43) | 0.0 | — |
| tesseract-pol-best-psm4 | 20/20 | 0/20 | 0/20 | 12/110 (10.9%) | 1/5 | 3 | 100.0% | 96.6% | 96.6% | 96.6% | 13/110 (11.8%) | 0.32 (0.33) | 0.0 | — |
| tesseract-pol-best-psm6 | 20/20 | 5/20 | 0/20 | 22/110 (20.0%) | 0/5 | 5 | 100.0% | 100.0% | 97.2% | 93.1% | 25/110 (22.7%) | 0.56 (0.49) | 0.0 | — |
| tesseract-pol-best-psm6-x2 | 20/20 | 3/20 | 1/20 | 23/110 (20.9%) | 2/5 | 3 | 100.0% | 100.0% | 97.6% | 82.1% | 24/110 (21.8%) | 0.84 (0.81) | 0.0 | — |
| tesseract-pol-eng-best-psm4 | 20/20 | 0/20 | 0/20 | 12/110 (10.9%) | 1/5 | 2 | 100.0% | 96.4% | 96.4% | 96.4% | 13/110 (11.8%) | 0.62 (0.65) | 0.0 | — |
| tesseract-pol-eng-best-psm6 | 20/20 | 4/20 | 0/20 | 28/110 (25.5%) | 0/5 | 6 | 100.0% | 100.0% | 100.0% | 98.7% | 29/110 (26.4%) | 1.18 (1.06) | 0.0 | — |
| tesseract-pol-eng-fast-psm4 | 20/20 | 0/20 | 0/20 | 8/110 (7.3%) | 1/5 | 5 | 100.0% | 96.9% | 96.9% | 96.9% | 8/110 (7.3%) | 0.37 (0.38) | 0.0 | — |
| tesseract-pol-eng-fast-psm6 | 20/20 | 3/20 | 0/20 | 22/110 (20.0%) | 2/5 | 7 | 100.0% | 100.0% | 100.0% | 95.4% | 22/110 (20.0%) | 0.6 (0.55) | 0.0 | — |
| tesseract-pol-eng-standard-psm4 | 20/20 | 1/20 | 0/20 | 12/110 (10.9%) | 1/5 | 6 | 100.0% | 94.6% | 91.9% | 91.9% | 14/110 (12.7%) | 0.53 (0.54) | 0.0 | — |
| tesseract-pol-eng-standard-psm6 | 20/20 | 2/20 | 0/20 | 32/110 (29.1%) | 1/5 | 3 | 100.0% | 100.0% | 97.8% | 100.0% | 33/110 (30.0%) | 0.82 (0.81) | 0.0 | — |
| tesseract-pol-fast-psm4 | 20/20 | 1/20 | 0/20 | 3/110 (2.7%) | 1/5 | 5 | 100.0% | 95.7% | 95.7% | 91.3% | 3/110 (2.7%) | 0.22 (0.23) | 0.0 | — |
| tesseract-pol-fast-psm6 | 20/20 | 3/20 | 0/20 | 12/110 (10.9%) | 2/5 | 8 | 100.0% | 100.0% | 98.6% | 92.8% | 13/110 (11.8%) | 0.33 (0.3) | 0.0 | — |
| tesseract-pol-fast-psm6-x2 | 20/20 | 2/20 | 0/20 | 14/110 (12.7%) | 1/5 | 4 | 100.0% | 100.0% | 97.4% | 88.2% | 16/110 (14.5%) | 0.54 (0.51) | 0.0 | — |
| tesseract-pol-standard-psm4 | 20/20 | 1/20 | 0/20 | 12/110 (10.9%) | 1/5 | 4 | 100.0% | 96.9% | 93.8% | 93.8% | 14/110 (12.7%) | 0.41 (0.47) | 0.0 | — |
| tesseract-pol-standard-psm6 | 20/20 | 3/20 | 0/20 | 26/110 (23.6%) | 0/5 | 4 | 100.0% | 100.0% | 97.4% | 93.5% | 26/110 (23.6%) | 0.58 (0.6) | 0.0 | — |

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
- `tesseract-pol-best-psm4`: langs pol; installed eng,pol
- `tesseract-pol-best-psm6`: langs pol; installed eng,pol
- `tesseract-pol-best-psm6-x2`: langs pol; installed eng,pol
- `tesseract-pol-eng-best-psm4`: langs pol+eng; installed eng,pol
- `tesseract-pol-eng-best-psm6`: langs pol+eng; installed eng,pol
- `tesseract-pol-eng-fast-psm4`: langs pol+eng; installed eng,pol
- `tesseract-pol-eng-fast-psm6`: langs pol+eng; installed eng,pol
- `tesseract-pol-eng-standard-psm4`: langs pol+eng; installed eng,pol
- `tesseract-pol-eng-standard-psm6`: langs pol+eng; installed eng,pol
- `tesseract-pol-fast-psm4`: langs pol; installed eng,pol
- `tesseract-pol-fast-psm6`: langs pol; installed eng,pol
- `tesseract-pol-fast-psm6-x2`: langs pol; installed eng,pol
- `tesseract-pol-standard-psm4`: langs pol; installed eng,pol
- `tesseract-pol-standard-psm6`: langs pol; installed eng,pol
- Azure calls in the ledger (all runs): {'analyzeCalls': 25, 'getCalls': 29}

## Hybrid: Haiku items + Azure boxes (free, from cache)

Items, totals and discounts are Haiku's (so those columns equal the Haiku run); only boxes change. Box columns are over the 110 labelled lines (8 receipts), matched to recognised lines by `score_items.pairs`; the reference word layer is Azure's (Tesseract for the plain Azure row, as above, so its right/wrong mostly reflect Tesseract misreads). right / ambiguous / wrong: see the box verdict definition below; wrong = other line + miss; "of which other line" = wrong boxes that land on another labelled line. "hit (Tess.)" re-checks the same boxes against Tesseract eng words (independent of Azure, but Tesseract misreads Polish text, so it is a lower bound). Discount rows = recognised negative lines on all 20 receipts with a hit box.

| configuration | items ok | total ok | disc | GT lines with box | right | ambiguous | wrong | of which other line | hit (Tess.) | box present (all 20) | box hit, Azure words (all 20) | discount rows with hit box |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| azure-prebuilt-receipt | 110/110 | 19/20 | 2/5 | 110/110 | 69/110 | 4 | 37 | 2 | 73/110 | 100.0% | 98.9% | 4/4 |
| haiku-prod-v3-saved-low-r1 | 108/110 | 20/20 | 5/5 | 0/110 | 0/110 | 0 | 0 | 0 | 0/110 | 6.7% | 6.7% | 0/7 |
| haiku-prod-v3-saved-low-r2 | 109/110 | 20/20 | 5/5 | 0/110 | 0/110 | 0 | 0 | 0 | 0/110 | 5.7% | 5.7% | 1/7 |
| haiku-prod-v3-saved-medium-r1 | 109/110 | 20/20 | 5/5 | 18/110 | 18/110 | 0 | 0 | 0 | 18/110 | 16.5% | 16.5% | 0/7 |
| haiku-prod-v3-saved-medium-r2 | 109/110 | 20/20 | 5/5 | 0/110 | 0/110 | 0 | 0 | 0 | 0/110 | 16.5% | 16.5% | 1/7 |
| hybrid-amount-low-r1 | 108/110 | 20/20 | 5/5 | 108/110 | 105/110 | 3 | 0 | 0 | 73/110 | 95.4% | 94.3% | 4/7 |
| hybrid-amount-low-r2 | 109/110 | 20/20 | 5/5 | 109/110 | 107/110 | 2 | 0 | 0 | 73/110 | 95.9% | 94.8% | 4/7 |
| hybrid-amount-medium-r1 | 109/110 | 20/20 | 5/5 | 109/110 | 107/110 | 2 | 0 | 0 | 73/110 | 95.9% | 94.8% | 4/7 |
| hybrid-amount-medium-r2 | 109/110 | 20/20 | 5/5 | 109/110 | 107/110 | 2 | 0 | 0 | 73/110 | 95.9% | 94.8% | 4/7 |
| hybrid-name-low-r1 | 108/110 | 20/20 | 5/5 | 110/110 | 106/110 | 3 | 1 | 1 | 73/110 | 97.4% | 95.4% | 5/7 |
| hybrid-name-low-r2 | 109/110 | 20/20 | 5/5 | 110/110 | 108/110 | 2 | 0 | 0 | 73/110 | 97.4% | 95.9% | 5/7 |
| hybrid-name-medium-r1 | 109/110 | 20/20 | 5/5 | 110/110 | 108/110 | 2 | 0 | 0 | 73/110 | 97.4% | 95.9% | 5/7 |
| hybrid-name-medium-r2 | 109/110 | 20/20 | 5/5 | 110/110 | 108/110 | 2 | 0 | 0 | 73/110 | 97.4% | 95.9% | 5/7 |
| hybrid-name-amount-low-r1 | 108/110 | 20/20 | 5/5 | 110/110 | 107/110 | 3 | 0 | 0 | 73/110 | 97.4% | 95.9% | 5/7 |
| hybrid-name-amount-low-r2 | 109/110 | 20/20 | 5/5 | 110/110 | 108/110 | 2 | 0 | 0 | 73/110 | 97.4% | 95.9% | 5/7 |
| hybrid-name-amount-medium-r1 | 109/110 | 20/20 | 5/5 | 110/110 | 108/110 | 2 | 0 | 0 | 73/110 | 97.4% | 95.9% | 5/7 |
| hybrid-name-amount-medium-r2 | 109/110 | 20/20 | 5/5 | 110/110 | 108/110 | 2 | 0 | 0 | 73/110 | 97.4% | 95.9% | 5/7 |
| hybrid-aligned-low-r1 | 108/110 | 20/20 | 5/5 | 110/110 | 106/110 | 3 | 1 | 1 | 73/110 | 97.4% | 95.4% | 5/7 |
| hybrid-aligned-low-r2 | 109/110 | 20/20 | 5/5 | 110/110 | 108/110 | 2 | 0 | 0 | 73/110 | 97.4% | 95.9% | 5/7 |
| hybrid-aligned-medium-r1 | 109/110 | 20/20 | 5/5 | 110/110 | 108/110 | 2 | 0 | 0 | 73/110 | 97.4% | 95.9% | 5/7 |
| hybrid-aligned-medium-r2 | 109/110 | 20/20 | 5/5 | 110/110 | 108/110 | 2 | 0 | 0 | 73/110 | 97.4% | 95.9% | 5/7 |
| hybrid-aligned+lines-haiku-bbox-required-high | 110/110 | 20/20 | 5/5 | 110/110 | 107/110 | 3 | 0 | 0 | 73/110 | 100.0% | 98.5% | 6/7 |
| hybrid-aligned+lines-haiku-bbox-required-low | 108/110 | 20/20 | 5/5 | 109/110 | 107/110 | 2 | 0 | 0 | 73/110 | 100.0% | 98.4% | 6/7 |
| hybrid-aligned+lines-haiku-bbox-required-medium | 109/110 | 20/20 | 5/5 | 110/110 | 108/110 | 2 | 0 | 0 | 73/110 | 100.0% | 98.5% | 6/7 |
| hybrid-aligned+lines-haiku-bbox-required-off | 107/110 | 20/20 | 4/5 | 109/110 | 104/110 | 4 | 1 | 1 | 73/110 | 100.0% | 97.9% | 5/6 |
| hybrid-aligned+lines-haiku-full-structured-high | 110/110 | 20/20 | 5/5 | 110/110 | 106/110 | 4 | 0 | 0 | 73/110 | 100.0% | 98.5% | 6/7 |
| hybrid-aligned+lines-haiku-full-structured-low | 110/110 | 20/20 | 5/5 | 110/110 | 107/110 | 3 | 0 | 0 | 73/110 | 100.0% | 98.5% | 6/7 |
| hybrid-aligned+lines-haiku-full-structured-medium | 110/110 | 20/20 | 5/5 | 110/110 | 107/110 | 3 | 0 | 0 | 73/110 | 100.0% | 98.5% | 6/7 |
| hybrid-aligned+lines-haiku-full-structured-off | 108/110 | 20/20 | 5/5 | 110/110 | 106/110 | 3 | 1 | 1 | 73/110 | 100.0% | 97.9% | 6/7 |
| hybrid-aligned+lines-haiku-prod-v3-high | 110/110 | 20/20 | 5/5 | 110/110 | 108/110 | 2 | 0 | 0 | 73/110 | 100.0% | 98.5% | 6/7 |
| hybrid-aligned+lines-haiku-prod-v3-low | 109/110 | 20/20 | 5/5 | 110/110 | 107/110 | 3 | 0 | 0 | 73/110 | 100.0% | 98.5% | 6/7 |
| hybrid-aligned+lines-haiku-prod-v3-medium | 110/110 | 20/20 | 5/5 | 110/110 | 108/110 | 2 | 0 | 0 | 73/110 | 100.0% | 98.5% | 6/7 |
| hybrid-aligned+lines-haiku-prod-v3-off | 107/110 | 20/20 | 5/5 | 110/110 | 106/110 | 3 | 1 | 1 | 73/110 | 100.0% | 97.9% | 6/7 |
| hybrid-aligned+lines-haiku-prod-v4-medium | 109/110 | 20/20 | 5/5 | 110/110 | 107/110 | 3 | 0 | 0 | 73/110 | 100.0% | 98.5% | 6/7 |
| hybrid-aligned+lines-low-r1 | 108/110 | 20/20 | 5/5 | 110/110 | 106/110 | 3 | 1 | 1 | 73/110 | 100.0% | 97.9% | 6/7 |
| hybrid-aligned+lines-low-r2 | 109/110 | 20/20 | 5/5 | 110/110 | 108/110 | 2 | 0 | 0 | 73/110 | 100.0% | 98.5% | 6/7 |
| hybrid-aligned+lines-medium-r1 | 109/110 | 20/20 | 5/5 | 110/110 | 108/110 | 2 | 0 | 0 | 73/110 | 100.0% | 98.5% | 6/7 |
| hybrid-aligned+lines-medium-r2 | 109/110 | 20/20 | 5/5 | 110/110 | 108/110 | 2 | 0 | 0 | 73/110 | 100.0% | 98.5% | 6/7 |
| hybrid-aligned+lines+own-low-r1 | 108/110 | 20/20 | 5/5 | 110/110 | 106/110 | 3 | 1 | 1 | 73/110 | 100.0% | 97.9% | 6/7 |
| hybrid-aligned+lines+own-low-r2 | 109/110 | 20/20 | 5/5 | 110/110 | 108/110 | 2 | 0 | 0 | 73/110 | 100.0% | 98.5% | 6/7 |
| hybrid-aligned+lines+own-medium-r1 | 109/110 | 20/20 | 5/5 | 110/110 | 108/110 | 2 | 0 | 0 | 73/110 | 100.0% | 98.5% | 6/7 |
| hybrid-aligned+lines+own-medium-r2 | 109/110 | 20/20 | 5/5 | 110/110 | 108/110 | 2 | 0 | 0 | 73/110 | 100.0% | 98.5% | 6/7 |
| calibration-aligned-medium-r1-boxes-shifted-1 | 109/110 | 20/20 | 5/5 | 110/110 | 52/110 | 15 | 43 | 43 | 40/110 | 97.4% | 55.7% | 4/7 |
| calibration-aligned-medium-r1-boxes-shifted-3 | 109/110 | 20/20 | 5/5 | 109/110 | 5/110 | 0 | 104 | 104 | 1/110 | 97.4% | 13.9% | 2/7 |

Calibration rows take the `hybrid-aligned-medium-r1` boxes and give every line the box of the line 1 (or 3) positions later on the same receipt, so every box is wrong by construction. Their "right" count is how many wrong boxes the check misses: neighbouring rows are close, Azure item boxes on Polish receipts span the name row and the price row, and the check widens boxes by 25% of their height, so an off-by-one box is caught only about half the time; a box several rows away is almost always caught.

Strategies:

```
Free hybrid: Haiku's items/totals/discounts + boxes taken from cached Azure output.

For every saved Haiku run a hybrid run is built per strategy ("scenario").
Items, prices, totals and discount lines stay exactly Haiku's; only each item's
`box` (and `totalBox`) is replaced. Nothing here calls a network service: the
Azure side is read from cache/azure/receipt_NN.json.

Two Azure sources:
  * Azure items  - prebuilt-receipt `Items` (description, total price, region);
  * Azure lines  - the raw OCR page lines (text + polygon). Used as a fallback
    for lines Azure's item model skipped (mostly discount rows like "OPUST").

Strategies (an item that is not matched keeps no box, unless the strategy says
"own"):
  amount       Azure items, same line total (+-0.01), first unused one in
               receipt order;
  name         Azure items, name similarity (score_items.similar >= 0.6),
               one-to-one, best pairs first;
  name-amount  Azure items, score = similarity + 1 if the amount agrees,
               one-to-one best pairs first; a pair needs similarity >= 0.6, or
               >= 0.3 with the right amount;
  aligned      Azure items, same pair score, but an order-preserving alignment
               (both lists are in receipt order); a pair needs similarity >= 0.6
               or the right amount;
  aligned+lines  aligned, then unmatched items go to Azure page lines: the line
               whose text best matches the name (partial similarity >= 0.6),
               joined with the nearest line just below it that prints the price;
               failing that, an unused line that prints the price;
  aligned+lines+own  as above, then Haiku's own box when still unmatched.
```

Box verdict (per labelled line):

```
"right" / "ambiguous" / "other-line" / "miss" for the plausible box of the line
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
```

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

## Haiku matrix cost estimate (actual spend: haiku_ledger.json)

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
