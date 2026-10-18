"""Reference output of the study's Python code for the C# parity test.

For every household-2026-08 receipt: the OCR line indexes hybrid_words (line
layer only) picks per Haiku item, and the "combo" snake outlines
(pad_v=0.10, pad_u=0.30) in photo fractions.

Usage: venv/bin/python -I make_reference.py <repo> <analyze.py> <scratch out> <fixture.json>
"""
import importlib.util
import json
import sys
from pathlib import Path

repo, analyze_path, scratch, fixture = sys.argv[1:5]
sys.argv = [analyze_path, repo, scratch]
spec = importlib.util.spec_from_file_location("analyze", analyze_path)
A = importlib.util.module_from_spec(spec)
spec.loader.exec_module(A)

out = {"source": "highlight_study/analyze.py hybrid_words(haiku, [], page) + snake(pad_v=0.10, pad_u=0.30)", "receipts": {}}
for n in range(1, 21):
    rid = f"{n:02d}"
    resp = json.loads((A.H08_CACHE / f"receipt_{rid}.json").read_text())["response"]
    page = A.Page(resp)
    pg = A.page_of(resp)
    W, H = pg["width"], pg["height"]
    haiku = json.loads((A.HAIKU_RUN / f"haiku-prod-v4-medium__claude-haiku-5-5__{rid}.json").read_text())["parsed"]
    hw = A.hybrid_words(haiku["items"], [], page)
    items = []
    for i, _ in enumerate(haiku["items"]):
        if i not in hw or not hw[i][0]:
            items.append(None)
            continue
        words = hw[i][0]
        lines = sorted({page.line_of[id(w)] for w in words})
        shapes = A.snake(page.groups(words), pad_v=0.10, pad_u=0.30)
        polys = [[round(float(v), 4) for p in s for v in (min(max(p[0] / W, 0), 1), min(max(p[1] / H, 0), 1))] for s in shapes]
        items.append({"lines": lines, "polygons": polys})
    out["receipts"][rid] = items
Path(fixture).write_text(json.dumps(out, separators=(",", ":")) + "\n")
print(sum(1 for r in out["receipts"].values() for i in r if i), "items")
