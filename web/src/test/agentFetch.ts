import { EventType } from '@ag-ui/core';
import { jsonResponse } from './render';

type Handler = (url: string, init?: RequestInit) => Response | Promise<Response>;

/**
 * How the test runtime answers a stop. `abort` does what CopilotKit's runtime does on this stack: its runner aborts the
 * run's request to the agent, `@ag-ui/client` reports that as `RUN_ERROR { code: "abort" }`, and the run's stream ends.
 * `hold` answers the stop and leaves the run's stream to the test, which then says itself how the run ends.
 */
export type StopBehaviour = 'abort' | 'hold';

/** What CopilotKit's runtime says about itself: the two agents this system has. */
const INFO = {
  version: '1.76.0',
  agents: {
    chat: { name: 'chat', description: '' },
    testgen: { name: 'testgen', description: '' },
  },
  mode: 'sse',
  telemetryDisabled: true,
};

/** Trace events a test declared with `run.trace`, kept by run, as the trace API would serve them. */
const traces = new Map<string, unknown[]>();

/**
 * A fetch for tests that plays CopilotKit's runtime (agui-protocol-only). The app reaches its agents only through
 * CopilotKit, so here:
 * - the runtime's info is answered;
 * - a run of `chat` is handed to the test's own handler as `/api/chat`, and a run of `testgen` as
 *   `/api/coverage/runs/<id>/events` — the names the tests were written against — and whatever the handler streams is
 *   made a well-formed run (it starts with RUN_STARTED, a message opens before its content, it ends once);
 * - what tests declare with `run.trace` leaves the stream and is served by the trace API, and `run.sources` joins the
 *   tool result before it, which is where the server carries sources;
 * - a stop is accepted, and by default ends the thread's run as the runtime does (see `StopBehaviour`).
 * Every other request goes to the handler unchanged.
 */
export function agentFetch(
  handler: Handler,
  { stop = 'abort' }: { stop?: StopBehaviour } = {},
): Handler {
  /** The runs still streaming, by thread: a stop for the thread ends its run. */
  const open = new Map<string, () => void>();
  return async (url: string, init?: RequestInit) => {
    if (url.endsWith('/copilotkit/info')) return jsonResponse(INFO);
    const stopped = /\/copilotkit\/agent\/[^/]+\/stop\/([^/?]+)/.exec(url);
    if (stopped) {
      if (stop === 'abort') open.get(decodeURIComponent(stopped[1]))?.();
      return jsonResponse({ stopped: true });
    }
    const live = /^\/api\/runs\/([^/?]+)\/trace(?:\?after=(\d+))?/.exec(url);
    if (live) {
      const after = Number(live[2] ?? 0);
      const events = (traces.get(decodeURIComponent(live[1])) ?? []).filter(
        (e) => ((e as { seq?: number }).seq ?? 0) > after,
      );
      return jsonResponse({ runId: live[1], turnId: live[1], ended: true, events });
    }
    const agent = /\/copilotkit\/agent\/([^/]+)\/run$/.exec(url);
    if (!agent) return handler(url, init);

    const input = JSON.parse(String(init?.body ?? '{}')) as { threadId?: string; runId?: string };
    const legacy =
      agent[1] === 'chat'
        ? '/api/chat'
        : `/api/coverage/runs/${encodeURIComponent((input.threadId ?? '').split(':')[1] ?? '')}/events`;
    const response = await handler(legacy, init);
    if (!response.ok || !response.body) return response;
    const normalize = wellFormed(input.threadId ?? '', input.runId ?? '', agent[1] === 'chat');
    const decoder = new TextDecoder();
    const encoder = new TextEncoder();
    const threadId = input.threadId ?? '';
    let pending = '';
    const body = response.body.pipeThrough(
      new TransformStream<Uint8Array, Uint8Array>({
        start(controller) {
          open.set(threadId, () => {
            open.delete(threadId);
            const out = normalize.abort();
            if (out.length > 0) controller.enqueue(encoder.encode(out.join('')));
            controller.terminate();
          });
        },
        transform(chunk, controller) {
          pending += decoder.decode(chunk, { stream: true });
          const frames = pending.split('\n\n');
          pending = frames.pop() ?? '';
          const out = frames.flatMap((frame) => normalize.frame(frame));
          if (out.length > 0) controller.enqueue(encoder.encode(out.join('')));
        },
        flush(controller) {
          open.delete(threadId);
          const out = [...(pending ? normalize.frame(pending) : []), ...normalize.end()];
          if (out.length > 0) controller.enqueue(encoder.encode(out.join('')));
        },
      }),
    );
    return new Response(body, {
      status: response.status,
      headers: { 'Content-Type': 'text/event-stream' },
    });
  };
}

