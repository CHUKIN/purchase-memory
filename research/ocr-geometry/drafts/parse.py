import os,json,urllib.request,time,re,sys
SP=os.environ.get("OCR_GEOM_DIR", os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "fixtures"))
model,n=sys.argv[1],sys.argv[2]
rows=open(f"{SP}/rows_{n}.json").read()
prompt=f"""ROWS below are OCR lines from a Polish supermarket receipt, already grouped by
vertical position. Each entry is {{"y": vertical position 0..1000, "cells": [text pieces
left to right]}}. A cell like "1 x8,99 8,99C" or "0,270 x63,99 17,28C" is
quantity x unitPrice lineTotal. Cells that are only long digit strings are barcodes - ignore them.

Rows are already deskewed: each product row holds exactly one product and its price cell.
EXCEPTION: weighted goods print on TWO rows - the product name row has NO price cell, and
the NEXT row holds only the weight cell like "0,270 x63,99 17,28C". Merge those two rows
into ONE item. Do not emit the weight row as a separate item, and never duplicate a product.

Return ONLY JSON:
{{"merchant":str,"date":str,"total":number,
  "items":[{{"name":str,"qty":number,"unitPrice":number,"lineTotal":number}}]}}
qty is the weight in kg for weighted goods. Include OPUST discounts with negative lineTotal.
Keep Polish names exactly as OCR read them, with diacritics. Every product line must appear once.

ROWS = {rows}"""
body=json.dumps({"model":model,"stream":False,"think":False,
 "options":{"temperature":0,"num_ctx":32768,"num_predict":16384},
 "messages":[{"role":"user","content":prompt}]}).encode()
t=time.time()
r=urllib.request.urlopen(urllib.request.Request("http://127.0.0.1:11434/api/chat",body,{"Content-Type":"application/json"}),timeout=1800)
c=json.load(r)["message"]["content"]; el=time.time()-t
open(f"{SP}/rows_{model.split(':')[0]}_{n}.txt","w").write(c)
j=json.loads(re.search(r'\{.*\}',c,re.S).group(0))
s=sum(float(i.get("lineTotal") or 0) for i in j["items"])
print(f"{model} чек{n}: {el:.0f}s | итог={j.get('total')} | позиций={len(j['items'])} | сумма строк={round(s,2)}")
bad=[i for i in j["items"] if not i.get("lineTotal")]
print("нулевых строк:",len(bad))
for i in j["items"][:8]: print("   ",i)

