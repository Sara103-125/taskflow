import { Injectable, effect, signal } from '@angular/core';

type Theme = 'light' | 'dark';
const STORAGE_KEY = 'taskflow.theme';

/**
 * Dark mode. The CSS defines all colours as variables; dark mode just swaps their values
 * when <html data-theme="dark"> is set. Starts from the OS setting, then remembers your choice.
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly theme = signal<Theme>(this.initialTheme());

  constructor() {
    effect(() => {
      document.documentElement.dataset['theme'] = this.theme();
      try {
        localStorage.setItem(STORAGE_KEY, this.theme());
      } catch {
        // storage can be blocked (private mode); the theme still works for this visit
      }
    });
  }

  toggle(): void {
    this.theme.update((t) => (t === 'dark' ? 'light' : 'dark'));
  }

  private initialTheme(): Theme {
    try {
      const saved = localStorage.getItem(STORAGE_KEY);
      if (saved === 'light' || saved === 'dark') return saved;
    } catch {
      // ignore
    }
    return window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  }
}
