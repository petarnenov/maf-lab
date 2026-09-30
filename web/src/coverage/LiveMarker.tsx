import styles from './CoveragePage.module.css';

/** A moving dot that says the agent is working now; still (but shown) for anyone who prefers reduced motion. */
export function LiveMarker({ label = 'Agent working' }: { label?: string }) {
  return <span className={styles.liveDot} role="img" aria-label={label} title={label} />;
}
