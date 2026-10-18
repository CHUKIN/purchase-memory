"""Stage 1.3: are Azure line/item polygons right, which line is which, and highlight mockups.

Offline only: reads cached Azure responses (scratchpad + the repo's read-only
household-2026-08 cache), saved Haiku runs and ground truth; writes into OUT.

Usage: python -I analyze.py <repo> <out>
"""
import copy
import html
import json
import math
import sys
from pathlib import Path

import cv2
import numpy as np

REPO = Path(sys.argv[1])
OUT = Path(sys.argv[2])
sys.path.insert(0, str(REPO / "tools" / "PurchaseMemory.ReceiptBenchmarks"))
from ocr_compare import boxes as obox, hybrid  # noqa: E402
from ocr_compare.common import load_score_items  # noqa: E402

SI = load_score_items()
TR = REPO / "test-data" / "receipts"
H08 = TR / "household-2026-08"
H08_CACHE = H08 / "results" / "ocr-compare" / "cache" / "azure"
HAIKU_RUN = H08 / "results" / "ocr-compare" / "runs" / "haiku-prod-v4-medium"
IMG = OUT / "img"
IMG.mkdir(parents=True, exist_ok=True)

# App tokens (src/PurchaseMemory.Web/src/styles.css): outline = --ion-color-warning #9c5a14,
# highlight fill rgba(255,196,0,.18), surrounding dim rgba(0,0,0,.28), border 3px, radius 4px.
WARN = (0x14, 0x5a, 0x9c)        # BGR of #9c5a14
FILL = (0x00, 0xc4, 0xff)        # BGR of rgb(255,196,0)
PHONE_W = 360
SPACE_ASPECT = 1.2               # LinePhoto default aspect before the space is measured


# ------------------------------------------------------------------ data
def photos():
    out = []
    for n in range(1, 21):
        n2 = f"{n:02d}"
        fresh = OUT / "cache" / "prebuilt-receipt" / f"receipt_h08_{n2}.json"
        out.append(dict(tag=f"h08_{n2}", img=H08 / f"receipt_{n2}.jpg",
                        az=fresh if fresh.exists() else H08_CACHE / f"receipt_{n2}.json",
                        read=OUT / "cache" / "prebuilt-read" / f"receipt_h08_{n2}.json",
                        haiku=HAIKU_RUN / f"haiku-prod-v4-medium__claude-haiku-5-5__{n2}.json",
                        gtset="h08", gtid=n2))
    for n in range(1, 12):
        n2 = f"{n:02d}"
        out.append(dict(tag=f"h10_{n2}", img=TR / "household-2026-10" / f"receipt_{n2}.jpg",
                        az=OUT / "cache" / "prebuilt-receipt" / f"receipt_h10_{n2}.json", gtset="h10", gtid=n2))
    for name in ["01-fakturaxl-sample.png", "02-sklep-u-asi.jpg", "03-spolem.webp", "04-wielkopolska.jpg"]:
        stem = name.split(".")[0]
        out.append(dict(tag="root_" + stem, img=TR / name,
                        az=OUT / "cache" / "prebuilt-receipt" / f"receipt_root_{stem}.json"))
    for name in ["biedronka-2020-01-09-cc-by-sa.jpg", "biedronka-eparagon-2024-07-official.jpg",
                 "lidl-2025-official-sample.png"]:
        stem = name.split(".")[0]
        out.append(dict(tag="pub_" + stem, img=TR / "public" / name,
                        az=OUT / "cache" / "prebuilt-receipt" / f"receipt_pub_{stem}.json"))
    return out


def load_gt():
    gt = {}
    for key, path in (("h08", H08 / "results" / "ground_truth_items.json"),
                      ("h10", TR / "household-2026-10" / "ground_truth_items.json")):
        d = json.loads(path.read_text())
        gt[key] = {r: d[r] for r in d["_receipts"]}
    return gt


def read_img(path):
    data = np.frombuffer(path.read_bytes(), np.uint8)
    img = cv2.imdecode(data, cv2.IMREAD_COLOR)
    if img is None:  # webp etc.
        from PIL import Image
        img = cv2.cvtColor(np.array(Image.open(path).convert("RGB")), cv2.COLOR_RGB2BGR)
    return img


def page_of(resp):
    return resp["analyzeResult"]["pages"][0]


def quad(poly):
    return np.array(poly, dtype=np.float64).reshape(-1, 2)


def region_quad(field):
    pts = [quad(r["polygon"]) for r in field.get("boundingRegions") or [] if r.get("pageNumber", 1) == 1]
    if not pts:
        return None
    if len(pts) == 1 and len(pts[0]) == 4:
        return pts[0]
    return min_rect(np.vstack(pts))


