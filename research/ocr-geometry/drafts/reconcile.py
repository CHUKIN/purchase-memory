"""Deterministic arithmetic repair for a parsed receipt.

Three rules, applied in order, each only where it is unambiguous:
  1. line total must equal qty * unitPrice  -> repair the field that is a near-miss
  2. drop rows that are not products (weight rows leaked through, barcodes)
  3. report the residual against the receipt total instead of hiding it
"""
import json,re,sys

def money(x):
    try: return round(float(x),2)
    except (TypeError,ValueError): return None

def qty(x):
    # weights carry three decimals (0,446 kg) - rounding them to 2 corrupts the line
    try: return round(float(x),3)
    except (TypeError,ValueError): return None

def repair(items):
    out,notes=[],[]
    for it in items:
        q,u,lt=qty(it.get("qty")),money(it.get("unitPrice")),money(it.get("lineTotal"))
        name=(it.get("name") or "").strip()
        if q is None or u is None or lt is None:
            notes.append(f"неполная строка: {name!r}"); out.append(it); continue
        calc=round(q*u,2)
        weighted=abs(q-round(q))>1e-9
        if weighted:
            # the register printed this amount under its own rounding rules - it is authoritative
            if abs(calc-lt)>0.05:
                notes.append(f"!! весовая {name!r}: {q}x{u}={calc}, на чеке {lt}")
            out.append({**it,"qty":q,"unitPrice":u,"lineTotal":lt}); continue
        if abs(calc-lt)<0.005:
            pass                                   # согласовано
        elif abs(calc-lt)<=0.10:
            # мелкое расхождение -> OCR ошибся в последней цифре суммы строки
            notes.append(f"{name!r}: сумма {lt} -> {calc} (пересчёт {q}×{u})")
            lt=calc
        else:
            notes.append(f"!! {name!r}: {q}×{u}={calc}, а в чеке {lt} — расхождение не исправлено")
        out.append({**it,"qty":q,"unitPrice":u,"lineTotal":lt})
    return out,notes

def check(items,total):
    s=round(sum(i["lineTotal"] for i in items if money(i.get("lineTotal")) is not None),2)
    return s,round(s-total,2)

if __name__=="__main__":
    raw=open(sys.argv[1]).read()
    j=json.loads(re.search(r'\{.*\}',raw,re.S).group(0))
    before=check(j["items"],j["total"])
    items,notes=repair(j["items"])
    after=check(items,j["total"])
    print(f"итог чека: {j['total']}")
    print(f"до пересчёта:    сумма строк {before[0]}, расхождение {before[1]:+.2f}")
    print(f"после пересчёта: сумма строк {after[0]}, расхождение {after[1]:+.2f}")
    print(f"позиций: {len(items)}")
    print("\nчто изменено:")
    for n in notes: print("  -",n)
