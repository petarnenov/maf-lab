import { trace } from '@opentelemetry/api';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { startBrowserTracing } from './browserTracing';

// Exercise the actual tracer/processor/instrumentation, with the collector replaced at its exporter boundary.
// shutdown flushes asynchronously; a fetch-only stub can be restored before that flush begins and leak real IO.
vi.mock('@opentelemetry/exporter-trace-otlp-http', () => ({
  OTLPTraceExporter: class {
    export(_spans: unknown[], done: (result: { code: number }) => void) {
      done({ code: 0 });
    }
    shutdown() {
      return Promise.resolve();
    }
  },
}));

describe('browser tracing', () => {
  afterEach(() => {
    trace.disable();
    vi.unstubAllGlobals();
  });

  it('stays out of the way when no endpoint is configured', () => {
    expect(startBrowserTracing(undefined)).toBeUndefined();
  });

  it('puts the run on a trace and sends it on with the request, until it is stopped', async () => {
    // Stubbed first: the instrumentation patches whatever fetch is there when it is registered.
    const headers: Record<string, string>[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn(async (_url: string, init?: RequestInit) => {
        headers.push(Object.fromEntries(new Headers(init?.headers).entries()));
        return new Response('{}', { status: 200, headers: { 'Content-Type': 'application/json' } });
      }),
    );

    const stop = startBrowserTracing('/v1/traces');
    expect(stop).toBeTypeOf('function');

    const send = () =>
      trace.getTracer('test').startActiveSpan('send', async (span) => {
        await fetch('/api/chat', { method: 'POST', body: '{}' });
        span.end();
      });
    await send();

    // The api is told which trace this run belongs to; the server hangs its turn under it.
    expect(headers.some((h) => 'traceparent' in h)).toBe(true);

    // The plugin left the set: nothing is traced or sent on any more.
    stop!();
    headers.length = 0;
    await send();
    expect(headers.some((h) => 'traceparent' in h)).toBe(false);
  });
});
