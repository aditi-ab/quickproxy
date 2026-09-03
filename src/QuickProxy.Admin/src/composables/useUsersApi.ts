import type { AdminUser } from './useAuthApi';
import { INTERNAL_ADMIN_API_ROOT } from './apiRoots';

export interface CreateUserRequest {
  email: string;
  password: string;
  fullName?: string | null;
  enabled: boolean;
}

export interface UpdateUserRequest {
  fullName?: string | null;
  enabled: boolean;
}

export function useUsersApi() {
  async function listUsers() {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/users`, {
      credentials: 'include',
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    return await response.json() as AdminUser[];
  }

  async function createUser(request: CreateUserRequest) {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/users`, {
      method: 'POST',
      credentials: 'include',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(request),
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }
  }

  async function updateUser(email: string, request: UpdateUserRequest) {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/users/${encodeURIComponent(email)}`, {
      method: 'PUT',
      credentials: 'include',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(request),
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }
  }

  async function updatePassword(email: string, password: string) {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/users/${encodeURIComponent(email)}/password`, {
      method: 'PUT',
      credentials: 'include',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ password }),
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }
  }

  async function deleteUser(email: string) {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/users/${encodeURIComponent(email)}`, {
      method: 'DELETE',
      credentials: 'include',
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }
  }

  return {
    listUsers,
    createUser,
    updateUser,
    updatePassword,
    deleteUser,
  };
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