def min_rect(points):
    """Rotated rectangle around points, ordered TL, TR, BR, BL in reading direction."""
    (cx, cy), (w, h), a = cv2.minAreaRect(points.astype(np.float32))
    if w < h:  # make width the long side (text lines are wide)
        w, h, a = h, w, a + 90
    if a > 45:
        a -= 180
    if a < -135:
        a += 180
    t = math.radians(a)
    u = np.array([math.cos(t), math.sin(t)])
    v = np.array([-math.sin(t), math.cos(t)])
    c = np.array([cx, cy])
    return np.array([c - u * w / 2 - v * h / 2, c + u * w / 2 - v * h / 2,
                     c + u * w / 2 + v * h / 2, c - u * w / 2 + v * h / 2])


def frame(q):
    """Local frame of a quad: centre, unit u (along the text), unit v (down), w, h, angle deg."""
    u = (q[1] - q[0]) + (q[2] - q[3])
    u = u / (np.linalg.norm(u) or 1)
    v = np.array([-u[1], u[0]])
    c = q.mean(axis=0)
    us, vs = (q - c) @ u, (q - c) @ v
    return dict(c=c, u=u, v=v, umin=us.min(), umax=us.max(), vmin=vs.min(), vmax=vs.max(),
                w=us.max() - us.min(), h=vs.max() - vs.min(), deg=math.degrees(math.atan2(u[1], u[0])))


def rect_pts(f, pad_u=0.0, pad_v=0.0, radius=0.0, n_arc=6):
    """Rotated (optionally rounded) rectangle of a frame, padded, as image points."""
    u0, u1 = f["umin"] - pad_u, f["umax"] + pad_u
    v0, v1 = f["vmin"] - pad_v, f["vmax"] + pad_v
    r = min(radius, (u1 - u0) / 2, (v1 - v0) / 2)
    if r <= 0:
        local = [(u0, v0), (u1, v0), (u1, v1), (u0, v1)]
    else:
        local = []
        for (cu, cv_, a0) in ((u1 - r, v0 + r, -90), (u1 - r, v1 - r, 0), (u0 + r, v1 - r, 90), (u0 + r, v0 + r, 180)):
            for k in range(n_arc + 1):
                a = math.radians(a0 + 90 * k / n_arc)
                local.append((cu + r * math.cos(a), cv_ + r * math.sin(a)))
    return np.array([f["c"] + f["u"] * a + f["v"] * b for a, b in local])


def axis_box(q):
    x0, y0 = q.min(axis=0)
    x1, y1 = q.max(axis=0)
    return np.array([[x0, y0], [x1, y0], [x1, y1], [x0, y1]])


PAD_U, PAD_V = 0.3, 0.05   # padding of a drawn shape, in line heights (receipt rows nearly touch)


def padded(q, pad_v=PAD_V, pad_u=PAD_U, radius=0.3):
    f = frame(q)
    return rect_pts(f, pad_u=pad_u * f["h"], pad_v=pad_v * f["h"], radius=radius * f["h"])


def drawn_shape(q):
    return padded(q)


def haiku_shape(box, W, H):
    """Today's frame: padBox (photoBox.ts) of Haiku's axis box, in pixels."""
    x, y, w, h = box
    px, py = 0.02, 0.015
    nx, ny = max(0, x - px), max(0, y - py)
    nw = min(1 - nx, w + (x - nx) + px)
    nh = min(1 - ny, h + (y - ny) + py)
    return np.array([[nx * W, ny * H], [(nx + nw) * W, ny * H], [(nx + nw) * W, (ny + nh) * H], [nx * W, (ny + nh) * H]])


