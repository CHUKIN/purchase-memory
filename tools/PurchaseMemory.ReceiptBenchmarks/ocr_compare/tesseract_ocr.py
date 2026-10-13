"""Tesseract (local CLI) as a word/line box source plus a naive receipt parser.

Tesseract returns text and boxes, not receipt structure. The parser below is a
deliberately simple, honest heuristic so its item accuracy can be measured
next to the others; it is not tuned per shop:
  * lines between "PARAGON FISKALNY" (if found) and the first "SUMA" line;
  * tax/subtotal/payment lines are skipped;
  * a line ending in a price ("5,98", "5,98 B", "-2,00") is an item; its name is
    the text before the "1 x 5,98" quantity part or before the price; when that
    leaves no real name (price-only row under the name row) the previous
    line without a price is the name and the box spans both rows;
  * RABAT/OPUST lines become negative lines; the "SUMA" line gives the total.
"""
import re
import subprocess
import tempfile
import time
import pathlib

from . import boxes

# A trailing VAT letter is often read as "8" (B) or "¢" (C) by Tesseract.
PRICE_AT_END = re.compile(r"(-?\d{1,5}[.,] ?\d{2})\s*€?\s*[A-Ga-g8¢]?\s*[|.]?\s*$")
# "1 x 2,79", "1szt*3,95=", "0,609kg*27,99=" ("1" is often read as "I").
QUANTITY = re.compile(r"[\dIl]+(?:[.,]\d+)?\s*[a-z]{0,4}\d?\s*[x×*#%]\s*\d+[.,] ?\d{2}\s*=?", re.I)
# "SUMA PLN" and its common misreads ("SUHA", "SUNA").
TOTAL_LINE = re.compile(r"^\W*su[mnh]a\b")
SKIP = ("ptu", "sprzed", "podsum", "opodat", "kwota", "podatek", "karta", "gotow",
        "reszta", "nip", "rozliczenie", "platnos", "płatnoś", "lacznie", "łącznie")
DISCOUNT = ("rabat", "opust", "upust")


def installed_languages(tesseract, tessdata=None):
    cmd = [tesseract, "--list-langs"] + (["--tessdata-dir", tessdata] if tessdata else [])
    out = subprocess.run(cmd, capture_output=True, text=True, check=False)
    return [line.strip() for line in out.stdout.splitlines()[1:] if line.strip()]


def run_tsv(tesseract, image_path, lang, psm, scale=1, tessdata=None):
    """Run Tesseract and return (tsv text, seconds). scale > 1 upsamples first (Pillow)."""
    started = time.monotonic()
    with tempfile.TemporaryDirectory() as tmp:
        source = str(image_path)
        if scale != 1:
            from PIL import Image  # only needed for upscaling
            image = Image.open(image_path).convert("L")
            image = image.resize((image.width * scale, image.height * scale), Image.LANCZOS)
            source = str(pathlib.Path(tmp) / "scaled.png")
            image.save(source)
        cmd = [tesseract, source, "stdout", "-l", lang, "--psm", str(psm)]
        if tessdata:
            cmd += ["--tessdata-dir", tessdata]
        out = subprocess.run(cmd + ["tsv"], capture_output=True, text=True, check=True)
    return out.stdout, time.monotonic() - started


def parse_tsv(tsv, width, height):
    """TSV -> (words, lines); boxes normalised by the TSV's own page size."""
    rows = [r.split("\t") for r in tsv.splitlines()]
    if not rows:
        return [], []
    header = rows[0]
    col = {name: i for i, name in enumerate(header)}
    page_w, page_h = width, height
    words, grouped = [], {}
    for r in rows[1:]:
        if len(r) != len(header):
            continue
        level = int(r[col["level"]])
        left, top, w, h = (int(r[col[k]]) for k in ("left", "top", "width", "height"))
        if level == 1:
            page_w, page_h = w, h
            continue
        text = r[col["text"]].strip()
        if level != 5 or not text:
            continue
        word = {"text": text, "box": [left / page_w, top / page_h, w / page_w, h / page_h],
                "conf": float(r[col["conf"]])}
        words.append(word)
        key = (r[col["block_num"]], r[col["par_num"]], r[col["line_num"]])
        grouped.setdefault(key, []).append(word)
    lines = []
    for group in grouped.values():
        group.sort(key=lambda wd: wd["box"][0])
        lines.append({"text": " ".join(wd["text"] for wd in group),
                      "box": boxes.union([wd["box"] for wd in group])})
    lines.sort(key=lambda ln: (ln["box"][1], ln["box"][0]))
    return words, lines


def _amount(text):
    return float(text.replace(" ", "").replace(",", "."))


def _letters(text):
    return sum(ch.isalpha() for ch in text)


def parse_receipt(lines, fold):
    """Naive line -> item parse (see module docstring). Returns the `parsed` dict."""
    start = next((i + 1 for i, ln in enumerate(lines) if "paragon" in fold(ln["text"])
                  and "fisk" in fold(ln["text"])), 0)
    items, total, pending = [], None, None
    for line in lines[start:]:
        text = line["text"]
        folded = fold(text)
        price = PRICE_AT_END.search(text)
        if TOTAL_LINE.match(folded) and "ptu" not in folded:
            total = _amount(price.group(1)) if price else None
            break
        if any(word in folded for word in SKIP):
            pending = None
            continue
        if not price:
            pending = line if _letters(text) >= 3 else pending
            continue
        head = text[:price.start()]
        quantity = QUANTITY.search(head)
        name = (head[:quantity.start()] if quantity else head).strip(" .:*-|")
        box = line["box"]
        if _letters(name) < 3 and pending is not None:
            name, box = pending["text"], boxes.union([pending["box"], line["box"]])
        pending = None
        if _letters(name) < 3:
            continue
        value = _amount(price.group(1))
        if any(word in fold(name) for word in DISCOUNT) and value > 0:
            value = -value
        items.append({"name": name, "quantity": None, "unitPrice": None,
                      "lineTotal": value, "box": box})
    return {"merchant": lines[0]["text"] if lines else None, "date": None,
            "currency": None, "total": total, "items": items}
