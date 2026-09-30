import { useCallback, useLayoutEffect, useState, type RefObject, type UIEvent } from 'react';

/** Assumed viewport when the container has no measured height yet (first render, or a test DOM). */
const FALLBACK_HEIGHT = 600;

export interface WindowedList {
  /** First and one-past-last index to render. */
  start: number;
  end: number;
  /** Space above the first rendered row and the height of the whole list, in px. */
  offsetTop: number;
  totalHeight: number;
  onScroll: (e: UIEvent<HTMLElement>) => void;
}

/**
 * Renders only the rows near the viewport of a scrolling container with fixed-height rows, so a list of thousands
 * of rows costs about a screenful of elements. Small enough not to be worth a dependency (DECISIONS §57).
 */
export function useWindowedList(
  container: RefObject<HTMLElement | null>,
  count: number,
  rowHeight: number,
  overscan = 30,
): WindowedList {
  const [scrollTop, setScrollTop] = useState(0);
  const [height, setHeight] = useState(FALLBACK_HEIGHT);

  useLayoutEffect(() => {
    const el = container.current;
    if (!el) return;
    const measure = () => setHeight(el.clientHeight || FALLBACK_HEIGHT);
    measure();
    if (typeof ResizeObserver === 'undefined') return;
    const observer = new ResizeObserver(measure);
    observer.observe(el);
    return () => observer.disconnect();
  }, [container]);

  const onScroll = useCallback((e: UIEvent<HTMLElement>) => setScrollTop(e.currentTarget.scrollTop), []);

  const first = Math.floor(scrollTop / rowHeight);
  const visible = Math.ceil(height / rowHeight);
  const start = Math.max(0, first - overscan);
  const end = Math.min(count, first + visible + overscan);
  return { start, end, offsetTop: start * rowHeight, totalHeight: count * rowHeight, onScroll };
}
