import { useState } from 'react';
import styles from '../components/Layout.module.css';
import { applyTheme, nextTheme, readTheme, type ThemeMode } from './theme';

const LABEL: Record<ThemeMode, string> = { system: 'System', light: 'Light', dark: 'Dark' };
const ICON: Record<ThemeMode, string> = { system: '◐', light: '☀', dark: '☾' };

/** One button that cycles System → Light → Dark and names the mode that is on. */
export function ThemeButton() {
  const [mode, setMode] = useState<ThemeMode>(readTheme);

  function cycle() {
    const next = nextTheme(mode);
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
