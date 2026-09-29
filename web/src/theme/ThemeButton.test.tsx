import { act, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { THEME_KEY } from './theme';
import { ThemeButton } from './ThemeButton';

const theme = () => document.documentElement.getAttribute('data-theme');

describe('ThemeButton', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
    document.documentElement.removeAttribute('data-theme');
  });

  it('cycles System → Light → Dark → System, forcing and remembering all but System', async () => {
    render(<ThemeButton />);
    const button = screen.getByRole('button', { name: 'Theme: System' });
    expect(theme()).toBeNull();

    await userEvent.click(button);
    expect(button).toHaveAccessibleName('Theme: Light');
    expect(button).toHaveTextContent('Light');
    expect(theme()).toBe('light');
    expect(localStorage.getItem(THEME_KEY)).toBe('light');

    await userEvent.click(button);
    expect(button).toHaveAccessibleName('Theme: Dark');
    expect(theme()).toBe('dark');
    expect(localStorage.getItem(THEME_KEY)).toBe('dark');

    await userEvent.click(button);
    expect(button).toHaveAccessibleName('Theme: System');
    expect(theme()).toBeNull();
    expect(localStorage.getItem(THEME_KEY)).toBeNull();
  });

  it('starts from the stored choice', () => {
    localStorage.setItem(THEME_KEY, 'dark');
    render(<ThemeButton />);
    expect(screen.getByRole('button')).toHaveAccessibleName('Theme: Dark');
  });

  it('ignores a stored value it does not know', () => {
    localStorage.setItem(THEME_KEY, 'sepia');
    render(<ThemeButton />);
    expect(screen.getByRole('button')).toHaveAccessibleName('Theme: System');
  });

  it('still changes the theme when storage refuses', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('blocked', 'SecurityError');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('blocked', 'SecurityError');
    });
    render(<ThemeButton />);
    await userEvent.click(screen.getByRole('button', { name: 'Theme: System' }));
    expect(theme()).toBe('light');
    expect(screen.getByRole('button')).toHaveAccessibleName('Theme: Light');
  });

  it('moves one mode per press, even for two presses before React renders', () => {
    render(<ThemeButton />);
    const button = screen.getByRole('button', { name: 'Theme: System' });
    // One act batches both clicks into a single render, as a fast double-click can.
    act(() => {
      button.click();
      button.click();
    });
    expect(theme()).toBe('dark');
    expect(button).toHaveAccessibleName('Theme: Dark');
    expect(localStorage.getItem(THEME_KEY)).toBe('dark');
  });
});
