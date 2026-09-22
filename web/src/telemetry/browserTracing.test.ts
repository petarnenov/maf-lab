import { trace } from '@opentelemetry/api';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { startBrowserTracing } from './browserTracing';

describe('browser tracing', () => {
  afterEach(() => {
    trace.disable();
    vi.unstubAllGlobals();
  });

  it('stays out of the way when no endpoint is configured', () => {
    expect(startBrowserTracing(undefined)).toBe(false);
  });

  it('puts the run on a trace and sends it on with the request', async () => {
    // Stubbed first: the instrumentation patches whatever fetch is there when it is registered.
    const headers: Record<string, string>[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn(async (_url: string, init?: RequestInit) => {
        headers.push(Object.fromEntries(new Headers(init?.headers).entries()));
        return new Response('{}', { status: 200, headers: { 'Content-Type': 'application/json' } });
      }),
    );

    expect(startBrowserTracing('/v1/traces')).toBe(true);

    const tracer = trace.getTracer('test');
    await tracer.startActiveSpan('send', async (span) => {
      await fetch('/api/chat', { method: 'POST', body: '{}' });
      span.end();
    });

    // The api is told which trace this run belongs to; the server hangs its turn under it.
    expect(headers.some((h) => 'traceparent' in h)).toBe(true);
  });
});
