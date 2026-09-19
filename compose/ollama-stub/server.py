"""Ollama-compatible stub for CI: deterministic embeddings and a scripted, streamed chat answer.

Implements only what maf-lab calls through OllamaSharp: /api/version, /api/tags, /api/show, /api/embed, /api/chat.
It also answers the intent classifier (recognised by its marker) with a single label, so the second classification
stage is exercised without a model. No model, no network, no secrets. Real model behaviour is covered by the
on-demand evals workflow.
"""
import hashlib
import json
import math
import re
import time
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

DIMENSIONS = {"nomic-embed-text": 768, "all-minilm": 384}
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


INTENT_MARKER = "maf-lab/intent-classifier"
# The same words the rules use, plus the Bulgarian ones the checks rely on. A real model classifies by meaning.
INTENT_WORDS = {
    "PROCEDURAL": ("how", "why", "procedure", "what is", "explain", "policy",
                   "как", "защо", "процедура", "процедурата", "обясни", "политика"),
    "CHITCHAT": ("hi", "hello", "thanks", "bye", "здравей", "здрасти", "благодаря", "мерси", "чао"),
    "DATA": ("status", "which runs", "failed runs", "latest run", "статус", "списък", "рънове"),
}
RUN_REFERENCE = re.compile(r"\b(run|рън)\s*#?\s*\d{3,}")


def classify(messages: list[dict]) -> str:
    """One label for the classifier's request; unknown questions are OTHER, as the caller expects."""
    question = next((m.get("content", "") for m in reversed(messages) if m.get("role") == "user"), "").lower()
    procedural = any(w in question for w in INTENT_WORDS["PROCEDURAL"])
    run = bool(RUN_REFERENCE.search(question))
    if procedural:
        return "MIXED" if run else "PROCEDURAL"
    if run or any(w in question for w in INTENT_WORDS["DATA"]):
        return "DATA"
    if any(w in question for w in INTENT_WORDS["CHITCHAT"]):
        return "CHITCHAT"
    return "OTHER"


TRANSLATOR_MARKER = "maf-lab/query-translator"


def is_classification(messages: list[dict]) -> bool:
    return any(INTENT_MARKER in (m.get("content") or "") for m in messages)


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
            if is_classification(messages):
                text = classify(messages)
            elif is_translation(messages):
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
