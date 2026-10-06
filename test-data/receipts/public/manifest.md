# Public Polish receipt sources

Retrieved 2026-07-15 for private local OCR/vision evaluation. This folder is **not** a training dataset. Public availability does not automatically grant a right to copy an image, so only three defensible files were stored locally.

## Files stored locally

| File | Store and date | Type | Quality | Reuse note |
|---|---|---|---|---|
| `biedronka-2020-01-09-cc-by-sa.jpg` | Biedronka, 2020-01-09 | Real handheld receipt photo | High resolution; perspective and thermal-paper noise | CC BY-SA 4.0; credit Hippietrail / Wikimedia Commons |
| `biedronka-eparagon-2024-07-official.jpg` | Biedronka, receipt 2024-07-22 | Real e-receipt visualization | Straight, small, two items | Official press download; private QA only, no open license stated |
| `lidl-2025-official-sample.png` | Lidl, sample dated 2025-03-10 | Illustrative physical receipt | Small but readable; colored field annotations | Crop from Lidloteria 2025 rules, Annex 1, page 23; private QA only |

Sources:

- [Biedronka real receipt, Wikimedia Commons](https://commons.wikimedia.org/wiki/File:Polish_supermarket_receipt.jpg) — explicitly licensed CC BY-SA 4.0.
- [Biedronka e-receipt press release](https://media.biedronka.pl/336828-biedronka-cyfryzuje-paragony-fiskalne) — attachment named `moja biedronka e-paragon w pdf.jpg` is offered with a download action.
- [Lidloteria 2025 official rules](https://www.lidl.pl/static/assets/Regulamin-Lidloteria2025_FINAL-1430486.pdf) — Annex 1, page 23 contains example paper, Lidl Plus, and online purchase documents. The local file is a crop of the paper example and must not be treated as a real shopper photograph.

The official Biedronka image contains a masked payment-card number with a few trailing digits visible. It contains no customer name. The Wikimedia image contains store and fiscal business identifiers but no obvious customer data.

## Verified candidates kept as links only

| Store | Source | Date | Why it was not copied |
|---|---|---|---|
| Żabka | [Fakt: receipt from a Żabka promotion](https://www.fakt.pl/pieniadze/zabka-rozdaje-zdrapki-za-zakupy-to-udalo-mi-sie-wydrapac/vv1g2hg) | Published 2025-12-03; receipt 2025-11-29 | Fakt credits copyrighted media and explicitly reserves automated TDM/AI use. The preview is otherwise a strong five-item example with discounts. |
| Auchan | [Reddit: Auchan vs Biedronka receipts](https://www.reddit.com/r/Polska/comments/1cb52rg/paragony_cd_auchan_vs_biedronka/) | 2024-04-23 | User-generated content with no reusable license. The long Auchan receipt would be useful for small-text stress testing. |
| Auchan | [Fakt: discounted Auchan basket and receipt](https://www.fakt.pl/pieniadze/oszczedzanie/zrobila-ogromne-zakupy-w-auchan-za-1-zl-16-gr-ty-tez-tak-mozesz/1v8x704) | 2024-03-22 | Fakt prohibits automated TDM/AI use and credits third-party Instagram material. |
| Carrefour | [o2: 55 cm Carrefour receipt/printouts](https://www.o2.pl/biznes/kupil-tylko-chipsy-paragon-mial-pol-metra-7029839433005728a) | 2024-05-21 | The images are marked © o2.pl with no reuse license. The article explicitly identifies Carrefour and includes the chain's response. |
| Biedronka + Lidl | [Przegląd Handlowy comparison](https://www.przegladhandlowy.pl/11938/paragony-w-biedronce-i-lidlu-porownanie/) | 2024-04-12 | No reuse license; the available preview is only 250×181, too small for a fair benchmark. |
| E.Leclerc | [Illustrative receipt in contest rules](https://www.regulaminy.albedomarketing.pl/storage/terms/V8QGfU9wK9odZ7x3GuRfJmAJZ2Pt7w-metaUmVndWxhbWluIGtvbmt1cnN1X0UubGVjcmVyY18xMjA3MjAyM19fZmluYWxfICgwMDIpLnBkZg%3D%3D-.pdf) | Sample dated 2023-10-17 | Synthetic and without an open license; retained only as a fallback source link. |

## Coverage gaps and recommendation

No reusable modern real receipt photograph was found for Żabka, Auchan, Carrefour, or E.Leclerc. Search results frequently led to copyrighted news images, private social accounts, unreadably small previews, or promotional sample receipts.

For a meaningful and defensible benchmark, collect at least three owner-consented phone photos per store:

1. a short, flat, well-lit receipt;
2. a long receipt with discounts and weighted products;
3. a difficult receipt with perspective, folds, fading, or uneven light.

For every image, save a manually verified expected JSON file. Keep clean electronic receipts in a separate benchmark group because they do not measure camera-photo robustness.
