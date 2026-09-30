import styles from './Progress.module.css';

/**
 * Work in progress, in the page's theme (progress-feedback): determinate when the server reports a step count, an
 * indeterminate bar otherwise. It names what is running, and stands still when the system asks for reduced motion.
 */
export function Progress({ label, value, max }: { label: string; value?: number; max?: number }) {
  const determinate = value != null && max != null && max > 0;
  const width = determinate ? `${Math.min(100, Math.max(0, (value / max) * 100))}%` : undefined;
  return (
    <div className={styles.progress}>
      <div
        className={styles.track}
        role="progressbar"
        aria-label={label}
        aria-valuemin={determinate ? 0 : undefined}
        aria-valuenow={determinate ? value : undefined}
        aria-valuemax={determinate ? max : undefined}
      >
        <div
          className={determinate ? styles.fill : styles.indeterminate}
          style={width ? { width } : undefined}
        />
      </div>
      <span className={styles.label}>{determinate ? `${label} (${value}/${max})` : label}</span>
    </div>
  );
}
