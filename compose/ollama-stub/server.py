"""Ollama-compatible stub for CI: deterministic embeddings and a scripted, streamed chat answer.

Implements only what maf-lab calls through OllamaSharp: /api/version, /api/tags, /api/show, /api/embed, /api/chat.
It also stands in for TypeSafe's Jev at POST /v1/systemone — the endpoint of the api's intent classifier and of the
retrieval server's relevance judge — answering from keyword sets, so forced retrieval and the relevance gate are
exercised without a model or a real key. No model, no network, no secrets. Real model behaviour is covered by the
on-demand evals workflow.
"""
import hashlib
import json
import math
import re
import time
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

DIMENSIONS = {"embeddinggemma": 768}
STOPWORDS = set("a an and are as at be by for from how i in is it of on or the to what when where which who why with".split())
TOKEN = re.compile(r"[a-z0-9]+")


def embed(text: str, dims: int) -> list[float]:
    """Feature hashing of tokens and bigrams, L2-normalised: texts sharing words are close."""
    vec = [0.0] * dims
    tokens = [t for t in TOKEN.findall(text.lower()) if t not in STOPWORDS]
    features = [(t, 1.0) for t in tokens] + [(a + "_" + b, 0.5) for a, b in zip(tokens, tokens[1:])]
    for feature, weight in features:
        h = int.from_bytes(hashlib.md5(feature.encode()).digest()[:4], "little")
        vec[h % dims] += weight if h & 0x80000000 == 0 else -weight
    norm = math.sqrt(sum(v * v for v in vec)) or 1.0
    if norm == 1.0 and not any(vec):
        vec[0] = 1.0
    return [v / norm for v in vec]


def dims_for(model: str) -> int:
    return DIMENSIONS.get(model.split(":")[0], 768)


# English and Bulgarian words the checks rely on. Jev classifies by meaning; a stub has to be deterministic.
INTENT_WORDS = {
    "PROCEDURAL": ("how", "why", "procedure", "process", "steps", "explain", "what is", "what's", "what are",
                   "what does", "define", "definition", "meaning", "policy", "what should", "what to do",
                   "when should", "guide",
                   "как", "защо", "процедура", "процедурата", "обясни", "политика"),
    "CHITCHAT": ("hi", "hello", "thanks", "bye", "здравей", "здрасти", "благодаря", "мерси", "чао"),
    "DATA": ("status", "state of", "which runs", "failed runs", "pending runs", "latest run", "list the runs",
             "статус", "списък", "рънове"),
}
RUN_REFERENCE = re.compile(r"\b(run|рън)\s*#?\s*\d{3,}")
JEV_OPTIONS = ("procedural", "mixed", "data", "chitchat", "other")
OFF_DOMAIN_WORDS = ("cook", "recipe", "carbonara", "passport", "weather", "баница", "паспорт")


def classify(question: str) -> str:
    """One of Jev's option names for the question; unknown questions are "other", as the caller expects."""
    question = question.lower()
    procedural = any(w in question for w in INTENT_WORDS["PROCEDURAL"])
    run = bool(RUN_REFERENCE.search(question))
    if procedural:
        return "mixed" if run else "procedural"
    if run or any(w in question for w in INTENT_WORDS["DATA"]):
        return "data"
    if any(w in question for w in INTENT_WORDS["CHITCHAT"]):
        return "chitchat"
    return "other"


def relevance(body: dict) -> dict:
    """The relevance judge's request: one Noul per passage. Nothing is relevant to a plainly off-domain query, and
    everything is relevant to any other — enough for the gate to silence one and pass the other, deterministically."""
    state = body.get("state") or {}
    query = str(state.get("query", "")).lower()
    p = 0.0 if any(w in query for w in OFF_DOMAIN_WORDS) else 1.0
    answers = {qid: {"type": "noul", "noul": p} for qid in (body.get("questions") or {})}
    return {"model": "jev-stub", "answers": answers, "usage": {"input_tokens": 0, "output_tokens": 0}}


STATUS_OPTIONS = ("pending", "running", "completed", "failed", "none")
WRITE_REQUEST = re.compile(r"\b(credit|reduce|increase|adjust|refund)\b.*\ba-\d+")


def tool_probability(tool: str, question: str) -> float:
    """A routing question: does the question need this tool? The named tool high, the rest low, from keywords."""
    run = bool(RUN_REFERENCE.search(question))
    if tool == "get_billing_run_status":
        return 0.93 if run else 0.1
    if tool == "search_billing_runs":
        return 0.92 if not run and (any(w in question for w in INTENT_WORDS["DATA"]) or "runs" in question) else 0.2
    if tool == "propose_fee_adjustment":
        return 0.8 if WRITE_REQUEST.search(question) else 0.02
    return 0.0


def run_status(question: str) -> str:
    for word, status in (("fail", "failed"), ("pending", "pending"), ("running", "running"), ("complete", "completed"), ("finished", "completed")):
        if word in question:
            return status
    return "none"


