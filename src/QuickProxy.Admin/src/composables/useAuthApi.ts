import { INTERNAL_ADMIN_API_ROOT } from './apiRoots';

export interface AdminUser {
  email: string;
  fullName?: string | null;
  enabled: boolean;
  authType?: string;
  authProviderId?: string | null;
  hasPassword?: boolean;
  externalIdentityCount?: number;
}

export interface ExternalLoginProvider {
  id: string;
  displayName: string;
  type: 'oidc' | 'ldap';
}

export interface AuthStatusResponse {
  authenticated: boolean;
  hasUsers: boolean;
  user?: AdminUser | null;
  externalProviders?: ExternalLoginProvider[];
}

export function useAuthApi() {
  async function getStatus() {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/auth/status`, {
      credentials: 'include',
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    return await response.json() as AuthStatusResponse;
  }

  async function login(email: string, password: string) {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/auth/login`, {
      method: 'POST',
      credentials: 'include',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ email, passwordBase64: encodeBase64(password) }),
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }
  }

  async function bootstrap(email: string, password: string, fullName?: string) {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/auth/bootstrap`, {
      method: 'POST',
      credentials: 'include',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ email, passwordBase64: encodeBase64(password), fullName }),
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }
  }

  async function logout() {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/auth/logout`, {
      method: 'POST',
      credentials: 'include',
    });

    if (!response.ok && response.status !== 401) {
      throw new Error(await readApiError(response));
    }
  }

  async function startOidcLogin(providerId: string, returnUrl?: string) {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/auth/oidc/${encodeURIComponent(providerId)}/start`, {
      method: 'POST',
      credentials: 'include',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ returnUrl }),
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    return await response.json() as { url: string };
  }

  return {
    getStatus,
    login,
    bootstrap,
    logout,
    startOidcLogin,
  };
}

function encodeBase64(value: string): string {
  const bytes = new TextEncoder().encode(value);
  let binary = '';

  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }

  return btoa(binary);
}

async function readApiError(response: Response): Promise<string> {
  try {
    const payload = await response.json() as { message?: string; details?: string[] };

    if (payload.details?.length) {
      return `${payload.message ?? 'Request failed'}: ${payload.details.join('; ')}`;
    }

    return payload.message ?? `Request failed with status ${response.status}`;
  }
  catch {
    return `Request failed with status ${response.status}`;
  }
}
