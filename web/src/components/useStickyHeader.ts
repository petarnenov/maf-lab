import { useLayoutEffect, useState, type RefObject } from 'react';

/** The root custom property every consumer reads: the pinned header's height, or `0px` when it is not pinned. */
export const HEADER_OFFSET_VAR = '--app-header-offset';

/** The header is pinned only while it takes at most this share of the window's height. */
export const MAX_SHARE_OF_WINDOW = 1 / 3;

/**
 * Measures the header as it wraps, decides whether it stays pinned at the top (not when it would take more than a
 * third of the window), and publishes the offset on the root so anchor jumps and window-sized screens leave room for
 * it (keep-the-header-in-view, design §2–3).
 */
export function useStickyHeader(ref: RefObject<HTMLElement | null>): boolean {
  const [sticky, setSticky] = useState(false);

  useLayoutEffect(() => {
    const header = ref.current;
    if (!header) return;
    const root = document.documentElement;

    const measure = () => {
      const height = header.getBoundingClientRect().height;
      const pinned = height > 0 && height <= window.innerHeight * MAX_SHARE_OF_WINDOW;
      root.style.setProperty(HEADER_OFFSET_VAR, pinned ? `${Math.ceil(height)}px` : '0px');
      setSticky(pinned);
    };

    measure();
    window.addEventListener('resize', measure);
    const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(measure);
    observer?.observe(header);

    return () => {
      window.removeEventListener('resize', measure);
      observer?.disconnect();
      root.style.removeProperty(HEADER_OFFSET_VAR);
    };
  }, [ref]);

  return sticky;
}
