import { vi } from 'vitest';
import { jsonResponse } from './render';

/**
 * A fetch whose matching requests never answer until they are aborted (a slow report), and whose other requests are
 * answered by `others`. Returns the abort signals of the held requests.
 */
export function hangingFetch(
  holds: (url: string) => boolean,
  others: (url: string) => Response = () => jsonResponse({}, 404),
) {
  const held: { url: string; signal: AbortSignal }[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn((url: string, init?: RequestInit) => {
      if (!holds(url)) return Promise.resolve(others(url));
      const signal = init?.signal ?? new AbortController().signal;
      held.push({ url, signal });
      return new Promise<Response>((_, reject) =>
        signal.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError'))),
      );
    }),
  );
  return held;
}
