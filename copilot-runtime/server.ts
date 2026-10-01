// CopilotKit's runtime, wiring only (agui-protocol-only, DECISIONS §74): the browser's CopilotKit reaches the api's
// agents through here, the one supported way CopilotKit OSS talks to an agent it does not host. Each agent is an
// AG-UI HttpAgent pointed at the api's MapAGUIServer endpoint, made per request so the caller's bearer token goes with
// it — the api resolves the principal and the tenant itself; this service never reads the token. No tools, prompts,
// middleware, memory or logging of content belong here.
//
// The runtime keeps each thread's events in memory with no owner, and by default serves them to anyone: listing
// threads, reading their messages and events, and reconnecting to them. That would let one firm read another's
// conversation. So only what the web uses is served — the runtime's info, a run of one of the two agents, and a stop —
// and a thread may be stopped only by the credentials that ran it. Everything an agent says still comes from the api,
// which checks who is asking on every run.
import { createHash } from 'node:crypto';
import { createServer, type IncomingMessage, type ServerResponse } from 'node:http';
import { Readable } from 'node:stream';
import type { ReadableStream as WebReadableStream } from 'node:stream/web';
import { HttpAgent } from '@ag-ui/client';
import { CopilotRuntime, InMemoryAgentRunner, createCopilotRuntimeHandler } from '@copilotkit/runtime/v2';

const api = process.env.AGENTS_BASE_URL ?? 'http://lb';
const port = Number(process.env.PORT ?? 8080);
const basePath = '/copilotkit';
const agents = ['chat', 'testgen'] as const;

const runtime = new CopilotRuntime({
  agents: ({ request }) => {
    const authorization = request.headers.get('authorization');
    const headers: Record<string, string> = authorization ? { Authorization: authorization } : {};
    return {
      chat: new HttpAgent({ url: `${api}/api/chat`, headers }),
      testgen: new HttpAgent({ url: `${api}/api/coverage/runs/agent`, headers }),
    };
  },
  // A new message while the previous run is still going replaces that run, as it did before the runtime existed.
  runner: new InMemoryAgentRunner({ onConcurrentRun: 'supersede' }),
});

const copilotkit = createCopilotRuntimeHandler({ runtime, basePath });

/** Who ran each thread here: a digest of the credentials, so only they may stop it. */
const runBy = new Map<string, string>();
const MAX_THREADS = 10_000;

const caller = (request: IncomingMessage) =>
  createHash('sha256').update(String(request.headers.authorization ?? '')).digest('hex');

async function bodyOf(request: IncomingMessage): Promise<Buffer> {
  const chunks: Buffer[] = [];
  for await (const chunk of request) chunks.push(chunk as Buffer);
  return Buffer.concat(chunks);
}

const threadOf = (body: Buffer): string | undefined => {
  try {
    return (JSON.parse(body.toString('utf8')) as { threadId?: string }).threadId;
  } catch {
    return undefined;
  }
};

/** Hands a request to the runtime as a fetch Request, and its streamed answer back as it comes. */
async function serve(request: IncomingMessage, response: ServerResponse, body?: Buffer, stopOnClose?: string) {
  const headers = new Headers();
  for (const [name, value] of Object.entries(request.headers)) {
    if (typeof value === 'string') headers.set(name, value);
    else if (Array.isArray(value)) value.forEach((v) => headers.append(name, v));
  }
  const abort = new AbortController();
  response.on('close', () => {
    abort.abort();
    // The browser walked away before the run ended: the run stops, as it did before the runtime existed. The runtime
    // keeps runs going for reconnecting clients; this system rejoins through the api instead.
    if (!response.writableFinished && stopOnClose) {
      void copilotkit(new Request(`http://copilot-runtime${stopOnClose}`, { method: 'POST', headers })).catch(() => {});
    }
  });
  const answer = await copilotkit(
    new Request(`http://${request.headers.host ?? 'copilot-runtime'}${request.url ?? '/'}`, {
      method: request.method,
      headers,
      body: body && body.length > 0 ? new Uint8Array(body) : undefined,
      signal: abort.signal,
    }),
  );
  response.writeHead(answer.status, Object.fromEntries(answer.headers.entries()));
  if (!answer.body) {
    response.end();
    return;
  }
  Readable.fromWeb(answer.body as WebReadableStream<Uint8Array>).pipe(response);
}

const agentRoute = new RegExp(`^${basePath}/agent/(${agents.join('|')})/(run|stop/([^/?]+))$`);

createServer(async (request, response) => {
  const path = (request.url ?? '').split('?')[0];
  if (path === '/health') {
    response.writeHead(200, { 'content-type': 'application/json' });
    response.end('{"status":"ok"}');
    return;
  }
  const route = agentRoute.exec(path);
  const allowed =
    (request.method === 'GET' && path === `${basePath}/info`) || (request.method === 'POST' && route !== null);
  if (!allowed) {
    response.writeHead(404, { 'content-type': 'application/json' });
    response.end('{"error":"not found"}');
    return;
  }
  if (route?.[3]) {
    const threadId = decodeURIComponent(route[3]);
    if (runBy.get(threadId) !== caller(request)) {
      response.writeHead(404, { 'content-type': 'application/json' });
      response.end('{"error":"not found"}');
      return;
    }
  }
  const body = request.method === 'POST' ? await bodyOf(request) : undefined;
  const threadId = route && !route[3] && body ? threadOf(body) : undefined;
  if (threadId) {
    if (runBy.size >= MAX_THREADS) runBy.delete(runBy.keys().next().value!);
    runBy.set(threadId, caller(request));
  }
  try {
    await serve(request, response, body, threadId && route ? `${basePath}/agent/${route[1]}/stop/${encodeURIComponent(threadId)}` : undefined);
  } catch {
    if (!response.headersSent) response.writeHead(502, { 'content-type': 'application/json' });
    response.end();
  }
}).listen(port, () => console.log(`copilot-runtime on :${port}, agents at ${api}`));