# ------------------------------------------------------------------ ink
class Ink:
    """Local binarisation + connected components of one photo, limited to the text area."""

    def __init__(self, img, lines):
        gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
        hs = [frame(quad(l["polygon"]))["h"] for l in lines if l.get("polygon")]
        self.line_h = float(np.median(hs)) if hs else 20.0
        block = int(max(15, round(self.line_h * 2.5))) | 1
        binary = cv2.adaptiveThreshold(gray, 255, cv2.ADAPTIVE_THRESH_GAUSSIAN_C, cv2.THRESH_BINARY_INV, block, 12)
        pts = np.vstack([quad(l["polygon"]) for l in lines if l.get("polygon")]).astype(np.int32)
        mask = np.zeros_like(gray)
        cv2.fillPoly(mask, [cv2.convexHull(pts)], 255)
        k = int(self.line_h) | 1
        mask = cv2.dilate(mask, np.ones((k, k), np.uint8))
        binary = cv2.bitwise_and(binary, mask)
        n, labels, stats, cents = cv2.connectedComponentsWithStats(binary, connectivity=8)
        keep = np.zeros(n, bool)
        lh = self.line_h
        for i in range(1, n):
            x, y, w, h, a = stats[i]
            if a >= 4 and h <= 2.2 * lh and w <= 12 * lh:
                keep[i] = True
        self.labels, self.stats, self.cents, self.keep = labels, stats, cents, keep
        self.binary = np.where(keep[labels], 255, 0).astype(np.uint8)
        self.shape = gray.shape

    def _mask(self, shapes):
        m = np.zeros(self.shape, np.uint8)
        for s in shapes:  # one by one: fillPoly with a list XORs overlapping polygons
            cv2.fillPoly(m, [np.round(s).astype(np.int32)], 1)
        return m.astype(bool)

    def measure(self, word_quads, shapes):
        """How well `shapes` (list of polygons, px) cover the ink of the given words.

        own      = ink components whose centre lies in one of the words' polygons,
                   grown by 0.12 word heights up/down and 0.15 sideways (accents,
                   ogonki): the printed text of this line;
        cover    = share of own ink pixels inside the shapes;
        clipped  = share of own characters less than 90% inside the shapes;
        bleed    = share of the ink inside the shapes that is NOT own (neighbouring
                   rows, other columns);
        edge     = share of the shapes' outline (2 px) lying on ink.
        """
        grown = [padded(q, pad_v=0.12, pad_u=0.15, radius=0) for q in word_quads]
        allpts = np.vstack(grown + list(shapes))
        x0, y0 = np.floor(allpts.min(axis=0)).astype(int) - 2
        x1, y1 = np.ceil(allpts.max(axis=0)).astype(int) + 2
        x0, y0 = max(x0, 0), max(y0, 0)
        x1, y1 = min(x1, self.shape[1]), min(y1, self.shape[0])
        idx = np.nonzero(self.keep)[0]
        c = self.cents[idx]
        near = idx[(c[:, 0] >= x0) & (c[:, 0] <= x1) & (c[:, 1] >= y0) & (c[:, 1] <= y1)]
        own_set = np.zeros(len(self.keep), bool)
        for g in grown:
            path = g.astype(np.float32).reshape(-1, 1, 2)
            for i in near:
                if not own_set[i] and cv2.pointPolygonTest(path, (float(self.cents[i][0]), float(self.cents[i][1])), False) >= 0:
                    own_set[i] = True
        # Blobs taller than 1.6 glyph heights are rows glued together by the binarisation
        # (thick print, glare) or creases; they belong to no single row, so skip them.
        # The yardstick is the median glyph height of this line (polygons are taller than glyphs).
        cand = np.nonzero(own_set)[0]
        glyph_h = float(np.median(self.stats[cand, cv2.CC_STAT_HEIGHT])) if len(cand) else 1e9
        tall = np.zeros(len(self.keep), bool)
        tall[near] = self.stats[near, cv2.CC_STAT_HEIGHT] > 1.6 * glyph_h
        own_set &= ~tall
        own = np.nonzero(own_set)[0]
        if not len(own):
            return None
        sm = self._mask(shapes)
        lab_in = self.labels[y0:y1, x0:x1][sm[y0:y1, x0:x1]]
        counts = np.bincount(lab_in, minlength=len(self.keep))
        counts[~self.keep] = 0
        counts[0] = 0
        counts[tall] = 0
        own_total = self.stats[own, cv2.CC_STAT_AREA].sum()
        own_in = counts[own_set].sum()
        foreign_in = counts[~own_set].sum()
        frac = counts[own] / np.maximum(self.stats[own, cv2.CC_STAT_AREA], 1)
        outline = np.zeros(self.shape, np.uint8)
        cv2.polylines(outline, [np.round(s).astype(np.int32) for s in shapes], True, 1, 2)
        ob = outline.astype(bool)
        return dict(cover=float(own_in / own_total), clipped=float((frac < 0.9).mean()),
                    bleed=float(foreign_in / max(own_in + foreign_in, 1)),
                    edge=float((self.binary[ob] > 0).mean()), n_chars=int(len(own)))


# ------------------------------------------------------------------ Azure structures
def in_spans(span, spans):
    return any(s["offset"] <= span["offset"] and span["offset"] + span["length"] <= s["offset"] + s["length"] for s in spans)


