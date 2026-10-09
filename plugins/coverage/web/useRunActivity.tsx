import { useRef, useState } from 'react';
import type { RunSummary } from './types';
import { RunActivityDialog } from './RunActivity';

/**
 * The Activity button and its modal, apart: a caller whose layout changes when the run does (the threshold control
 * locks while a run is active) renders the button where it belongs and the dialog once, so the modal stays open
 * across that change — including the moment the run ends, which is when someone watching wants to see it.
 */
export function useRunActivity(run: RunSummary | null | undefined, canCancel: boolean) {
  const [open, setOpen] = useState(false);
  const button = useRef<HTMLButtonElement>(null);
  return {
    button: run ? (
      <button type="button" ref={button} onClick={() => setOpen(true)} aria-haspopup="dialog">
        Activity
      </button>
    ) : null,
    dialog:
      open && run ? (
        <RunActivityDialog
          run={run}
          canCancel={canCancel}
          onClose={() => {
            setOpen(false);
            button.current?.focus();
          }}
        />
      ) : null,
  };
}
