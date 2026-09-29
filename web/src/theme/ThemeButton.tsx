import { useState } from 'react';
import styles from '../components/Layout.module.css';
import { applyTheme, currentTheme, nextTheme, readTheme, type ThemeMode } from './theme';

const LABEL: Record<ThemeMode, string> = { system: 'System', light: 'Light', dark: 'Dark' };
const ICON: Record<ThemeMode, string> = { system: '◐', light: '☀', dark: '☾' };

/** One button that cycles System → Light → Dark and names the mode that is on. */
export function ThemeButton() {
  const [mode, setMode] = useState<ThemeMode>(readTheme);

  function cycle() {
    // From the page, not from `mode`: two presses before a render would both read the same stale `mode`.
    const next = nextTheme(currentTheme());
    applyTheme(next);
    setMode(next);
  }

  return (
    <button
      type="button"
      className={styles.theme}
      aria-label={`Theme: ${LABEL[mode]}`}
      title={`Switch to ${LABEL[nextTheme(mode)]} theme`}
      onClick={cycle}
    >
      <span aria-hidden="true">{ICON[mode]}</span> {LABEL[mode]}
    </button>
  );
}