class Page:
    def __init__(self, resp):
        self.resp = resp
        p = page_of(resp)
        self.words = [dict(text=w["content"], quad=quad(w["polygon"]), span=w["span"]) for w in p.get("words") or []
                      if w.get("polygon")]
        for w in self.words:
            w["c"] = w["quad"].mean(axis=0)
        self.lines = [dict(text=l.get("content") or "", quad=quad(l["polygon"]), spans=l.get("spans") or [])
                      for l in p.get("lines") or [] if l.get("polygon")]
        for k, l in enumerate(self.lines):
            l["words"] = [w for w in self.words if in_spans(w["span"], l["spans"])]
        self.line_of = {}
        for k, l in enumerate(self.lines):
            for w in l["words"]:
                self.line_of[id(w)] = k

    def words_in(self, spans):
        return [w for w in self.words if in_spans(w["span"], spans)]

    def segments(self, words):
        """Words grouped by OCR line; each group -> quad from its first/last word."""
        groups = {}
        for w in words:
            groups.setdefault(self.line_of.get(id(w), -1), []).append(w)
        segs = []
        for _, ws in sorted(groups.items()):
            u = frame(ws[0]["quad"])["u"]
            ws = sorted(ws, key=lambda w: w["c"] @ u)
            a, b = ws[0]["quad"], ws[-1]["quad"]
            q = np.array([a[0], b[1], b[2], a[3]])
            # The direction comes from the first and last word; the band is widened so
            # every word fits (a bowed line would otherwise be cut in the middle).
            f = frame(q)
            pts = np.vstack([w["quad"] for w in ws]) - f["c"]
            us, vs = pts @ f["u"], pts @ f["v"]
            f.update(umin=us.min(), umax=us.max(), vmin=vs.min(), vmax=vs.max())
            f["w"], f["h"] = f["umax"] - f["umin"], f["vmax"] - f["vmin"]
            segs.append(rect_pts(f))
        return segs

    def groups(self, words):
        """Words of a target grouped by OCR line, each group in reading order."""
        groups = {}
        for w in words:
            groups.setdefault(self.line_of.get(id(w), -1), []).append(w)
        out = []
        for _, ws in sorted(groups.items()):
            u = frame(ws[0]["quad"])["u"]
            out.append(sorted(ws, key=lambda w: w["c"] @ u))
        return out


def snake(groups, pad_v=PAD_V, pad_u=0.15, scale=1.0):
    """Per OCR line: the words chained (hull of each neighbouring pair) - follows a bent line."""
    out = []
    for ws in groups:
        qs = [padded(w["quad"] * scale, pad_v=pad_v, pad_u=pad_u, radius=0) for w in ws]
        if len(qs) == 1:
            out.append(qs[0])
        for x, y in zip(qs, qs[1:]):
            out.append(cv2.convexHull(np.vstack([x, y]).astype(np.float32)).reshape(-1, 2).astype(np.float64))
    return out


def azure_items(resp, page):
    docs = resp["analyzeResult"].get("documents") or []
    fields = docs[0].get("fields", {}) if docs else {}
    out = []
    for e in (fields.get("Items") or {}).get("valueArray") or []:
        val = e.get("valueObject") or {}
        d = val.get("Description") or {}
        name = (d.get("valueString") or d.get("content") or e.get("content") or "").replace("\n", " ")
        tp = val.get("TotalPrice") or val.get("Price") or {}
        amt = (tp.get("valueCurrency") or {}).get("amount", tp.get("valueNumber"))
        words = page.words_in(e.get("spans") or [])
        out.append(dict(name=name, lineTotal=amt, item_quad=region_quad(e), words=words))
    return out


# ------------------------------------------------------------------ matching
def text_in(shapes, words):
    paths = [s.astype(np.float32).reshape(-1, 1, 2) for s in shapes]
    inside = [(w["span"]["offset"], 0, w["text"]) for w in words  # Azure's reading order
              if any(cv2.pointPolygonTest(p, (float(w["c"][0]), float(w["c"][1])), False) >= 0 for p in paths)]
    return " ".join(t for _, _, t in sorted(inside))


def verdict(gt_items, k, shapes, words):
    text = text_in(shapes, words)
    g = gt_items[k]
    if obox.partial_similarity(g["name"], text, SI.normalize) >= obox.NAME_MATCH:
        return "right"
    others = [o for j, o in enumerate(gt_items) if j != k]
    other_name = any(obox.partial_similarity(o["name"], text, SI.normalize) >= obox.NAME_MATCH for o in others)
    if obox.price_printed(g["lineTotal"], text):
        return "ambiguous" if other_name else "right"
    if other_name or any(obox.price_printed(o["lineTotal"], text) for o in others):
        return "other-line"
    return "miss"


