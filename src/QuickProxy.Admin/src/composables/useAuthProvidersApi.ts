import { INTERNAL_ADMIN_API_ROOT } from './apiRoots';

export type AuthProviderType = 'ldap' | 'oidc';

export interface AuthProviderSummary {
  id: string;
  displayName: string;
  enabled: boolean;
  allowAutoAccess: boolean;
  type: AuthProviderType;
  ldap: {
    server: string;
    port: number;
    useSsl: boolean;
    bindDn: string;
    hasBindPassword: boolean;
    baseDn: string;
    userFilter: string;
    emailAttribute: string;
    fullNameAttribute: string;
  };
  oidc: {
    authority: string;
    metadataUrl: string;
    clientId: string;
    hasClientSecret: boolean;
    scopes: string;
    emailClaim: string;
    nameClaim: string;
    subjectClaim: string;
    usePkce: boolean;
  };
}

export interface UpsertAuthProviderRequest {
  id: string;
  displayName: string;
  enabled: boolean;
  allowAutoAccess: boolean;
  type: AuthProviderType;
  ldap: {
    server: string;
    port: number;
    useSsl: boolean;
    bindDn: string;
    bindPassword?: string;
    clearBindPassword: boolean;
    baseDn: string;
    userFilter: string;
    emailAttribute: string;
    fullNameAttribute: string;
  };
  oidc: {
    authority: string;
    metadataUrl: string;
    clientId: string;
    clientSecret?: string;
    clearClientSecret: boolean;
    scopes: string;
    emailClaim: string;
    nameClaim: string;
    subjectClaim: string;
    usePkce: boolean;
  };
}

export function useAuthProvidersApi() {
  async function listProviders() {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/auth-providers`, { credentials: 'include' });

    if (!response.ok)
      throw new Error(await readApiError(response));

    return await response.json() as AuthProviderSummary[];
  }

  async function createProvider(request: UpsertAuthProviderRequest) {
    return await sendJson(`${INTERNAL_ADMIN_API_ROOT}/auth-providers`, 'POST', request);
  }

  async function updateProvider(id: string, request: UpsertAuthProviderRequest) {
    return await sendJson(`${INTERNAL_ADMIN_API_ROOT}/auth-providers/${encodeURIComponent(id)}`, 'PUT', request);
  }

  async function deleteProvider(id: string) {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/auth-providers/${encodeURIComponent(id)}`, {
      method: 'DELETE',
      credentials: 'include',
    });

    if (!response.ok)
      throw new Error(await readApiError(response));
  }

  async function testLdap(request: UpsertAuthProviderRequest) {
    return await sendJson(`${INTERNAL_ADMIN_API_ROOT}/auth-providers/test/ldap`, 'POST', request);
  }

  async function testOidcDiscovery(request: UpsertAuthProviderRequest) {
    return await sendJson(`${INTERNAL_ADMIN_API_ROOT}/auth-providers/test/oidc-discovery`, 'POST', request);
  }

  return {
    listProviders,
    createProvider,
    updateProvider,
    deleteProvider,
    testLdap,
    testOidcDiscovery,
  };
}

async function sendJson(url: string, method: string, body: unknown) {
  const response = await fetch(url, {
    method,
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(body),
  });

  if (!response.ok)
    throw new Error(await readApiError(response));

  return response.status === 204 ? null : await response.json();
}

async function readApiError(response: Response): Promise<string> {
  try {
    const payload = await response.json() as { message?: string; details?: string[] };

    if (payload.details?.length)
      return `${payload.message ?? 'Request failed'}: ${payload.details.join('; ')}`;

    return payload.message ?? `Request failed with status ${response.status}`;
  }
  catch {
    return `Request failed with status ${response.status}`;
  }
}
