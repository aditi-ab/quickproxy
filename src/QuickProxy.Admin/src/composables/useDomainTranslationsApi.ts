import { INTERNAL_ADMIN_API_ROOT } from './apiRoots';

export interface DomainTranslationRule {
  id: string;
  enabled: boolean;
  sourceDomain: string;
  targetDomain: string;
  certificateId?: string | null;
  rewriteHostHeader: boolean;
}

export function useDomainTranslationsApi() {
  async function listRules() {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/domain-translations`);

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    return await response.json() as DomainTranslationRule[];
  }

  async function createRule(rule: DomainTranslationRule) {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/domain-translations`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(rule),
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }
  }

  async function updateRule(rule: DomainTranslationRule) {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/domain-translations/${encodeURIComponent(rule.id)}`, {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(rule),
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }
  }

  async function deleteRule(id: string) {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/domain-translations/${encodeURIComponent(id)}`, {
      method: 'DELETE',
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }
  }

  async function setRuleEnabled(rule: DomainTranslationRule, enabled: boolean) {
    await updateRule({
      ...rule,
      enabled,
    });
  }

  return {
    listRules,
    createRule,
    updateRule,
    deleteRule,
    setRuleEnabled,
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
