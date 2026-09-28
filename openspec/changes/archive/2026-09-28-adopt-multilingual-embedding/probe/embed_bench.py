"""Offline dense-only recall@5 per language, tenant-scoped like search: EN, BG (Cyrillic), BG in Latin letters."""
import json, math, sys, time, urllib.request, os, pickle
QDRANT="http://localhost:6333"; OLLAMA="http://localhost:11435"; S=os.path.dirname(os.path.abspath(__file__))
PROFILES={
 "nomic-embed-text":        ("search_document: ", "search_query: "),
 "nomic-embed-text-v2-moe": ("search_document: ", "search_query: "),
 "bge-m3":                  ("", ""),
 "qwen3-embedding:0.6b":    ("", "Instruct: Given a question, retrieve documentation passages that answer it\nQuery: "),
 "embeddinggemma":          ("title: none | text: ", "task: search result | query: "),
}
TR=dict(zip("абвгдежзийклмнопрстуфхцчшщъьюя",["a","b","v","g","d","e","j","z","i","i","k","l","m","n","o","p","r","s","t","u","f","h","c","ch","sh","sht","a","","iu","q"]))
def latin(s): return "".join((TR.get(ch.lower(),ch).capitalize() if ch.isupper() and ch.lower() in TR else TR.get(ch,ch)) for ch in s)
def post(url, body):
    return json.load(urllib.request.urlopen(urllib.request.Request(url, json.dumps(body).encode(), {"content-type":"application/json"}), timeout=600))
def chunks():
    p=f"{S}/chunks.pkl"
    if os.path.exists(p): return pickle.load(open(p,"rb"))
    out, off = [], None
    while True:
        r=post(f"{QDRANT}/collections/maf_chunks/points/scroll",{"limit":500,"with_payload":["chunk_id","tenant_id","section_path","text"],"with_vector":False,**({"offset":off} if off else {})})["result"]
        out += [p["payload"] for p in r["points"]]; off=r.get("next_page_offset")
        if not off: break
    pickle.dump(out,open(p,"wb")); return out
def embed(model, texts, bs=32):
    vecs=[]
    for i in range(0,len(texts),bs):
        vecs += post(f"{OLLAMA}/api/embed",{"model":model,"input":texts[i:i+bs],"truncate":True})["embeddings"]
    return [normalize(v) for v in vecs]
def normalize(v):
    n=math.sqrt(sum(x*x for x in v)) or 1; return [x/n for x in v]
def run(model):
    dp,qp=PROFILES[model]; C=chunks()
    cache=f"{S}/emb-{model.replace(':','_')}.pkl"
    t=time.time()
    if os.path.exists(cache): D=pickle.load(open(cache,"rb"))
    else:
        D=embed(model,[dp+c["section_path"]+"\n"+c["text"] for c in C]); pickle.dump(D,open(cache,"wb"))
    doc_s=time.time()-t
    cases=[json.loads(l) for l in open("evals/retrieval.jsonl")]
    cases=[c for c in cases if not c.get("offDomain")]
    qs=[]
    for c in cases:
        lang=c.get("language","en"); qs.append((lang,c["query"],c))
        if lang=="bg": qs.append(("bg-latn",latin(c["query"]),c))
    t=time.time(); Q=embed(model,[qp+q for _,q,_ in qs]); q_ms=(time.time()-t)*1000/len(qs)
    res={}
    for (lang,q,c),qv in zip(qs,Q):
        scope={c["firmId"],"shared"}
        scored=sorted(((sum(a*b for a,b in zip(qv,dv)),C[i]["chunk_id"]) for i,dv in enumerate(D) if C[i]["tenant_id"] in scope),reverse=True)[:5]
        rel=set(c["relevantChunkIds"]); top={cid for _,cid in scored}
        res.setdefault(lang,[]).append(len(rel&top)/len(rel) if rel else 1)
    out={l:round(sum(v)/len(v),3) for l,v in res.items()}
    print(f"{model:26} dims={len(D[0]):5} recall@5 " + " ".join(f"{l}={out[l]:.3f}" for l in ("en","bg","bg-latn")) + f"   index {doc_s:.0f}s  query {q_ms:.0f}ms", flush=True)
    return out
if __name__=="__main__":
    print("sample latin:", latin("Каква е процедурата, когато липсва фий схедюл?"))
    for m in sys.argv[1:]: run(m)
