import { ref } from 'vue';

export type ThemePreference = 'system' | 'light' | 'dark';
export const themePreference = ref<ThemePreference>('system');

const storageKey = 'quickproxy.theme';
const media = window.matchMedia('(prefers-color-scheme: dark)');

function applyTheme(): void {
  const dark = themePreference.value === 'dark' || (themePreference.value === 'system' && media.matches);

  document.documentElement.classList.toggle('dark', dark);
  document.documentElement.style.colorScheme = dark ? 'dark' : 'light';
}

export function initializeTheme(): void {
  const stored = localStorage.getItem(storageKey);

  if (stored === 'light' || stored === 'dark' || stored === 'system')
    themePreference.value = stored;

  applyTheme();
  media.addEventListener('change', applyTheme);
}

export function setThemePreference(value: ThemePreference): void {
  themePreference.value = value;
  localStorage.setItem(storageKey, value);
  applyTheme();
}
