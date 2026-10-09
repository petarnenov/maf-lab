// AG-UI conformance of this system's agents (agui-protocol-only), driven by nothing but the protocol's own client:
// a bare HttpAgent that knows no more about these agents than their URLs. Each agent is driven directly on the api and
// through CopilotKit's runtime, the way the browser reaches it.
//
// Every run must start with RUN_STARTED, end with exactly one terminal event and nothing after it, and carry only the
// protocol's own event types — never CUSTOM. Also: a run that pauses for a person is resumed (declined, so nothing is
// changed), and a run stopped by its client ends on the api within seconds.
//
// Usage: node copilot-runtime/conformance.mjs [base_url]   (default http://localhost:7171; the stack must be up)
// MODEL_FREE=1 (CI's stub model, which calls no tool) skips the interrupt check: only a real model proposes a write.
import { EventType, HttpAgent } from '@ag-ui/client';

const base = process.argv[2] ?? 'http://localhost:7171';
const official = new Set(Object.values(EventType));
const checks = [];
const check = (name, run) => checks.push({ name, run });
const failures = [];

// Ctrl+C and SIGTERM (stop-anything): every run in flight is stopped the protocol's own way — the client aborts it,
// which ends its request, and the agent stops — then the check says so and exits.
const inFlight = new Set();
for (const signal of ['SIGINT', 'SIGTERM']) {
  process.once(signal, () => {
    for (const agent of inFlight) agent.abortRun();
    console.error(`Cancelled with ${inFlight.size} run(s) in flight, stopped; nothing was changed. Run \`make verify\` again.`);
    process.exit(130);
  });
}

const token = async (userId, tenantId, role) =>
  (
    await (
      await fetch(`${base}/dev/token`, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ userId, tenantId, role }),
      })
    ).json()
  ).token;

const adam = { Authorization: `Bearer ${await token('adam', 'firm-a', 'USER')}` };
const thread = () => `c_${crypto.randomUUID().replaceAll('-', '')}`;

// What is in use, asked once (introduce-plugins 7.1): a plugin's checks run only while /api/plugins lists it, and what a
// chat turn must do depends on whether any domain is in use (`make core` has none, and every turn declines).
const inUseAnswer = await (await fetch(`${base}/api/plugins`, { headers: adam })).json().catch(() => ({ plugins: [], domains: [] }));
const domainInUse = (inUseAnswer.domains ?? []).length > 0;
// A plugin's agent is checked only while the plugin is in use (introduce-plugins task 2.4): still core (no plugins/<name>/
// folder yet), or listed by /api/plugins.
const inUse = async (name) => (inUseAnswer.plugins ?? []).some((p) => p.name === name);
const monitorInUse = await inUse('monitor');
const routes = {
  chat: { direct: `${base}/api/chat`, runtime: `${base}/copilotkit/agent/chat/run` },
};

/** One run with a bare client; the events in the order they came. */
async function run(url, { threadId, message, resume, onEvent } = {}) {
  const agent = new HttpAgent({ url, headers: adam, threadId });
  if (message) agent.addMessage({ id: `u_${Date.now()}`, role: 'user', content: message });
  const events = [];
  const runId = `r_${crypto.randomUUID().replaceAll('-', '').slice(0, 16)}`;
  inFlight.add(agent);
  try {
    await agent.runAgent(
      { runId, ...(resume ? { resume } : {}) },
      {
        onEvent: ({ event }) => {
          events.push(event);
          onEvent?.(event, agent);
        },
      },
    );
  } finally {
    inFlight.delete(agent);
  }
  return { events, runId };
}

/** What every run must be, whatever agent it is. */
function wellFormed(events) {
  const types = events.map((e) => e.type);
  const unknown = types.filter((t) => !official.has(t));
  const terminal = types.filter((t) => t === 'RUN_FINISHED' || t === 'RUN_ERROR');
  if (types[0] !== 'RUN_STARTED') throw new Error(`starts with ${types[0]}`);
  if (types.includes('CUSTOM')) throw new Error('carries a CUSTOM event');
  if (unknown.length > 0) throw new Error(`carries event types the protocol does not define: ${unknown}`);
  if (terminal.length !== 1 || !['RUN_FINISHED', 'RUN_ERROR'].includes(types.at(-1))) {
    throw new Error(`ends with ${terminal.length} terminal event(s), last ${types.at(-1)}`);
  }
}

