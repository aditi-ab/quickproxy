import { describe, expect, it } from 'vitest';
import { createApp } from 'vue';
import { nativeUiPlugin } from './nativeUi';

describe('nativeUiPlugin', () => {
  it('registers card header actions used by product templates', () => {
    const app = createApp({});

    app.use(nativeUiPlugin);

    expect(app.component('CardAction')).toBeDefined();
  });
});
