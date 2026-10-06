import os,json,re,sys
SP=os.environ.get("OCR_GEOM_DIR", os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "fixtures"))
n=sys.argv[1]
F=json.load(open(f"{SP}/frag_{n}.json"))
price=re.compile(r'x\s?\d+[.,]\s?\d')
hs=sorted(f['h'] for f in F); med=hs[len(hs)//2]; tol=med*0.6
def cluster(a):
    pts=sorted(({'y':f['y']+f['h']/2-a*f['x']/1000.0,'f':f} for f in F),key=lambda p:p['y'])
    rows=[]
    for p in pts:
        if rows and abs(rows[-1]['anchor']-p['y'])<=tol:
            r=rows[-1]; r['anchor']=(r['anchor']*r['n']+p['y'])/(r['n']+1); r['n']+=1; r['fr'].append(p['f'])
        else: rows.append({'anchor':p['y'],'n':1,'fr':[p['f']]})
    return rows
# Search a wide angle range, but on a tie prefer the SMALLEST correction: a flat
# objective (few price cells on a short receipt) must not push us to the boundary.
cand=[]
for a in [x/2 for x in range(-120,121)]:
    rows=cluster(a)
    o=sum(1 for r in rows if sum(1 for f in r['fr'] if price.search(f['t']))==1)
    m=sum(1 for r in rows if sum(1 for f in r['fr'] if price.search(f['t']))>1)
    cand.append((o-2*m,-abs(a),a,rows))
top=max(c[0] for c in cand)
best=max((c for c in cand if c[0]==top),key=lambda c:c[1])
a,rows=best[2],best[3]
if abs(a)>=59.5: print(f"ВНИМАНИЕ: наклон уперся в границу поиска (a={a})",file=sys.stderr)
out=[{"y":round(r['anchor']),"cells":[f['t'] for f in sorted(r['fr'],key=lambda f:f['x'])]} for r in rows]
json.dump(out,open(f"{SP}/rows_{n}.json","w"),ensure_ascii=False)
print(f"наклон a={a}, строк={len(out)}",file=sys.stderr)