def hybrid_words(haiku_items, az_items, page):
    """hybrid.py aligned+lines, keeping the words: {haiku idx: (azure words, item quad|None, source)}."""
    cands = [c for c in az_items if c["words"]]
    result = {}
    for i, j in hybrid.match_aligned(haiku_items, cands, SI.similar).items():
        result[i] = (cands[j]["words"], cands[j]["item_quad"], "azure-item")
    lines = page.lines
    used = set()
    for i, item in enumerate(haiku_items):
        if i in result:
            continue
        name = item.get("name") or ""
        best, best_sim = None, hybrid.NAME_MIN
        for k, line in enumerate(lines):
            if k in used:
                continue
            sim = obox.partial_similarity(name, line["text"], SI.normalize)
            if sim > best_sim or (sim == best_sim and best is None and sim >= hybrid.NAME_MIN):
                best, best_sim = k, sim
        if best is not None:
            chosen = [best]
            if not obox.price_printed(item.get("lineTotal"), lines[best]["text"]):
                h = frame(lines[best]["quad"])["h"]
                top = lines[best]["quad"][:, 1].min()
                below = [k for k, line in enumerate(lines) if k not in used and k != best
                         and top < line["quad"][:, 1].min() <= top + hybrid.PRICE_ROW_GAP * max(h, 1)
                         and obox.price_printed(item.get("lineTotal"), line["text"])]
                if below:
                    chosen.append(min(below, key=lambda k: lines[k]["quad"][:, 1].min()))
            used.update(chosen)
            result[i] = ([w for k in chosen for w in lines[k]["words"]], None, "azure-line")
            continue
        for k, line in enumerate(lines):
            if k not in used and obox.price_printed(item.get("lineTotal"), line["text"]):
                used.add(k)
                result[i] = (line["words"], None, "azure-line-price")
                break
    return result


def shapes_of(t):
    """The candidate highlight shapes of a target, in original pixels."""
    pts = np.vstack([w["quad"] for w in t["words"]])
    rect = min_rect(pts)
    out = dict(seg=[padded(q) for q in t["segs"]], snake=t["snakes"], rect=[padded(rect)], axis=[padded(axis_box(pts), radius=0.3)])
    if t.get("item_quad") is not None:
        out["item"] = [padded(t["item_quad"])]
    if t.get("haiku") is not None:
        out["haiku"] = [t["haiku"]]
    t["rect_quad"] = rect
    return out


# ------------------------------------------------------------------ drawing
def soft_mask(shapes_px, size, blur=0.0):
    m = np.zeros(size, np.uint8)
    for s in shapes_px:
        cv2.fillPoly(m, [np.round(s * 8).astype(np.int32)], 255, cv2.LINE_AA, 3)
    m = m.astype(np.float32) / 255
    if blur > 0:
        m = cv2.GaussianBlur(m, (0, 0), blur)
    return m[..., None]


def poly8(pts_list):
    return [np.round(p * 8).astype(np.int32) for p in pts_list]


def union_mask(shapes_px, size, close_px):
    """Filled union of polygons (8x supersampled), closed so neighbouring words merge smoothly."""
    m = np.zeros(size, np.uint8)
    for q in shapes_px:
        cv2.fillPoly(m, [np.round(q * 8).astype(np.int32)], 255, cv2.LINE_AA, 3)
    k = max(3, int(close_px) | 1)
    m = cv2.morphologyEx(m, cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (k, k)))
    return m


