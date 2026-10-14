"""Free hybrid: Haiku's items/totals/discounts + boxes taken from cached Azure output.

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
"""
import copy

from . import azure_receipt, boxes
from .common import read_json

AMOUNT_TOLERANCE = 0.01 + 1e-9
NAME_MIN = 0.6
NAME_WITH_AMOUNT_MIN = 0.3
PRICE_ROW_GAP = 2.5   # price line may start up to this many name-line heights below the name line

STRATEGIES = ["amount", "name", "name-amount", "aligned", "aligned+lines", "aligned+lines+own"]


def amount_ok(a, b):
    return a is not None and b is not None and abs(float(a) - float(b)) <= AMOUNT_TOLERANCE


# ---- Azure side ------------------------------------------------------------------

def azure_lines(response):
    """Azure OCR page lines as [{"text", "box"}] in normalised coordinates."""
    page = azure_receipt._page(response)
    return [{"text": line.get("content") or "",
             "box": boxes.polygon_to_box(line.get("polygon"), page["width"], page["height"])}
            for line in page.get("lines") or [] if line.get("polygon")]


# ---- matching --------------------------------------------------------------------

def pair_score(item, candidate, similar):
    """(name similarity, amount agrees) for a Haiku item vs an Azure item."""
    return similar(item.get("name") or "", candidate.get("name") or ""), \
        amount_ok(item.get("lineTotal"), candidate.get("lineTotal"))


def match_amount(items, candidates, similar=None):
    used, result = set(), {}
    for i, item in enumerate(items):
        j = next((j for j, c in enumerate(candidates)
                  if j not in used and amount_ok(item.get("lineTotal"), c.get("lineTotal"))), None)
        if j is not None:
            used.add(j)
            result[i] = j
    return result


def _greedy(scored):
    """scored: [(score, i, j)] -> {i: j}, best pairs first, one-to-one."""
    used_i, used_j, result = set(), set(), {}
    for _, i, j in sorted(scored, key=lambda s: (-s[0], s[1], s[2])):
        if i not in used_i and j not in used_j:
            used_i.add(i)
            used_j.add(j)
            result[i] = j
    return result


def match_name(items, candidates, similar):
    scored = []
    for i, item in enumerate(items):
        for j, candidate in enumerate(candidates):
            sim, _ = pair_score(item, candidate, similar)
            if sim >= NAME_MIN:
                scored.append((sim, i, j))
    return _greedy(scored)


def match_name_amount(items, candidates, similar):
    scored = []
    for i, item in enumerate(items):
        for j, candidate in enumerate(candidates):
            sim, same = pair_score(item, candidate, similar)
            if sim >= NAME_MIN or (same and sim >= NAME_WITH_AMOUNT_MIN):
                scored.append((sim + (1.0 if same else 0.0), i, j))
    return _greedy(scored)


def match_aligned(items, candidates, similar):
    """Order-preserving alignment maximising the summed pair score (LCS-style DP)."""
    n, m = len(items), len(candidates)
    score = [[None] * m for _ in range(n)]
    for i, item in enumerate(items):
        for j, candidate in enumerate(candidates):
            sim, same = pair_score(item, candidate, similar)
            if sim >= NAME_MIN or same:
                score[i][j] = sim + (1.0 if same else 0.0)
    best = [[0.0] * (m + 1) for _ in range(n + 1)]
    for i in range(n - 1, -1, -1):
        for j in range(m - 1, -1, -1):
            options = [best[i + 1][j], best[i][j + 1]]
            if score[i][j] is not None:
                options.append(score[i][j] + best[i + 1][j + 1])
            best[i][j] = max(options)
    result, i, j = {}, 0, 0
    while i < n and j < m:
        if score[i][j] is not None and abs(best[i][j] - (score[i][j] + best[i + 1][j + 1])) < 1e-9:
            result[i] = j
            i, j = i + 1, j + 1
        elif best[i][j] == best[i + 1][j]:
            i += 1
        else:
            j += 1
    return result


MATCHERS = {"amount": match_amount, "name": match_name, "name-amount": match_name_amount, "aligned": match_aligned}


