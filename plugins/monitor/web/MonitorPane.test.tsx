import { act } from '@testing-library/react';
import { useState } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ChatContext } from '@maf/plugin-api';
import { hangingFetch, jsonResponse, renderWithProviders } from '@maf/testing';
import { MonitorPane, TRACE_POLL_MS } from './MonitorPane';
import { createMonitorStore, type MonitorStore } from './monitorStore';

/** What the chat tells the pane, changed by the test as the chat would change it. */
let show: (context: ChatContext) => void = () => {};
function Chat({ store, initial }: { store: MonitorStore; initial: ChatContext }) {
  const [current, setCurrent] = useState(initial);
  show = setCurrent;
  return <MonitorPane context={current} store={store} />;
}

function context(active: boolean, streaming = true): ChatContext {
  return {
    turn: {
      key: 'a-1',
      question: 'q',
      sources: [],
      restored: false,
      streaming,
      text: '',
    },
    openPane: () => {},
    pane: { active },
  };
}

const isLive = (url: string) => url.startsWith('/api/runs/');

describe('MonitorPane', () => {
  beforeEach(() => vi.useFakeTimers({ shouldAdvanceTime: true }));
  afterEach(() => vi.useRealTimers());

  it('reads a live run’s trace only while it shows, and aborts the read when it stops showing', async () => {
    const held = hangingFetch(isLive, () => jsonResponse({}, 404));
    const store = createMonitorStore();
    store.observer.onRunStart?.({ runId: 'r-1', turnKey: 'a-1' });

    const view = renderWithProviders(<Chat initial={context(false)} store={store} />);
    await act(() => vi.advanceTimersByTimeAsync(TRACE_POLL_MS * 3));
    expect(held).toHaveLength(0);

    act(() => show(context(true)));
    await act(() => vi.advanceTimersByTimeAsync(0));
    expect(held.map((h) => h.url)).toEqual(['/api/runs/r-1/trace?after=0']);
    expect(held[0].signal.aborted).toBe(false);

    view.unmount();
    expect(held[0].signal.aborted).toBe(true);
  });

  it('polls while the run goes, and reads once more when it has ended', async () => {
    const fetch = vi.fn(async (url: string) =>
      isLive(url) ? jsonResponse({ events: [] }) : jsonResponse({}, 404),
    );
    vi.stubGlobal('fetch', fetch);
    const store = createMonitorStore();
    store.observer.onRunStart?.({ runId: 'r-1', turnKey: 'a-1' });
    const reads = () => fetch.mock.calls.filter(([url]) => isLive(url)).length;

    renderWithProviders(<Chat initial={context(true)} store={store} />);
    await act(() => vi.advanceTimersByTimeAsync(TRACE_POLL_MS * 2 + 10));
    expect(reads()).toBe(3);

    act(() => show(context(true, false)));
    await act(() => vi.advanceTimersByTimeAsync(TRACE_POLL_MS * 4));
    expect(reads()).toBe(4);
  });
});
