import styles from './StopHint.module.css';

/**
 * Says that Esc stops the work in progress, or — once asked — that it is stopping (stop-anything), in the page's
 * theme. It never says the work has stopped: only the work's own state says that.
 */
export function StopHint({ stopping, className }: { stopping: boolean; className?: string }) {
  return (
    <div className={`${styles.hint} ${className ?? ''}`} data-testid="stop-hint" role="status">
      {stopping ? (
        'Stopping…'
      ) : (
        <>
          <kbd>Esc</kbd> to stop
        </>
      )}
    </div>
  );
}