for (const via of ['direct', 'runtime']) {
  check(`chat (${via}): a question is one well-formed run`, async () => {
    const { events } = await run(routes.chat[via], {
      threadId: thread(),
      message: 'What is the procedure when a fee schedule is missing?',
    });
    wellFormed(events);
    if (domainInUse) {
      if (!events.some((e) => e.type === 'TOOL_CALL_RESULT')) throw new Error('no tool result');
      return undefined;
    }
    // No domain in use: the fixed reply, and no tool call.
    if (events.some((e) => e.type.startsWith('TOOL_CALL_'))) throw new Error('a tool was called with no domain in use');
    const said = events.filter((e) => e.type === 'TEXT_MESSAGE_CONTENT').map((e) => e.delta).join('');
    if (!/^No domain is enabled/.test(said)) throw new Error(`the answer said: ${said.slice(0, 80)}`);
    return 'no domain in use: the fixed reply';
  });

  check(`chat (${via}): a write pauses on an interrupt, and the answer resumes it`, async () => {
    if (process.env.MODEL_FREE === '1') return 'skipped: the stub model proposes no write';
    if (!domainInUse) return 'skipped: no domain is in use, so nothing proposes a write';
    const threadId = thread();
    const asked = await run(routes.chat[via], {
      threadId,
      message: 'adjust the fee on A-1042 down by 200 because the client was overcharged in Q2',
    });
    wellFormed(asked.events);
    const interrupt = asked.events.at(-1).outcome?.interrupts?.[0];
    if (asked.events.at(-1).outcome?.type !== 'interrupt' || !interrupt) throw new Error('did not pause on an interrupt');
    // Declined: the check changes nothing.
    const answered = await run(routes.chat[via], {
      threadId,
      resume: [{ interruptId: interrupt.id, status: 'resolved', payload: { approve: false } }],
    });
    wellFormed(answered.events);
    const said = answered.events.filter((e) => e.type === 'TEXT_MESSAGE_CONTENT').map((e) => e.delta).join('');
    if (!/nothing was applied/i.test(said)) throw new Error(`the answer said: ${said.slice(0, 80)}`);
  });

  check(`chat (${via}): a run its client stops ends on the api within seconds`, async () => {
    if (!domainInUse) return 'skipped: no domain is in use, so a turn ends before any step there is to stop';
    // The run's end is read from the monitor's live trace; the core's own rejoin replays what a run said, not whether it
    // has ended, so this core property is checked only while the monitor is in use.
    if (!monitorInUse) return 'skipped: the monitor plugin, whose trace says the run ended, is not in use';
    let stoppedAt = 0;
    const { runId } = await run(routes.chat[via], {
      threadId: thread(),
      message: 'What is the procedure when a fee schedule is missing, in detail?',
      onEvent: (event, agent) => {
        if (event.type === 'STEP_STARTED' && !stoppedAt) {
          stoppedAt = Date.now();
          agent.abortRun();
        }
      },
    }).catch(() => ({ runId: undefined }));
    if (!stoppedAt) throw new Error('the run never started a step');
    for (let i = 0; i < 50; i++) {
      const trace = await fetch(`${base}/api/runs/${runId ?? 'none'}/trace`, { headers: adam });
      if (trace.ok && (await trace.json()).ended) return;
      await new Promise((r) => setTimeout(r, 100));
    }
    throw new Error('the run did not end within 5 s of the stop');
  });
}

// A plugin's own protocol checks leave with its folder; only installed plugins register them.
const { readdirSync, existsSync } = await import('node:fs');
const pluginRoot = new URL('../plugins/', import.meta.url);
if (existsSync(pluginRoot)) {
  for (const entry of readdirSync(pluginRoot, { withFileTypes: true })) {
    if (!entry.isDirectory() || !await inUse(entry.name)) continue;
    const source = new URL(`${entry.name}/tests/conformance.mjs`, pluginRoot);
    if (!existsSync(source)) continue;
    const { registerChecks } = await import(source.href);
    await registerChecks({ check, run, wellFormed, base, headers: adam });
  }
}

const width = 24;
for (const [i, { name, run: body }] of checks.entries()) {
  const done = Math.round(((i + 1) / checks.length) * width);
  try {
    const note = await body();
    console.log(`[${'#'.repeat(done)}${'-'.repeat(width - done)}] ${i + 1}/${checks.length} PASS  ${name}${note ? `  — ${note}` : ''}`);
  } catch (error) {
    failures.push(name);
    console.log(`[${'#'.repeat(done)}${'-'.repeat(width - done)}] ${i + 1}/${checks.length} FAIL  ${name}  — ${error.message ?? error}`);
  }
}
console.log(failures.length === 0 ? `all ${checks.length} AG-UI conformance checks passed` : `${failures.length} failed`);
process.exit(failures.length === 0 ? 0 : 1);