def render(img, kind, t, s):
    """img already scaled by s; target geometry in original pixels. Shapes follow the words."""
    out = img.astype(np.float32)
    size = img.shape[:2]
    word_h = float(np.median([frame(w["quad"])["h"] for w in t["words"]])) * s
    if kind == "marker":   # highlighter: warm band multiplied over the text, round ends
        m = union_mask(snake(t["groups"], pad_v=0.08, pad_u=0.25, scale=s), size, word_h * 0.8)
        m = cv2.GaussianBlur(m, (0, 0), max(0.6, word_h * 0.08)).astype(np.float32)[..., None] / 255 * 0.85
        tint = np.array(FILL, np.float32) / 255
        return np.clip(out * (1 - m) + out * tint * m, 0, 255).astype(np.uint8)
    if kind == "spotlight":  # rest dimmed 45%, the line stays bright with a thin outline
        mu = union_mask(snake(t["groups"], pad_v=0.14, pad_u=0.35, scale=s), size, word_h * 1.2)
        m = cv2.GaussianBlur(mu, (0, 0), 0.8).astype(np.float32)[..., None] / 255
        out = (out * (m + (1 - m) * 0.55)).astype(np.uint8)
        contours, _ = cv2.findContours((mu > 127).astype(np.uint8), cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        cv2.drawContours(out, contours, -1, WARN, 2, cv2.LINE_AA)
        return out
    if kind == "combo":  # marker band + rest dimmed lightly + thin outline
        mu = union_mask(snake(t["groups"], pad_v=0.10, pad_u=0.30, scale=s), size, word_h * 1.0)
        m = cv2.GaussianBlur(mu, (0, 0), max(0.6, word_h * 0.06)).astype(np.float32)[..., None] / 255
        out = out * (m + (1 - m) * 0.70)
        tint = np.array(FILL, np.float32) / 255
        a = m * 0.45
        out = np.clip(out * (1 - a) + out * tint * a, 0, 255).astype(np.uint8)
        contours, _ = cv2.findContours((mu > 127).astype(np.uint8), cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        cv2.drawContours(out, contours, -1, WARN, 2, cv2.LINE_AA)
        return out
    if kind == "underline":  # thick line under the words, following their baseline
        out = out.astype(np.uint8)
        th = max(3, int(round(word_h * 0.14)))
        for ws in t["groups"]:
            if sum(frame(w["quad"])["w"] for w in ws) * s < 1.2 * word_h:
                continue  # a lone tax letter ("A", "X") would become a dot
            pts = []
            for w in ws:
                f = frame(w["quad"] * s)
                off = f["vmax"] + 0.1 * f["h"] + th / 2
                pts += [f["c"] + f["u"] * f["umin"] + f["v"] * off, f["c"] + f["u"] * f["umax"] + f["v"] * off]
            pts = np.array(pts)
            # smooth the chain a little so word-to-word jitter does not show
            if len(pts) > 2:
                sm = pts.copy()
                sm[1:-1] = (pts[:-2] + 2 * pts[1:-1] + pts[2:]) / 4
                pts = sm
            cv2.polylines(out, [np.round(pts * 8).astype(np.int32)], False, WARN, th, cv2.LINE_AA, 3)
            for p in (pts[0], pts[-1]):
                cv2.circle(out, tuple(np.round(p * 8).astype(int)), int(th / 2 * 8), WARN, -1, cv2.LINE_AA, 3)
        return out
    if kind == "today":   # LinePhoto CSS: dim .28 outside, fill rgba(255,196,0,.18), 3px #9c5a14 border
        shp = t["haiku"] * s
        m = soft_mask([shp], size)
        out = out * (m + (1 - m) * 0.72)
        out = out * (1 - 0.18 * m) + np.array(FILL, np.float32) * 0.18 * m
        out = out.astype(np.uint8)
        x0, y0 = shp.min(axis=0)
        x1, y1 = shp.max(axis=0)
        cv2.rectangle(out, (int(x0 * 8), int(y0 * 8)), (int(x1 * 8), int(y1 * 8)), WARN, 3, cv2.LINE_AA, 3)
        return out
    raise ValueError(kind)


def fit_window(box, ratio, aspect):
    """photoBox.ts fitWindow (fractions)."""
    x = max(0, box[0] - 0.04)
    right = min(1, box[0] + box[2] + 0.04)
    width = right - x
    wanted = (width * ratio) / max(aspect, 0.1)
    band = max(0.3, box[3] * 10)
    height = min(1, max(box[3] * 1.5, min(wanted, band)))
    centre = box[1] + box[3] / 2
    y = min(max(0, centre - height / 2), 1 - height)
    return x, y, width, height


def crop_render(img, kind, t, window_pts):
    H, W = img.shape[:2]
    x0, y0 = window_pts.min(axis=0)
    x1, y1 = window_pts.max(axis=0)
    x, y, w, h = fit_window([x0 / W, y0 / H, (x1 - x0) / W, (y1 - y0) / H], W / H, SPACE_ASPECT)
    X0, Y0, X1, Y1 = int(x * W), int(y * H), int(math.ceil((x + w) * W)), int(math.ceil((y + h) * H))
    s = PHONE_W / (X1 - X0)
    big = cv2.resize(img, (int(round(W * s)), int(round(H * s))), interpolation=cv2.INTER_AREA if s < 1 else cv2.INTER_CUBIC)
    r = render(big, kind, t, s)
    return r[int(Y0 * s):int(Y1 * s), int(X0 * s):int(X0 * s) + PHONE_W]


def full_render(img, kind, t, width=PHONE_W):
    H, W = img.shape[:2]
    s = width / W
    big = cv2.resize(img, (width, int(round(H * s))), interpolation=cv2.INTER_AREA if s < 1 else cv2.INTER_CUBIC)
    return render(big, kind, t, s)


def save(name, im, q=82):
    cv2.imwrite(str(IMG / name), im, [cv2.IMWRITE_JPEG_QUALITY, q])
    return f"img/{name}"


def overlay(img, lines, targets=(), fails=(), colour=(230, 120, 20), width=900):
    H, W = img.shape[:2]
    s = width / W if (W > width or W < 500) else 1.0
    big = cv2.resize(img, (int(W * s), int(H * s)), interpolation=cv2.INTER_AREA if s < 1 else cv2.INTER_CUBIC)
    ov = big.copy()
    for l in lines:
        cv2.polylines(ov, poly8([l["quad"] * s]), True, colour, 1, cv2.LINE_AA, 3)
    for k, t in enumerate(targets):
        col = (0, 0, 230) if k in fails else WARN
        cv2.polylines(ov, poly8([p * s for p in t["shapes"]["snake"]]), True, col, 2, cv2.LINE_AA, 3)
        p = (t["segs"][0][0] * s).astype(int)
        cv2.putText(ov, str(k + 1), (int(p[0]) - 18, int(p[1]) + 12), cv2.FONT_HERSHEY_SIMPLEX, 0.45, col, 1, cv2.LINE_AA)
    return ov


# ------------------------------------------------------------------ main
SHAPES = ["seg", "snake", "rect", "item", "axis", "haiku"]


def stat(ms, key):
    v = [m[key] for m in ms if m and m.get(key) is not None]
    return float(np.mean(v)) if v else None


def failed(m):
    return m is None or m["cover"] < 0.9 or m["bleed"] > 0.15


def main():
    gt = load_gt()
    report = dict(photos=[], read_vs_receipt=[])
    keep = {}
    for p in photos():
        resp = json.loads(p["az"].read_text())["response"]
        img = read_img(p["img"])
        H, W = img.shape[:2]
        pg = page_of(resp)
        assert abs(pg["width"] - W) < 2 and abs(pg["height"] - H) < 2, (p["tag"], pg["width"], W)
        page = Page(resp)
        ink = Ink(img, pg["lines"])
        az = azure_items(resp, page)
        targets, haiku = [], None
        if p.get("haiku") and p["haiku"].exists():
            haiku = json.loads(p["haiku"].read_text())["parsed"]
            hw = hybrid_words(haiku["items"], az, page)
            for i, it in enumerate(haiku["items"]):
                if i in hw and hw[i][0]:
                    words, iq, src = hw[i]
                    targets.append(dict(name=it["name"], price=it.get("lineTotal"), words=words, item_quad=iq, source=src,
                                        haiku=haiku_shape(it["box"], W, H) if it.get("box") else None, idx=i))
        else:
            for i, it in enumerate(az):
                if it["words"]:
                    targets.append(dict(name=it["name"], price=it["lineTotal"], words=it["words"], item_quad=it["item_quad"],
                                        source="azure-item", haiku=None, idx=i))
        for t in targets:
            t["segs"] = page.segments(t["words"])
            t["groups"] = page.groups(t["words"])
            t["snakes"] = snake(t["groups"])
            t["shapes"] = shapes_of(t)
            wq = [w["quad"] for w in t["words"]]
            t["m"] = {k: ink.measure(wq, v) for k, v in t["shapes"].items()}
            t["deg"] = float(np.median([frame(q)["deg"] for q in t["segs"]]))
        fails = [k for k, t in enumerate(targets) if failed(t["m"]["snake"])]
        # matching vs ground truth
        match = None
        gts = gt.get(p.get("gtset"), {}).get(p.get("gtid"))
        if gts:
            parsed = haiku if haiku else dict(total=None, items=[dict(name=a["name"], lineTotal=a["lineTotal"]) for a in az])
            res = SI.score_receipt(copy.deepcopy(gts), parsed)
            by_idx = {t["idx"]: t for t in targets}
            vs = dict.fromkeys(["right", "ambiguous", "other-line", "miss", "no-shape", "unrecognised"], 0)
            hv = dict.fromkeys(["right", "ambiguous", "other-line", "miss", "no-box"], 0)
            details = []
            paired = dict(res["pairs"])
            for k, g in enumerate(gts["items"]):
                if k not in paired:
                    vs["unrecognised"] += 1
                    details.append((g["name"], "unrecognised"))
                    continue
                t = by_idx.get(paired[k])
                if t is None:
                    vs["no-shape"] += 1
                    details.append((g["name"], "no-shape"))
                    if haiku:
                        hv["no-box"] += 1
                    continue
                v = verdict(gts["items"], k, t["shapes"]["snake"], page.words)
                vs[v] += 1
                if v != "right":
                    details.append((g["name"], v, t["name"]))
                if haiku:
                    hv["no-box" if t["haiku"] is None else verdict(gts["items"], k, [t["haiku"]], page.words)] += 1
            lo = None
            if haiku:  # the same with the OCR line layer only (what prebuilt-read returns), no Azure Items
                lo = dict.fromkeys(["right", "ambiguous", "other-line", "miss", "no-shape"], 0)
                hl = hybrid_words(haiku["items"], [], page)
                for k, i in res["pairs"]:
                    lo["no-shape" if i not in hl else verdict(gts["items"], k, snake(page.groups(hl[i][0])), page.words)] += 1
            match = dict(n=len(gts["items"]), azure=vs, haiku_own=hv if haiku else None, lines_only=lo, details=details)
        ovp = save(f"coords_{p['tag']}.jpg", overlay(img, page.lines, targets, fails), 80)
        ovr = None
        if p.get("read") and p["read"].exists():
            rresp = json.loads(p["read"].read_text())["response"]
            rpage = Page(rresp)
            ovr = save(f"coords_{p['tag']}_read.jpg", overlay(img, rpage.lines, colour=(40, 160, 40)), 80)
            rows = {}
            for name, pgx in (("receipt", page), ("read", rpage)):
                ms = [ink.measure([w["quad"] for w in l["words"]], [padded(l["quad"])]) for l in pgx.lines if l["words"]]
                rows[name] = dict(n=len(pgx.lines), cover=stat(ms, "cover"), clipped=stat(ms, "clipped"),
                                  bleed=stat(ms, "bleed"), edge=stat(ms, "edge"),
                                  fails=sum(1 for m in ms if failed(m)),
                                  h=float(np.median([frame(l["quad"])["h"] for l in pgx.lines])))
            texts_rc = [l["text"] for l in page.lines]
            texts_rd = [l["text"] for l in rpage.lines]
            same_poly = sum(1 for a in page.lines for b in rpage.lines
                            if a["text"] == b["text"] and np.abs(a["quad"] - b["quad"]).max() <= 2)
            rows.update(tag=p["tag"], same_text_lines=len(set(texts_rc) & set(texts_rd)), same_poly_lines=same_poly)
            report["read_vs_receipt"].append(rows)
        # every OCR line on the page (not only items), segment = Azure line polygon
        lm = [ink.measure([w["quad"] for w in l["words"]], [padded(l["quad"])]) for l in page.lines if l["words"]]
        prow = dict(tag=p["tag"], size=f"{W}x{H}", n_lines=len(page.lines), n_items=len(targets), line_h=round(ink.line_h, 1),
                    deg_median=float(np.median([t["deg"] for t in targets])) if targets else None,
                    deg_range=[float(min(t["deg"] for t in targets)), float(max(t["deg"] for t in targets))] if targets else None,
                    shapes={k: dict(n=sum(1 for t in targets if t["m"].get(k)),
                                    cover=stat([t["m"].get(k) for t in targets], "cover"),
                                    clipped=stat([t["m"].get(k) for t in targets], "clipped"),
                                    bleed=stat([t["m"].get(k) for t in targets], "bleed"),
                                    edge=stat([t["m"].get(k) for t in targets], "edge"),
                                    fails=sum(1 for t in targets if k in t["m"] and failed(t["m"][k])))
                            for k in SHAPES},
                    lines=dict(n=len(lm), cover=stat(lm, "cover"), clipped=stat(lm, "clipped"), bleed=stat(lm, "bleed"),
                               edge=stat(lm, "edge"), fails=sum(1 for m in lm if failed(m))),
                    fails=[dict(k=k + 1, name=targets[k]["name"], **{a: round(b, 3) for a, b in targets[k]["m"]["snake"].items()
                                                                     if isinstance(b, float)}) for k in fails],
                    items=[dict(k=k + 1, name=t["name"], source=t["source"], deg=round(t["deg"], 2), n_segs=len(t["segs"]),
                                **{f"{s}_{a}": round(b, 3) for s, m in t["m"].items() if m for a, b in m.items() if isinstance(b, float)})
                           for k, t in enumerate(targets)],
                    match=match, overlay=ovp, overlay_read=ovr)
        report["photos"].append(prow)
        keep[p["tag"]] = (img, targets)
        sh = prow["shapes"]
        print(f"{p['tag']:40s} n={len(targets):2d} deg={prow['deg_median'] or 0:5.1f} " +
              " ".join(f"{k}:{sh[k]['cover']:.2f}/{sh[k]['bleed']:.2f}/f{sh[k]['fails']}" for k in SHAPES if sh[k]["n"]) +
              (f" | match {match['azure']}" if match else ""), flush=True)

    chosen = ["h08_04", "root_02-sklep-u-asi", "h10_05", "h10_09", "h08_20", "h08_11", "h10_02", "pub_biedronka-2020-01-09-cc-by-sa"]
    mock = []
    for tag in chosen:
        img, targets = keep[tag]
        if not targets:
            continue
        picks = sorted({0, len(targets) // 2, len(targets) - 1})
        entry = dict(tag=tag, items=[])
        for k in picks:
            t = targets[k]
            win = np.vstack(t["shapes"]["snake"])
            ims = {}
            for kind in ("marker", "spotlight", "underline", "combo"):
                ims[kind] = dict(crop=save(f"m_{tag}_{k}_{kind}_crop.jpg", crop_render(img, kind, t, win)),
                                 full=save(f"m_{tag}_{k}_{kind}_full.jpg", full_render(img, kind, t)))
            if t["haiku"] is not None:
                ims["today"] = dict(crop=save(f"m_{tag}_{k}_today_crop.jpg", crop_render(img, "today", t, t["haiku"])),
                                    full=save(f"m_{tag}_{k}_today_full.jpg", full_render(img, "today", t)))
            entry["items"].append(dict(k=k + 1, name=t["name"], price=t["price"], deg=t["deg"], source=t["source"],
                                       m={a: b for a, b in t["m"].items() if b}, ims=ims))
        mock.append(entry)
    report["mockups"] = mock
    (OUT / "report.json").write_text(json.dumps(report, ensure_ascii=False, indent=1, default=float))


if __name__ == "__main__":
    main()
