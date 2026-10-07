import { waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { EvalsPage } from '../evals/EvalsPage';
import { renderWithProviders } from '../test/render';

describe('requests the web makes (stop-anything)', () => {
  it('a page left while its request is still loading aborts that request', async () => {
    const signals: AbortSignal[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn((_url: string, init?: RequestInit) => {
        if (init?.signal) signals.push(init.signal);
        // A report that takes its time: it is still loading when the page goes.
        return new Promise<Response>((_, reject) =>
          init?.signal?.addEventListener('abort', () =>
            reject(new DOMException('aborted', 'AbortError')),
          ),
        );
      }),
    );

    const page = renderWithProviders(<EvalsPage />);
    await waitFor(() => expect(signals.length).toBeGreaterThan(0));
    expect(signals.every((s) => !s.aborted)).toBe(true);

    page.unmount();

    await waitFor(() => expect(signals.every((s) => s.aborted)).toBe(true));
  });
});
