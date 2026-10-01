// AG-UI conformance of this system's agents (agui-protocol-only), driven by nothing but the protocol's own client:
// a bare HttpAgent that knows no more about these agents than their URLs. Each agent is driven directly on the api and
// through CopilotKit's runtime, the way the browser reaches it.
//
// Every run must start with RUN_STARTED, end with exactly one terminal event and nothing after it, and carry only the
// protocol's own event types — never CUSTOM. Also: a run that pauses for a person is resumed (declined, so nothing is
// changed), and a run stopped by its client ends on the api within seconds.
//
// Usage: node copilot-runtime/conformance.mjs [base_url]   (default http://localhost:7171; the stack must be up)
import { EventType, HttpAgent } from '@ag-ui/client';

const base = process.argv[2] ?? 'http://localhost:7171';
const official = new Set(Object.values(EventType));
const checks = [];
const check = (name, run) => checks.push({ name, run });
const failures = [];

const token = async (userId, firmId, role) =>
  (
    await (
      await fetch(`${base}/dev/token`, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ userId, firmId, role }),
      })
    ).json()
  ).token;

const adam = { Authorization: `Bearer ${await token('adam', 'firm-a', 'ADVISOR')}` };
const thread = () => `c_${crypto.randomUUID().replaceAll('-', '')}`;
const routes = {
  chat: { direct: `${base}/api/chat`, runtime: `${base}/copilotkit/agent/chat/run` },
  testgen: { direct: `${base}/api/coverage/runs/agent`, runtime: `${base}/copilotkit/agent/testgen/run` },
};

/** One run with a bare client; the events in the order they came. */
async function run(url, { threadId, message, resume, onEvent } = {}) {
  const agent = new HttpAgent({ url, headers: adam, threadId });
  if (message) agent.addMessage({ id: `u_${Date.now()}`, role: 'user', content: message });
  const events = [];
  const runId = `r_${crypto.randomUUID().replaceAll('-', '').slice(0, 16)}`;
  await agent.runAgent(
    { runId, ...(resume ? { resume } : {}) },
    {
      onEvent: ({ event }) => {
        events.push(event);
        onEvent?.(event, agent);
      },
    },
  );
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
    if (!events.some((e) => e.type === 'TOOL_CALL_RESULT')) throw new Error('no tool result');
  });

  check(`chat (${via}): a write pauses on an interrupt, and the answer resumes it`, async () => {
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

const runs = await (await fetch(`${base}/api/coverage/runs`, { headers: adam })).json().catch(() => []);
const followed = Array.isArray(runs) ? runs.find((r) => !r.active) : undefined;
for (const via of ['direct', 'runtime']) {
  check(`testgen (${via}): a finished run replays as one well-formed run`, async () => {
    if (!followed) return 'skipped: no finished test run on this stack';
    const { events } = await run(routes.testgen[via], { threadId: `testgen:${followed.id}:conformance` });
    wellFormed(events);
    if (!events.some((e) => e.type === 'STATE_SNAPSHOT')) throw new Error('no state');
  });
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