/**
 * Makes a fixture stream, frame by frame, into a run the protocol's client accepts. A chat fixture that never ends is
 * ended; a test-run fixture is left to end (or drop) as the test wrote it.
 */
function wellFormed(threadId: string, runId: string, endsByItself: boolean) {
  let started = false;
  let ended = false;
  let open: string | null = null;
  let lastResult: Record<string, unknown> | null = null;
  let held: Record<string, unknown> | null = null;
  const out: Record<string, unknown>[] = [];
  const emit = (e: Record<string, unknown>) => {
    // A tool result is held until the next event, so sources declared right after it can join it.
    if (held) out.push(held);
    held = endsByItself && e.type === EventType.TOOL_CALL_RESULT ? e : null;
    if (!held) out.push(e);
    if (e.type === EventType.RUN_FINISHED || e.type === EventType.RUN_ERROR) ended = true;
  };
  const close = () => {
    if (open) emit({ type: EventType.TEXT_MESSAGE_END, messageId: open });
    open = null;
  };
  const drain = () => {
    const frames = out.splice(0).map((e) => `data: ${JSON.stringify(e)}\n\n`);
    return frames;
  };
  const start = (event: Record<string, unknown> & { type: string }) => {
    if (started) return;
    started = true;
    if (event.type !== EventType.RUN_STARTED)
      emit({ type: EventType.RUN_STARTED, threadId, runId });
  };
  return {
    frame(frame: string): string[] {
      const line = frame.split('\n').find((l) => l.startsWith('data:'));
      if (!line) return [];
      const event = JSON.parse(line.slice(5).trim()) as Record<string, unknown> & { type: string };
      start(event);
      switch (event.type) {
        case EventType.CUSTOM:
          if (event.name === 'maf-lab/trace') {
            traces.set(runId, [...(traces.get(runId) ?? []), event.value]);
          } else if (event.name === 'maf-lab/sources') {
            const sources = (event.value as { sources: unknown[] }).sources;
            const result = held ?? lastResult;
            if (result) {
              const content = JSON.parse(String(result.content)) as Record<string, unknown>;
              result.content = JSON.stringify({ ...content, sources });
            } else {
              close();
              emit({
                type: EventType.TOOL_CALL_START,
                toolCallId: 'search',
                toolCallName: 'search_documents',
              });
              emit({ type: EventType.TOOL_CALL_END, toolCallId: 'search' });
              emit({
                type: EventType.TOOL_CALL_RESULT,
                toolCallId: 'search',
                messageId: 'search',
                content: JSON.stringify({
                  tool: 'search_documents',
                  summary: 'done',
                  sourceCount: sources.length,
                  sources,
                }),
              });
            }
          }
          break;
        case EventType.TEXT_MESSAGE_START:
          close();
          open = String(event.messageId);
          emit(event);
          break;
        case EventType.TEXT_MESSAGE_CONTENT:
          if (open !== String(event.messageId)) {
            close();
            open = String(event.messageId);
            emit({ type: EventType.TEXT_MESSAGE_START, messageId: open, role: 'assistant' });
          }
          emit(event);
          break;
        case EventType.TEXT_MESSAGE_END:
          if (open === String(event.messageId)) {
            emit(event);
            open = null;
          }
          break;
        case EventType.TOOL_CALL_START:
        case EventType.RUN_FINISHED:
        case EventType.RUN_ERROR:
          close();
          emit(event);
          break;
        case EventType.TOOL_CALL_RESULT:
          lastResult = event;
          emit(event);
          break;
        default:
          emit(event);
      }
      return drain();
    },
    /** The runtime aborted the run: what is open closes, and the run ends as `@ag-ui/client` reports an abort. */
    abort(): string[] {
      if (ended) return [];
      if (!started) {
        started = true;
        emit({ type: EventType.RUN_STARTED, threadId, runId });
      }
      close();
      emit({ type: EventType.RUN_ERROR, message: 'Request aborted', code: 'abort' });
      return drain();
    },
    end(): string[] {
      close();
      if (held) out.push(held);
      held = null;
      // A chat stream that ends without its end is what CopilotKit's runtime reports as an incomplete stream.
      if (endsByItself && !ended) {
        if (!started) out.push({ type: EventType.RUN_STARTED, threadId, runId });
        out.push({
          type: EventType.RUN_ERROR,
          message: 'Run ended without a terminal event',
          code: 'INCOMPLETE_STREAM',
        });
      }
      return drain();
    },
  };
}
