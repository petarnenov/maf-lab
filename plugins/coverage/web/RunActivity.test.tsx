import { EventType } from '@ag-ui/core';
import { act, fireEvent, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { RunSummary } from './types';
import { jsonResponse, makeSession, renderWithProviders, sse, streamResponse } from '@maf/testing';
import { CoveragePage } from './CoveragePage';
import { detail } from './CoveragePage.test';
import { agentFetch } from '@maf/testing';
import { attemptScope, initialRunStream, reduceRunEvent, type RunStreamState } from './runStream';
import { sampleTree } from './treeModel.test';

const run = (overrides: Partial<RunSummary> = {}): RunSummary => ({
  id: 'r_1',
  path: 'src/Lab/Beta.cs',
  state: 'working',
  reason: null,
  attempt: 1,
  maxAttempts: 5,
  lastPct: 62,
  targetPct: 85,
  model: 'glm-5.3:cloud',
  tokens: 1200,
  costUsd: 0.012,
  branch: null,
  createdAt: '2026-09-30T10:00:00Z',
  updatedAt: '2026-09-30T10:02:05Z',
  active: true,
  phase: 'generating',
  ...overrides,
});

const e = (type: EventType, data: Record<string, unknown> = {}) => ({ type, ...data });

const firstAttempt = {
  attempt: 1,
  before: 62,
  after: 71.4,
  build: 'ok',
  tests: { passed: 3, failed: 1, skipped: 0 },
  errors: ['BetaTests.Zero: expected 0'],
  violations: 0,
};

/** A run's activity as the server writes it: one attempt with a phase, a tool call, some text and its result. */
const attemptOne = [
  sse(EventType.STEP_STARTED, { stepName: 'attempt 1: generating' }),
  sse(EventType.REASONING_START, { messageId: 'reasoning-2' }),
  sse(EventType.REASONING_MESSAGE_START, { messageId: 'reasoning-2', role: 'reasoning' }),
  sse(EventType.REASONING_MESSAGE_CONTENT, { messageId: 'reasoning-2', delta: 'Which lines are uncovered?' }),
  sse(EventType.REASONING_MESSAGE_END, { messageId: 'reasoning-2' }),
  sse(EventType.REASONING_END, { messageId: 'reasoning-2' }),
  sse(EventType.TOOL_CALL_START, { toolCallId: 'tool-3', toolCallName: 'write_file' }),
  sse(EventType.TOOL_CALL_ARGS, { toolCallId: 'tool-3', delta: '{"path":"tests/Lab.Tests/BetaTests.cs"}' }),
  sse(EventType.TOOL_CALL_END, { toolCallId: 'tool-3' }),
  sse(EventType.TOOL_CALL_RESULT, {
    messageId: 'tool-result-3',
    toolCallId: 'tool-3',
    role: 'tool',
    content: '{"outcome":"ok","summary":"created, 214 bytes"}',
  }),
  sse(EventType.TEXT_MESSAGE_START, { messageId: 'text-4', role: 'assistant' }),
  sse(EventType.TEXT_MESSAGE_CONTENT, { messageId: 'text-4', delta: 'Wrote one test ' }),
  sse(EventType.TEXT_MESSAGE_CONTENT, { messageId: 'text-4', delta: 'per branch.' }),
  sse(EventType.TEXT_MESSAGE_END, { messageId: 'text-4' }),
  sse(EventType.STEP_FINISHED, { stepName: 'attempt 1: generating' }),
  sse(EventType.STEP_STARTED, { stepName: 'attempt 1: building' }),
  // An attempt's result is part of the run's state (agui-protocol-only).
  sse(EventType.STATE_SNAPSHOT, { snapshot: { ...run({ phase: 'building' }), attempts: [firstAttempt] } }),
  sse(EventType.STEP_FINISHED, { stepName: 'attempt 1: building' }),
];

const started = sse(EventType.RUN_STARTED, { threadId: 'testgen:r_1', runId: 'r_1' });
const state = (summary: RunSummary) => sse(EventType.STATE_SNAPSHOT, { snapshot: summary });

/** A stream the test writes to as it goes, for what happens after the modal is open. */
function liveStream() {
  const encoder = new TextEncoder();
  let push: (chunk: string) => void = () => {};
  const body = new ReadableStream<Uint8Array>({
    start(controller) {
      push = (chunk) => controller.enqueue(encoder.encode(chunk));
    },
  });
  return {
    response: new Response(body, { status: 200, headers: { 'Content-Type': 'text/event-stream' } }),
    push: (chunks: string[]) => act(async () => chunks.forEach((c) => push(c))),
  };
}

function stubApi(fileRun: RunSummary | null, events: () => Response) {
  const calls: { url: string; method: string }[] = [];
  vi.stubGlobal(
    'fetch',
    agentFetch(vi.fn(async (url: string, init?: RequestInit) => {
      calls.push({ url, method: init?.method ?? 'GET' });
      if (url.startsWith('/api/coverage/tree')) return jsonResponse(sampleTree);
      if (url.startsWith('/api/coverage/files')) return jsonResponse(detail({ run: fileRun }));
      if (url.endsWith('/events')) return events();
      if (url.endsWith('/cancel')) return jsonResponse(run({ state: 'canceled', active: false }));
      if (url.startsWith('/api/coverage/refresh')) return jsonResponse(undefined, 204);
      return jsonResponse({ title: 'Not found' }, 404);
    })),
  );
  return calls;
}

/** Every stream opened, each its own: the run status and the modal both follow the run. */
function liveStreams() {
  const opened: ReturnType<typeof liveStream>[] = [];
  return {
    open: () => {
      const stream = liveStream();
      opened.push(stream);
      return stream.response;
    },
    /** Writes to every stream opened: the run status's and the modal's alike, as the server would. */
    push: async (chunks: string[]) => {
      for (const stream of opened) await stream.push(chunks);
    },
  };
}

const openActivity = async () => {
  await userEvent.click(await screen.findByRole('button', { name: 'Activity' }));
  return screen.findByRole('dialog', { name: /Activity/ });
};

describe('run stream reducer (AG-UI)', () => {
  const fold = (events: Record<string, unknown>[]): RunStreamState =>
    events.reduce<RunStreamState>((s, ev) => reduceRunEvent(s, ev as { type: string }), initialRunStream);

  it('grows a text message by its content events and ends it', () => {
    const s = fold([
      e(EventType.TEXT_MESSAGE_START, { messageId: 'text-1' }),
      e(EventType.TEXT_MESSAGE_CONTENT, { messageId: 'text-1', delta: 'Looking ' }),
      e(EventType.TEXT_MESSAGE_CONTENT, { messageId: 'text-1', delta: 'at it.' }),
    ]);
    expect(s.timeline).toEqual([{ kind: 'text', id: 'text-1', text: 'Looking at it.', done: false }]);
    expect(fold([...[e(EventType.TEXT_MESSAGE_START, { messageId: 'x' })], e(EventType.TEXT_MESSAGE_END, { messageId: 'x' })]).timeline[0]).toMatchObject({ done: true });
  });

  it('joins a tool call’s events by their id', () => {
    const s = fold([
      e(EventType.TOOL_CALL_START, { toolCallId: 't', toolCallName: 'run_tests' }),
      e(EventType.TOOL_CALL_ARGS, { toolCallId: 't', delta: '{"path":null}' }),
      e(EventType.TOOL_CALL_END, { toolCallId: 't' }),
      e(EventType.TOOL_CALL_RESULT, { toolCallId: 't', content: '{"outcome":"refused","summary":"Only test files."}' }),
    ]);
    expect(s.timeline).toEqual([
      { kind: 'tool', id: 't', name: 'run_tests', path: null, outcome: 'refused', summary: 'Only test files.' },
    ]);
  });

  it('notes where the agent resumed after a restart', () => {
    const s = fold([
      e(EventType.STEP_STARTED, { stepName: 'attempt 2: generating' }),
      e(EventType.STEP_FINISHED, { stepName: 'attempt 2: generating' }),
      e(EventType.STATE_SNAPSHOT, { snapshot: { ...run({ attempt: 2 }), resumes: [2] } }),
    ]);
    expect(s.timeline.at(-1)).toMatchObject({
      kind: 'notice',
      text: 'The agent restarted and resumed the run at attempt 2.',
    });
  });

  it('takes the summary from state snapshots and the end from the terminal event', () => {
    const s = fold([
      e(EventType.STATE_SNAPSHOT, { snapshot: run({ attempt: 2, phase: 'building' }) }),
      e(EventType.RUN_ERROR, { code: 'runner_unavailable', message: 'The run failed (runner_unavailable).' }),
    ]);
    expect(s.summary).toMatchObject({ attempt: 2, phase: 'building' });
    expect(s.ended).toEqual({ outcome: 'error', code: 'runner_unavailable', message: 'The run failed (runner_unavailable).' });
  });
});

describe('Run activity', () => {
  it('shows what the agent does as it happens, without a reload', async () => {
    const stream = liveStreams();
    stubApi(run(), stream.open);
    renderWithProviders(<CoveragePage />, { route: '/coverage?file=src%2FLab%2FBeta.cs', session: makeSession('TENANT_ADMIN') });

    const dialog = await openActivity();
    await stream.push([started, state(run()), ...attemptOne.slice(0, 1)]);
    expect(within(dialog).getByText('attempt 1: generating')).toBeInTheDocument();
    expect(within(dialog).getByLabelText('Run')).toHaveTextContent('1/5 · generating');

    await stream.push([...attemptOne.slice(1), state(run({ attempt: 1, phase: 'building', lastPct: 71.4 }))]);
    const timeline = within(dialog).getByRole('list', { name: 'Run activity' });
    expect(timeline).toHaveTextContent('write_file');
    expect(timeline).toHaveTextContent('tests/Lab.Tests/BetaTests.cs');
    expect(timeline).toHaveTextContent('created, 214 bytes');
    expect(timeline).toHaveTextContent('Wrote one test per branch.');
    expect(timeline).toHaveTextContent('Which lines are uncovered?');
    expect(timeline).toHaveTextContent('Attempt 1: 62.0% → 71.4%');
    expect(within(dialog).getByLabelText('Run')).toHaveTextContent('71.4% of 85%');
  });

  it('says what each attempt ran: related tests, then the whole suite that confirmed it, or why the whole suite ran', async () => {
    const done = run({ state: 'candidate', attempt: 2, active: false, lastPct: 86.3 });
    const attempt = (n: number, extra: Record<string, unknown>) => ({
      attempt: n,
      before: 41,
      after: 86.3,
      build: 'ok',
      tests: { passed: 1219, failed: 0, skipped: 0 },
      errors: [],
      violations: 0,
      ...extra,
    });
    const first = attempt(1, { run: { scope: 'all', files: 0, tests: 1219, reason: 'no test uses the target or a changed test file' } });
    const second = attempt(2, {
      run: { scope: 'related', files: 3, tests: 58 },
      confirmation: { scope: 'all', files: 0, tests: 1219, pct: 86.3 },
    });
    stubApi(done, () =>
      streamResponse([
        started,
        state(done),
        sse(EventType.STATE_SNAPSHOT, { snapshot: { ...done, attempts: [first] } }),
        sse(EventType.STATE_SNAPSHOT, { snapshot: { ...done, attempts: [first, second] } }),
        sse(EventType.RUN_FINISHED, { threadId: 'testgen:r_1', runId: 'r_1', outcome: { type: 'success' } }),
      ]),
    );
    renderWithProviders(<CoveragePage />, { route: '/coverage?file=src%2FLab%2FBeta.cs', session: makeSession('TENANT_ADMIN') });

    const dialog = await openActivity();

    const timeline = within(dialog).getByRole('list', { name: 'Run activity' });
    expect(await within(timeline).findByText(/related: 3 files, 58 tests → whole suite: 1,219 tests \(confirmation\)/)).toBeInTheDocument();
    expect(timeline).toHaveTextContent('Attempt 1: 41.0% → 86.3% · build ok · 1219 passed, 0 failed · whole suite: no test uses the target or a changed test file');
  });

  it('shows an attempt recorded before attempts said what they ran as it always did', async () => {
    const failed = run({ state: 'failed', reason: 'runner_unavailable', active: false });
    stubApi(failed, () =>
      streamResponse([started, state(failed), ...attemptOne, state(failed), sse(EventType.RUN_ERROR, { code: 'runner_unavailable', message: '' })]),
    );
    renderWithProviders(<CoveragePage />, { route: '/coverage?file=src%2FLab%2FBeta.cs', session: makeSession('TENANT_ADMIN') });

    const dialog = await openActivity();

    const row = (await within(dialog).findByText('Attempt 1')).closest('li')!;
    expect(row).toHaveTextContent('3 passed, 1 failed');
    expect(row).not.toHaveTextContent('related');
    expect(row).not.toHaveTextContent('whole suite');
  });

  it('shows a finished run whole, and why it failed', async () => {
    const failed = run({ state: 'failed', reason: 'runner_unavailable', active: false });
    stubApi(failed, () =>
      streamResponse([
        started,
        state(failed),
        ...attemptOne,
        // How the run ended is in the state the server sends before the end.
        state(failed),
        sse(EventType.RUN_ERROR, { code: 'runner_unavailable', message: 'The run failed (runner_unavailable).' }),
      ]),
    );
    renderWithProviders(<CoveragePage />, { route: '/coverage?file=src%2FLab%2FBeta.cs', session: makeSession('TENANT_ADMIN') });

    const dialog = await openActivity();

    expect(await within(dialog).findByText('(runner_unavailable)')).toBeInTheDocument();
    expect(within(dialog).getByRole('list', { name: 'Run activity' })).toHaveTextContent('Attempt 1');
    expect(within(dialog).getByLabelText('Run')).toHaveTextContent('Failed');
    expect(within(dialog).queryByRole('button', { name: 'Cancel run' })).toBeNull();
  });

  it('ends a run stopped by its budget on why, in the header and the timeline', async () => {
    const done = run({
      state: 'completed_no_change',
      reason: 'budget',
      attempt: 2,
      active: false,
      budget: { maxTokens: 400000, maxCostUsd: null },
    });
    stubApi(done, () =>
      streamResponse([
        started,
        state(done),
        ...attemptOne,
        sse(EventType.STEP_STARTED, { stepName: 'attempt 2: measuring' }),
        sse(EventType.STEP_FINISHED, { stepName: 'attempt 2: measuring' }),
        sse(EventType.STATE_SNAPSHOT, {
          snapshot: { ...done, attempts: [firstAttempt], stop: { reason: 'budget', lastAttempt: 2, bestPct: 0, notStarted: 3 } },
        }),
        sse(EventType.RUN_FINISHED, { threadId: 'testgen:r_1', runId: 'r_1', outcome: { type: 'success' } }),
      ]),
    );
    renderWithProviders(<CoveragePage />, { route: '/coverage?file=src%2FLab%2FBeta.cs', session: makeSession('TENANT_ADMIN') });

    const dialog = await openActivity();

    const timeline = within(dialog).getByRole('list', { name: 'Run activity' });
    expect(await within(timeline).findByText('Stopped before attempt 3: the budget would be exceeded.')).toBeInTheDocument();
    expect(timeline.lastElementChild).toHaveTextContent('Stopped before attempt 3');
    const header = within(dialog).getByLabelText('Run');
    expect(header).toHaveTextContent('No change · budget');
    expect(header).toHaveTextContent('400,000 tokens');
    expect(within(header).queryByRole('img', { name: 'Agent working' })).toBeNull();
  });

  it('shows the budget as unlimited, and that the agent is working, while a run without one is live', async () => {
    const streams = liveStreams();
    stubApi(run(), streams.open);
    renderWithProviders(<CoveragePage />, { route: '/coverage?file=src%2FLab%2FBeta.cs', session: makeSession('TENANT_ADMIN') });

    const dialog = await openActivity();
    await streams.push([started, state(run())]);

    const header = within(dialog).getByLabelText('Run');
    expect(header).toHaveTextContent('unlimited');
    expect(within(header).getByRole('img', { name: 'Agent working' })).toBeInTheDocument();
  });

  it('says so when a run recorded no activity', async () => {
    const done = run({ state: 'completed_no_change', active: false });
    stubApi(done, () => streamResponse([started, state(done), sse(EventType.RUN_FINISHED, { threadId: 'testgen:r_1', runId: 'r_1', outcome: { type: 'success' } })]));
    renderWithProviders(<CoveragePage />, { route: '/coverage?file=src%2FLab%2FBeta.cs', session: makeSession('TENANT_ADMIN') });

    const dialog = await openActivity();

    expect(await within(dialog).findByText('No activity was recorded for this run.')).toBeInTheDocument();
  });

  it('lets an administrator cancel an active run from the modal', async () => {
    const calls = stubApi(run(), () => liveStream().response);
    renderWithProviders(<CoveragePage />, { route: '/coverage?file=src%2FLab%2FBeta.cs', session: makeSession('TENANT_ADMIN') });

    const dialog = await openActivity();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel run' }));

    await vi.waitFor(() => expect(calls.some((c) => c.url === '/api/coverage/runs/r_1/cancel' && c.method === 'POST')).toBe(true));
  });

  it('lets someone who is not an administrator watch, without cancel', async () => {
    const stream = liveStreams();
    stubApi(run(), stream.open);
    renderWithProviders(<CoveragePage />, { route: '/coverage?file=src%2FLab%2FBeta.cs' });

    const dialog = await openActivity();
    await stream.push([started, state(run()), ...attemptOne.slice(0, 1)]);

    expect(within(dialog).getByText('attempt 1: generating')).toBeInTheDocument();
    expect(within(dialog).queryByRole('button', { name: 'Cancel run' })).toBeNull();
  });

  it('closes with Escape for someone who may not stop the run, leaves it alone, and gives focus back', async () => {
    const calls = stubApi(run(), () => liveStream().response);
    renderWithProviders(<CoveragePage />, { route: '/coverage?file=src%2FLab%2FBeta.cs' });

    const dialog = await openActivity();
    expect(dialog).toHaveFocus();
    await userEvent.keyboard('{Escape}');

    expect(screen.queryByRole('dialog')).toBeNull();
    expect(screen.getByRole('button', { name: 'Activity' })).toHaveFocus();
    expect(calls.some((c) => c.url.endsWith('/cancel'))).toBe(false);
  });

  it('stops the run with Escape for an administrator, once, and says it is stopping', async () => {
    const calls = stubApi(run(), () => liveStream().response);
    renderWithProviders(<CoveragePage />, { route: '/coverage?file=src%2FLab%2FBeta.cs', session: makeSession('TENANT_ADMIN') });

    const dialog = await openActivity();
    expect(within(dialog).getByTestId('stop-hint')).toHaveTextContent('Esc to stop');
    await userEvent.keyboard('{Escape}');

    await vi.waitFor(() =>
      expect(calls.filter((c) => c.url === '/api/coverage/runs/r_1/cancel' && c.method === 'POST')).toHaveLength(1),
    );
    expect(within(dialog).getByTestId('stop-hint')).toHaveTextContent('Stopping…');
    // The dialog stays to show the run end; a second Escape sends no second cancel.
    await userEvent.keyboard('{Escape}');
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(calls.filter((c) => c.url.endsWith('/cancel'))).toHaveLength(1);
    // Its close button still only closes it.
    await userEvent.click(within(dialog).getByRole('button', { name: 'Close' }));
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('stays where the user scrolled, and offers the way back to the newest entry', async () => {
    const stream = liveStreams();
    stubApi(run(), stream.open);
    renderWithProviders(<CoveragePage />, { route: '/coverage?file=src%2FLab%2FBeta.cs', session: makeSession('TENANT_ADMIN') });
    const dialog = await openActivity();
    await stream.push([started, state(run()), ...attemptOne.slice(0, 1)]);
    const timeline = within(dialog).getByRole('list', { name: 'Run activity' });

    // Scrolled up: well away from the bottom.
    Object.defineProperty(timeline, 'scrollHeight', { configurable: true, value: 1000 });
    Object.defineProperty(timeline, 'clientHeight', { configurable: true, value: 200 });
    timeline.scrollTop = 100;
    fireEvent.scroll(timeline);
    await stream.push(attemptOne.slice(1, 10));

    expect(timeline.scrollTop).toBe(100);
    await userEvent.click(within(dialog).getByRole('button', { name: 'New activity ↓' }));
    expect(timeline.scrollTop).toBe(1000);
    expect(within(dialog).queryByRole('button', { name: 'New activity ↓' })).toBeNull();
  });

  it('stays open when the run ends, so the end can be seen', async () => {
    const streams = liveStreams();
    let fileRun = run();
    stubApi(null, streams.open);
    vi.stubGlobal(
      'fetch',
      agentFetch(vi.fn(async (url: string) => {
        if (url.startsWith('/api/coverage/tree')) return jsonResponse(sampleTree);
        if (url.startsWith('/api/coverage/files')) return jsonResponse(detail({ run: fileRun }));
        if (url.endsWith('/events')) return streams.open();
        return jsonResponse(undefined, 204);
      })),
    );
    renderWithProviders(<CoveragePage />, { route: '/coverage?file=src%2FLab%2FBeta.cs', session: makeSession('TENANT_ADMIN') });
    const dialog = await openActivity();

    // The run ends: the file now shows it unlocked, and the stream says why it ended.
    fileRun = run({ state: 'canceled', active: false });
    await streams.push([started, state(fileRun), sse(EventType.RUN_ERROR, { code: 'canceled', message: 'The run canceled.' })]);

    expect(await screen.findByRole('button', { name: 'Save' })).toBeInTheDocument();
    expect(screen.getByRole('dialog', { name: /Activity/ })).toBe(dialog);
    expect(within(dialog).getByLabelText('Run')).toHaveTextContent('Canceled');
  });

  it('is offered next to Save, and only for a file that has a run', async () => {
    stubApi(null, () => streamResponse([]));
    renderWithProviders(<CoveragePage />, { route: '/coverage?file=src%2FLab%2FBeta.cs', session: makeSession('TENANT_ADMIN') });

    expect(await screen.findByRole('button', { name: 'Save' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Activity' })).toBeNull();
  });

  it('takes the place of Save while a run is active', async () => {
    stubApi(run(), () => liveStream().response);
    renderWithProviders(<CoveragePage />, { route: '/coverage?file=src%2FLab%2FBeta.cs', session: makeSession('TENANT_ADMIN') });

    expect(await screen.findByRole('button', { name: 'Activity' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Save' })).toBeNull();
  });

  it('sits next to Save once a run has ended', async () => {
    const finished = run({ state: 'failed', reason: 'deadline', active: false });
    stubApi(finished, () => streamResponse([started, state(finished), sse(EventType.RUN_ERROR, { code: 'deadline', message: '' })]));
    renderWithProviders(<CoveragePage />, { route: '/coverage?file=src%2FLab%2FBeta.cs', session: makeSession('TENANT_ADMIN') });

    const save = await screen.findByRole('button', { name: 'Save' });
    expect(save.parentElement).toContainElement(screen.getByRole('button', { name: 'Activity' }));
  });
});

describe('attempt scope', () => {
  it('names a related run, its confirmation and what was reused', () => {
    expect(attemptScope({ run: { scope: 'related', files: 1, tests: 1 } })).toBe('related: 1 file, 1 test');
    expect(
      attemptScope({
        run: { scope: 'related', files: 3, tests: 58, reused: true },
        confirmation: { scope: 'all', files: 0, tests: 1219, reused: true },
      }),
    ).toBe('related: 3 files, 58 tests (reused) → whole suite: 1,219 tests (confirmation, reused)');
  });

  it('names a whole-suite run by its reason, or by its tests when it has none', () => {
    expect(attemptScope({ run: { scope: 'all', files: 0, tests: 7, reason: 'the diff changes the test project' } })).toBe(
      'whole suite: the diff changes the test project',
    );
    expect(attemptScope({ run: { scope: 'all', files: 0, tests: 7, reason: null } })).toBe('whole suite: 7 tests');
  });

  it('says nothing for an entry that does not say what it ran', () => {
    expect(attemptScope({})).toBeNull();
    expect(attemptScope({ run: null, confirmation: null })).toBeNull();
  });
});
