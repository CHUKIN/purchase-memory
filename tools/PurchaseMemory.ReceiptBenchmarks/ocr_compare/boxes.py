"""Box geometry and the "box hit" check.

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
"""
import difflib
import re

MAX_LINE_HEIGHT = 0.15
MIN_HEIGHT = 0.003
MIN_WIDTH = 0.01
SLACK = 0.02
ROW_MARGIN = 0.25
NAME_MATCH = 0.6


def polygon_to_box(polygon, width, height):
    """Flat [x1, y1, x2, y2, ...] polygon in pixels -> normalised axis-aligned box."""
    if not polygon or width <= 0 or height <= 0:
        return None
    xs, ys = polygon[0::2], polygon[1::2]
    left, top = min(xs), min(ys)
    return [left / width, top / height, (max(xs) - left) / width, (max(ys) - top) / height]


def union(boxes):
    boxes = [b for b in boxes if b]
    if not boxes:
        return None
    left = min(b[0] for b in boxes)
    top = min(b[1] for b in boxes)
    right = max(b[0] + b[2] for b in boxes)
    bottom = max(b[1] + b[3] for b in boxes)
    return [left, top, right - left, bottom - top]


def plausible(box):
    if not box or len(box) != 4 or any(v is None for v in box):
        return False
    x, y, w, h = box
    return (x >= -SLACK and y >= -SLACK and x + w <= 1 + SLACK and y + h <= 1 + SLACK
            and w >= MIN_WIDTH and MIN_HEIGHT <= h <= MAX_LINE_HEIGHT)


def text_in_box(box, words):
    """Join (in reading order) the reference words whose centre lies in the widened box."""
    x, y, w, h = box
    left, right = x - 0.02 * w, x + w + 0.02 * w
    top, bottom = y - ROW_MARGIN * h, y + h + ROW_MARGIN * h
    inside = []
    for word in words:
        wx, wy, ww, wh = word["box"]
        cx, cy = wx + ww / 2, wy + wh / 2
        if left <= cx <= right and top <= cy <= bottom:
            inside.append((round(cy, 3), cx, word["text"]))
    return " ".join(text for _, _, text in sorted(inside))


def partial_similarity(name, text, normalize):
    """Best difflib ratio of the normalised name against same-length windows of text."""
    a, b = normalize(name), normalize(text)
    if len(a) < 3 or not b:
        return 0.0
    if len(b) <= len(a):
        return difflib.SequenceMatcher(None, a, b).ratio()
    best = 0.0
    for start in range(0, len(b) - len(a) + 1):
        ratio = difflib.SequenceMatcher(None, a, b[start:start + len(a)]).ratio()
        if ratio > best:
            best = ratio
            if best == 1.0:
                break
    return best


def price_tokens(value):
    if value is None:
        return set()
    amount = f"{abs(float(value)):.2f}"
    return {amount, amount.replace(".", ",")}


def price_printed(line_total, text):
    tokens = price_tokens(line_total)
    found = {m.replace(" ", "") for m in re.findall(r"\d+[.,] ?\d{2}", text)}
    return bool(tokens & found)


def check_box(item, words, normalize):
    """Return {"present", "plausible", "hit", "nameHit", "priceHit"} for one item."""
    box = item.get("box")
    result = {"present": bool(box), "plausible": plausible(box),
              "hit": False, "nameHit": False, "priceHit": False}
    if not result["plausible"] or not words:
        return result
    text = text_in_box(box, words)
    result["nameHit"] = partial_similarity(item.get("name") or "", text, normalize) >= NAME_MATCH
    result["priceHit"] = price_printed(item.get("lineTotal"), text)
    result["hit"] = result["nameHit"] or result["priceHit"]
    return result
