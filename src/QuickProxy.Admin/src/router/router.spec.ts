import { describe, expect, it } from 'vitest';
import router from './index';

describe('quickProxy administration routes', () => {
  it('keeps every product module on a conventional admin route', () => {
    expect(router.getRoutes().map(route => route.path)).toEqual(expect.arrayContaining([
      '/',
      '/proxy-hosts',
      '/containers',
      '/key-values',
      '/certificates',
      '/settings',
      '/audit',
      '/users',
    ]));
  });
});
