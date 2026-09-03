function expandHexColor(value: string) {
  const match = /^#([0-9a-f]{3}|[0-9a-f]{4}|[0-9a-f]{6}|[0-9a-f]{8})$/i.exec(value);

  if (!match) {
    return null;
  }

  const digits = match[1];

  if (!digits) {
    return null;
  }

  if (digits.length === 3 || digits.length === 4) {
    return `#${Array.from(digits, digit => `${digit}${digit}`).join('')}`;
  }

  return `#${digits}`;
}

export function normalizeMonacoThemeColor(value: string, fallback: string) {
  return expandHexColor(value.trim())
    ?? expandHexColor(fallback.trim())
    ?? '#000000';
}
