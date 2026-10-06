#!/usr/bin/env python3
"""Salvage truncated JSON replies.

When a reply hit the num_predict cap mid-object the JSON is unterminated and the
main driver records a parse error. That would score as "produced nothing", which
overstates the failure: the model did emit usable items before it was cut off.
This pass re-reads every out/*.json with a parse error, closes the dangling
structure, and rewrites the record with parsed data plus a "truncated" flag.
"""
import json
import pathlib

OUT = pathlib.Path(__file__).resolve().parent / "out"


def close_json(text):
    """Trim to the last complete array element / object member, then close brackets."""
    start = text.find("{")
    if start < 0:
        return None
    s = text[start:]
    # Walk the string tracking structure, remembering the last position where the
    # document was at a "safe" boundary (just after a completed value inside a container).
    stack, in_str, esc = [], False, False
    safe = None
    for i, c in enumerate(s):
        if in_str:
            if esc:
                esc = False
            elif c == "\\":
                esc = True
            elif c == '"':
                in_str = False
                if stack and stack[-1] == "[":
                    safe = i + 1
            continue
        if c == '"':
            in_str = True
        elif c in "{[":
            stack.append(c)
        elif c in "}]":
            if stack:
                stack.pop()
            if stack and stack[-1] == "[":
                safe = i + 1
        elif c == "," and stack:
            safe = i  # before the comma: everything up to here is complete

    for cut in (len(s), safe):
        if cut is None:
            continue
        head = s[:cut].rstrip().rstrip(",")
        # recompute what still needs closing
        st, ins, es = [], False, False
        for c in head:
            if ins:
                if es:
                    es = False
                elif c == "\\":
                    es = True
                elif c == '"':
                    ins = False
                continue
            if c == '"':
                ins = True
            elif c in "{[":
                st.append(c)
            elif c in "}]" and st:
                st.pop()
        if ins:
            continue
        candidate = head + "".join("}" if b == "{" else "]" for b in reversed(st))
        try:
            return json.loads(candidate)
        except Exception:
            continue
    return None


fixed = 0
for p in sorted(OUT.glob("*_[AB].json")):
    rec = json.loads(p.read_text())
    if not rec.get("error") or rec.get("parsed"):
        continue
    obj = close_json(rec.get("raw") or "")
    if obj is None:
        print(f"{p.name}: could not salvage")
        continue
    rec["parsed"] = obj
    rec["truncated"] = True
    rec["error"] = rec["error"] + " (salvaged by closing truncated JSON)"
    p.write_text(json.dumps(rec, ensure_ascii=False, indent=2))
    print(f"{p.name}: salvaged, items={len(obj.get('items', []))} total={obj.get('total')}")
    fixed += 1

print(f"salvaged {fixed} record(s)")
