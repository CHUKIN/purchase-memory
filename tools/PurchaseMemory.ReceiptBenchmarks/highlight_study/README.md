# Highlight study (stage 1.3, 2026-10-17)

Offline study scripts behind plan #1 stage 1.3 (Azure text polygons, "combo"
highlight). Reference only — not reviewed as production code.

- `run_azure.py` — live Azure Document Intelligence calls (`prebuilt-receipt` /
  `prebuilt-read`) on `test-data/` photos; key from `~/.zshrc` via `zsh -ic`,
  never printed. Writes raw JSON to `<out>/cache/<model>/`. F0 tier: costs 0,
  but each run needs the owner's OK.
- `analyze.py REPO OUT` — offline: reads cached Azure JSON + ground truth + saved
  Haiku runs, matches items to Azure lines, chains words into highlight shapes,
  scores own/foreign ink and renders mockups (`render(kind=...)`: `marker`,
  `spotlight`, `underline`, `combo` = the owner's choice, `today`).
  Deps: numpy, opencv-python-headless, pillow (use a venv outside the repo).
- Outputs (images of receipts) go to `OUT` outside the repo — never commit them.
- Test fixtures of the C# port (`tests/PurchaseMemory.IntegrationTests/TestData/`,
  checked by `ReceiptTextMatcherTests`): `make_pairs.py REPO OUT.json` (ground
  truth → Haiku item pairs via `score_items.score_receipt`, stdlib only) and
  `make_reference.py REPO analyze.py SCRATCH OUT.json` (the lines and "combo"
  outlines `analyze.py` itself picks; needs the venv above).
