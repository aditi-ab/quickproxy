import { describe, expect, it } from 'vitest';
import { normalizeMonacoThemeColor } from './monacoTheme';

describe('normalizeMonacoThemeColor', () => {
  it('expands shorthand colors accepted by CSS but rejected by Monaco', () => {
    expect(normalizeMonacoThemeColor('#fff', '#000000')).toBe('#ffffff');
    expect(normalizeMonacoThemeColor('#123a', '#000000')).toBe('#112233aa');
  });

  it('preserves supported six and eight digit colors', () => {
    expect(normalizeMonacoThemeColor('#f8fafc', '#000000')).toBe('#f8fafc');
    expect(normalizeMonacoThemeColor('#00000000', '#ffffff')).toBe('#00000000');
  });

  it('uses a valid fallback when a token is empty or unsupported', () => {
    expect(normalizeMonacoThemeColor('', '#fff')).toBe('#ffffff');
    expect(normalizeMonacoThemeColor('oklch(1 0 0)', '#0f172a')).toBe('#0f172a');
  });
});
