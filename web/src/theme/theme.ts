/**
 * The colour theme (add-theme-toggle). "system" follows the operating system through `color-scheme: light dark`;
 * "light" and "dark" force it with `data-theme` on <html>. The inline script in index.html applies the stored mode
 * before the first paint with the same key and attribute — keep the two in step.
 */
export type ThemeMode = 'system' | 'light' | 'dark';

export const THEME_KEY = 'maf-lab.theme';
export const THEME_ORDER: ThemeMode[] = ['system', 'light', 'dark'];

export const nextTheme = (mode: ThemeMode): ThemeMode =>
  THEME_ORDER[(THEME_ORDER.indexOf(mode) + 1) % THEME_ORDER.length];

/** The stored mode; anything else, or storage that refuses, is "system". */
export function readTheme(): ThemeMode {
  try {
    const stored = localStorage.getItem(THEME_KEY);
    return stored === 'light' || stored === 'dark' ? stored : 'system';
  } catch {
    return 'system';
  }
}

/** The mode the page shows now, read from <html>: always current, even between a press and the next render. */
export function currentTheme(): ThemeMode {
  const forced = document.documentElement.getAttribute('data-theme');
  return forced === 'light' || forced === 'dark' ? forced : 'system';
}

/** Applies the mode to the page and remembers it; "system" forgets it. Storage that refuses only loses the memory. */
export function applyTheme(mode: ThemeMode) {
  const root = document.documentElement;
  if (mode === 'system') root.removeAttribute('data-theme');
  else root.setAttribute('data-theme', mode);
  try {
    if (mode === 'system') localStorage.removeItem(THEME_KEY);
    else localStorage.setItem(THEME_KEY, mode);
  } catch {
    // Blocked storage: the choice lasts for this page.
  }
}
