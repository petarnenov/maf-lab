import type { Dispatch, KeyboardEvent } from 'react';
import type { TimeTravelAction } from './timeTravel';

/** Keyboard shortcuts for the monitor region; ignored while typing in form fields or on focused buttons (Space). */
export function timeTravelKeyHandler(dispatch: Dispatch<TimeTravelAction>, playing: boolean) {
  return (e: KeyboardEvent) => {
    const target = e.target as HTMLElement;
    const tag = target.tagName;
    if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || target.isContentEditable)
      return;
    switch (e.key) {
      case 'ArrowLeft':
        dispatch({ type: 'step', delta: -1 });
        break;
      case 'ArrowRight':
        dispatch({ type: 'step', delta: 1 });
        break;
      case 'Home':
        dispatch({ type: 'start' });
        break;
      case 'End':
        dispatch({ type: 'end' });
        break;
      case ' ':
        if (tag === 'BUTTON') return;
        dispatch({ type: playing ? 'pause' : 'play' });
        break;
      default:
        return;
    }
    e.preventDefault();
  };
}
