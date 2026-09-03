import type { IdentityStatus } from '@aditify/identity';
import { createIdentityApi } from '@aditify/identity';
import { reactive } from 'vue';

export const identityApi = createIdentityApi('/admin');
export const identityState = reactive<IdentityStatus & { initialized: boolean }>({
  initialized: false,
  authenticated: false,
  bootstrapRequired: false,
  mustChangePassword: false,
  roles: [],
  providers: [],
  antiforgeryToken: '',
});

export async function refreshIdentity() {
  Object.assign(identityState, await identityApi.status(), { initialized: true });
}

export function hasRole(...roles: string[]) {
  return identityState.roles.some(role => roles.includes(role));
}

export function installAuthenticatedFetch() {
  const originalFetch = window.fetch.bind(window);

  window.fetch = (input: RequestInfo | URL, init: RequestInit = {}) => {
    const method = (init.method ?? (input instanceof Request ? input.method : 'GET')).toUpperCase();
    const url = new URL(input instanceof Request ? input.url : String(input), window.location.href);

    if (url.origin !== window.location.origin || ['GET', 'HEAD', 'OPTIONS'].includes(method) || !identityState.antiforgeryToken)
      return originalFetch(input, init);

    const headers = new Headers(input instanceof Request ? input.headers : undefined);

    new Headers(init.headers).forEach((value, key) => headers.set(key, value));
    headers.set('X-CSRF-TOKEN', identityState.antiforgeryToken);
    return originalFetch(input, { ...init, headers });
  };
}