def line_box(item, lines, used, normalize):
    """Box from Azure page lines for one item, or (None, None). Marks the used line indexes."""
    name = item.get("name") or ""
    best, best_sim = None, NAME_MIN
    for k, line in enumerate(lines):
        if k in used or not line["box"]:
            continue
        sim = boxes.partial_similarity(name, line["text"], normalize)
        if sim > best_sim or (sim == best_sim and best is None and sim >= NAME_MIN):
            best, best_sim = k, sim
    if best is not None:
        chosen = [best]
        if not boxes.price_printed(item.get("lineTotal"), lines[best]["text"]):
            top, height = lines[best]["box"][1], lines[best]["box"][3]
            below = [k for k, line in enumerate(lines)
                     if k not in used and k != best and line["box"]
                     and top < line["box"][1] <= top + PRICE_ROW_GAP * max(height, 0.01)
                     and boxes.price_printed(item.get("lineTotal"), line["text"])]
            if below:
                chosen.append(min(below, key=lambda k: lines[k]["box"][1]))
        used.update(chosen)
        return boxes.union([lines[k]["box"] for k in chosen]), "azure-line"
    for k, line in enumerate(lines):
        if k not in used and line["box"] and boxes.price_printed(item.get("lineTotal"), line["text"]):
            used.add(k)
            return line["box"], "azure-line-price"
    return None, None


def hybridise(haiku_parsed, azure_parsed, lines, strategy, similar, normalize):
    """Copy of haiku_parsed with boxes from Azure; each item gets `boxSource`."""
    parsed = copy.deepcopy(haiku_parsed)
    own_boxes = [item.get("box") for item in parsed["items"]]
    for item in parsed["items"]:
        item["box"], item["boxSource"] = None, None
    parsed["totalBox"] = (azure_parsed or {}).get("totalBox")
    candidates = [c for c in (azure_parsed or {}).get("items") or [] if c.get("box")]
    base = strategy.split("+")[0]
    for i, j in MATCHERS[base](parsed["items"], candidates, similar).items():
        parsed["items"][i]["box"], parsed["items"][i]["boxSource"] = candidates[j]["box"], "azure-item"
    if "lines" in strategy:
        used = set()
        for item in parsed["items"]:
            if item["box"] is None:
                item["box"], item["boxSource"] = line_box(item, lines, used, normalize)
    if strategy.endswith("+own"):
        for item, own in zip(parsed["items"], own_boxes):
            if item["box"] is None and own:
                item["box"], item["boxSource"] = own, "haiku"
    return parsed


def build_runs(haiku_records, out_dir, similar, normalize, strategies=STRATEGIES):
    """{configuration: {receipt id: record}} for every (Haiku run x strategy); reads the Azure cache only."""
    azure_cache = {}
    runs = {}
    for record in haiku_records:
        receipt_id = record["receiptId"]
        if receipt_id not in azure_cache:
            path = out_dir / "cache" / "azure" / f"receipt_{receipt_id}.json"
            response = read_json(path)["response"] if path.exists() else None
            azure_cache[receipt_id] = (azure_receipt.to_parsed(response) if response else None,
                                       azure_lines(response) if response else [])
        azure_parsed, lines = azure_cache[receipt_id]
        haiku_run = record["configuration"].replace("haiku-prod-v3-saved-", "")
        for strategy in strategies:
            configuration = f"hybrid-{strategy}-{haiku_run}"
            new = copy.deepcopy(record)
            new.update(configuration=configuration, source=f"{record['configuration']} + Azure boxes ({strategy})")
            if record.get("parsed"):
                new["parsed"] = hybridise(record["parsed"], azure_parsed, lines, strategy, similar, normalize)
            runs.setdefault(configuration, {})[receipt_id] = new
    return runs


def shift_boxes(records, shift, configuration):
    """Calibration copy of a run: item k gets the box of item k+shift (same receipt, wraps).

    Every box then sits on the wrong line, so the report shows how many wrong
    boxes the box check actually notices.
    """
    shifted = {}
    for receipt_id, record in records.items():
        new = copy.deepcopy(record)
        new["configuration"] = configuration
        items = (new.get("parsed") or {}).get("items") or []
        own = [item.get("box") for item in items]
        for k, item in enumerate(items):
            item["box"] = own[(k + shift) % len(own)]
        shifted[receipt_id] = new
    return shifted