def systemone(body: dict) -> dict:
    """An answer in the shape of https://docs.typesafe.ai/api, for every question asked."""
    state = body.get("state") or {}
    if isinstance(state, dict) and "passages" in state:
        return relevance(body)
    question = state.get("user_question", "") if isinstance(state, dict) else str(state)
    choice = classify(question)
    # The domain Noul: a few words that are plainly not fee billing, enough for a check that such a question is not
    # forced. Everything else is in the domain.
    in_domain = 0.0 if any(w in question.lower() for w in OFF_DOMAIN_WORDS) else 1.0
    answers = {}
    lowered = question.lower()
    for qid, q in (body.get("questions") or {}).items():
        if qid == "run_status":
            status = run_status(lowered)
            answers[qid] = {"type": "choice", "choice": status, "confidence": 1.0,
                            "probabilities": {o: 1.0 if o == status else 0.0 for o in STATUS_OPTIONS}}
        elif qid.startswith("tool_"):
            answers[qid] = {"type": "noul", "noul": tool_probability(qid[len("tool_"):], lowered)}
        elif (q or {}).get("type") == "noul":
            answers[qid] = {"type": "noul", "noul": in_domain}
        else:
            answers[qid] = {"type": "choice", "choice": choice, "confidence": 1.0,
                            "probabilities": {o: 1.0 if o == choice else 0.0 for o in JEV_OPTIONS}}
    return {"model": "jev-stub", "answers": answers, "usage": {"input_tokens": 0, "output_tokens": 0}}


TRANSLATOR_MARKER = "maf-lab/query-translator"


def is_translation(messages: list[dict]) -> bool:
    return any(TRANSLATOR_MARKER in (m.get("content") or "") for m in messages)


def translate(messages: list[dict]) -> str:
    """No model here: echo the Latin-script words of the query, which is enough to exercise the path."""
    query = next((m.get("content", "") for m in reversed(messages) if m.get("role") == "user"), "")
    query = query.replace("<query>", " ").replace("</query>", " ")
    kept = [w for w in query.split() if all(ord(c) < 128 for c in w)]
    return " ".join(kept) or "billing"


def answer_for(messages: list[dict]) -> str:
    """Quote the first source from a tool_data block, if the conversation has one."""
    for message in messages:
        content = message.get("content") or ""
        if "<tool_data" in content:
            match = re.search(r'"sectionPath"\s*:\s*"([^"]+)"', content)
            snippet = re.search(r'"snippet"\s*:\s*"([^"]{0,160})', content)
            if match:
                quoted = snippet.group(1) if snippet else ""
                return f"Stub answer for CI. Per {match.group(1)}: {quoted}"
    question = next((m.get("content", "") for m in reversed(messages) if m.get("role") == "user"), "")
    return f"Stub answer for CI (no tools used) to: {question[:80]}"


def now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, fmt, *args):  # quiet: one line per request
        print(f"{self.command} {self.path} -> {args[1] if len(args) > 1 else ''}", flush=True)

    def _json(self, status: int, body: dict):
        data = json.dumps(body).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def _body(self) -> dict:
        length = int(self.headers.get("Content-Length") or 0)
        return json.loads(self.rfile.read(length) or b"{}")

    def do_GET(self):
        if self.path == "/api/version":
            return self._json(200, {"version": "0.0.0-stub"})
        if self.path == "/api/tags":
            return self._json(200, {"models": [{"name": m, "model": m} for m in (*DIMENSIONS, "stub")]})
        if self.path in ("/", "/healthz"):
            return self._json(200, {"status": "ok"})
        return self._json(404, {"error": "not found"})

    def do_POST(self):
        body = self._body()
        model = body.get("model", "stub")
        if self.path == "/v1/systemone":
            # Like the real endpoint, no bearer token is a 401 — so CI exercises the header, with a non-secret value.
            auth = self.headers.get("Authorization") or ""
            if not auth.startswith("Bearer ") or not auth[7:].strip():
                return self._json(401, {"detail": "missing API key"})
            return self._json(200, systemone(body))
        if self.path == "/api/show":
            return self._json(200, {"modelfile": "", "details": {"family": "stub"}, "capabilities": ["completion", "tools", "embedding"]})
        if self.path in ("/api/embed", "/api/embeddings"):
            inputs = body.get("input", body.get("prompt", ""))
            inputs = inputs if isinstance(inputs, list) else [inputs]
            vectors = [embed(text, dims_for(model)) for text in inputs]
            if self.path == "/api/embeddings":
                return self._json(200, {"embedding": vectors[0]})
            return self._json(200, {"model": model, "embeddings": vectors, "total_duration": 1, "prompt_eval_count": len(inputs)})
        if self.path == "/api/chat":
            messages = body.get("messages", [])
            if is_translation(messages):
                text = translate(messages)
            else:
                text = answer_for(messages)
            if not body.get("stream", True):
                return self._json(200, {"model": model, "created_at": now(), "done": True, "done_reason": "stop",
                                        "message": {"role": "assistant", "content": text}, "eval_count": len(text.split())})
            return self._stream_chat(model, text)
        return self._json(404, {"error": "not found"})

    def _stream_chat(self, model: str, text: str):
        self.send_response(200)
        self.send_header("Content-Type", "application/x-ndjson")
        self.send_header("Transfer-Encoding", "chunked")
        self.end_headers()
        words = text.split(" ")
        chunks = [" ".join(words[i:i + 4]) + (" " if i + 4 < len(words) else "") for i in range(0, len(words), 4)]
        for chunk in chunks:
            self._chunk({"model": model, "created_at": now(), "message": {"role": "assistant", "content": chunk}, "done": False})
            time.sleep(0.05)  # stream like a real model so SSE relaying is observable
        self._chunk({"model": model, "created_at": now(), "message": {"role": "assistant", "content": ""}, "done": True,
                     "done_reason": "stop", "total_duration": 1, "eval_count": len(words), "prompt_eval_count": 1})
        self.wfile.write(b"0\r\n\r\n")

    def _chunk(self, obj: dict):
        data = (json.dumps(obj) + "\n").encode()
        self.wfile.write(f"{len(data):x}\r\n".encode() + data + b"\r\n")
        self.wfile.flush()


if __name__ == "__main__":
    print("ollama-stub listening on :11434", flush=True)
    ThreadingHTTPServer(("0.0.0.0", 11434), Handler).serve_forever()
