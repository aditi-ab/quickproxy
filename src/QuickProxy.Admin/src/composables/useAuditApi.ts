import { INTERNAL_ADMIN_API_ROOT } from './apiRoots';

export interface AuditActor {
  type: string;
  id?: string | null;
  displayName?: string | null;
}

export interface AuditFieldChange {
  path: string;
  before?: string | null;
  after?: string | null;
  kind: string;
}

export interface AuditChangeSet {
  summary?: string | null;
  fields: AuditFieldChange[];
}

export interface AuditEventListItem {
  id: string;
  occurredAtUtc: string;
  module: string;
  action: string;
  targetType?: string | null;
  targetId?: string | null;
  actor: AuditActor;
  source: string;
  outcome: string;
  statusCode?: number | null;
  correlationId?: string | null;
  error?: string | null;
  summary?: string | null;
}

export interface AuditEvent extends AuditEventListItem {
  changes?: AuditChangeSet | null;
}

export interface AuditListResponse {
  total: number;
  items: AuditEventListItem[];
}

export interface AuditQuery {
  module?: string;
  action?: string;
  actor?: string;
  target?: string;
  outcome?: string;
  fromUtc?: string;
  toUtc?: string;
  limit?: number;
  offset?: number;
}

export function useAuditApi() {
  async function listAuditEvents(query: AuditQuery = {}) {
    const url = new URL(`${window.location.origin}${INTERNAL_ADMIN_API_ROOT}/audit`);

    for (const [key, value] of Object.entries(query)) {
      if (value === undefined || value === null || value === '') {
        continue;
      }

      url.searchParams.set(key, String(value));
    }

    const response = await fetch(url, {
      credentials: 'include',
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    return await response.json() as AuditListResponse;
  }

  async function getAuditEvent(id: string) {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/audit/${encodeURIComponent(id)}`, {
      credentials: 'include',
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    return await response.json() as AuditEvent;
  }

  return {
    listAuditEvents,
    getAuditEvent,
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
