import type { NodeHealth } from '../api/types';
import styles from './Topology.module.css';

/** Health is never shown by colour alone: each state also has a symbol and a word. */
export const HEALTH_MARK: Record<NodeHealth, { symbol: string; label: string; className: string }> =
  {
    Healthy: { symbol: '●', label: 'healthy', className: styles.healthy },
    Degraded: { symbol: '▲', label: 'degraded', className: styles.degraded },
    Unreachable: { symbol: '✕', label: 'unreachable', className: styles.unreachable },
    NotProbed: { symbol: '◌', label: 'not probed', className: styles.notProbed },
  };
