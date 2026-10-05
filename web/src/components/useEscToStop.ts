import { useEffect, useRef } from 'react';

/**
 * Esc stops what the page started (stop-anything): while `active`, an Esc anywhere on the page calls `stop`. An Esc a
 * control has already handled for itself (closing a menu, cancelling an edit) or pressed while an input method is
 * composing is left alone. The page's own `stop` decides what stopping means — and that it happens once.
 */
export function useEscToStop(active: boolean, stop: () => void) {
  const latest = useRef(stop);
  useEffect(() => {
    latest.current = stop;
  }, [stop]);

  useEffect(() => {
    if (!active) return;
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key !== 'Escape' || e.defaultPrevented || e.isComposing) return;
      e.preventDefault();
      latest.current();
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [active]);
}
