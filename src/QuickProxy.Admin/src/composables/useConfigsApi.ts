import { INTERNAL_ADMIN_API_ROOT } from './apiRoots';

export type ConfigEntryType = 'data' | 'secret';
export type ConfigPayloadKind = 'text' | 'binary';

export interface ConfigLabel {
  key: string;
  value: string;
}

export interface ConfigEntry {
  key: string;
  value: string;
  binaryBase64?: string | null;
  encryptedValue?: string | null;
  encryptedBinaryBase64?: string | null;
  encryptedLabels?: string | null;
  mediaType?: string | null;
  entryType: ConfigEntryType;
  payloadKind: ConfigPayloadKind;
  labels: ConfigLabel[];
  updatedAtUtc: string;
  updatedBy?: string | null;
  source: 'local' | 'remote';
  readOnly: boolean;
  hasLocalOverride: boolean;
}

export interface ConfigEntryVariant {
  value: string;
  binaryBase64?: string | null;
  encryptedValue?: string | null;
  encryptedBinaryBase64?: string | null;
  encryptedLabels?: string | null;
  mediaType?: string | null;
  entryType: ConfigEntryType;
  payloadKind: ConfigPayloadKind;
  labels: ConfigLabel[];
  updatedAtUtc: string;
  updatedBy?: string | null;
  isRevealed?: boolean;
}

export interface ConfigEntryDetails extends ConfigEntry {
  isRevealed?: boolean;
  local?: ConfigEntryVariant | null;
  remote?: ConfigEntryVariant | null;
}

export interface ConfigEntryRevisionSummary {
  revisionId: string;
  key: string;
  entryType: ConfigEntryType;
  payloadKind: ConfigPayloadKind;
  mediaType?: string | null;
  updatedAtUtc: string;
  updatedBy?: string | null;
  capturedAtUtc: string;
  capturedBy?: string | null;
  action: string;
}

export interface ConfigEntryRevisionDetails {
  revisionId: string;
  key: string;
  capturedAtUtc: string;
  capturedBy?: string | null;
  action: string;
  snapshot: ConfigEntryVariant;
}

export interface ConfigTreeKey extends Omit<ConfigEntry, 'key'> {
  name: string;
  path: string;
}

export interface ConfigTreeNode {
  name: string;
  path: string;
  type: 'folder' | 'key';
  source: 'local' | 'remote';
  readOnly: boolean;
  hasLocalOverride: boolean;
  entryType: ConfigEntryType;
  payloadKind: ConfigPayloadKind;
  key?: ConfigTreeKey | null;
  children: ConfigTreeNode[];
}

export interface ConfigBackupEntry {
  key: string;
  entryType: ConfigEntryType;
  payloadKind: ConfigPayloadKind;
  value?: string | null;
  binaryBase64?: string | null;
  encryptedValue?: string | null;
  encryptedBinaryBase64?: string | null;
  encryptedLabels?: string | null;
  mediaType?: string | null;
  labels?: ConfigLabel[] | null;
}

export interface ConfigBackupRevision extends ConfigBackupEntry {
  revisionId: string;
  updatedAtUtc: string;
  updatedBy?: string | null;
  capturedAtUtc: string;
  capturedBy?: string | null;
  action: string;
}

export interface ConfigBackupDocument {
  formatVersion: number;
  exportedAtUtc: string;
  entries: ConfigBackupEntry[];
  revisions?: ConfigBackupRevision[] | null;
}

export interface UpsertConfigPayload {
  entryType: ConfigEntryType;
  payloadKind: ConfigPayloadKind;
  value?: string | null;
  binaryBase64?: string | null;
  mediaType?: string | null;
  labels?: ConfigLabel[] | null;
}

export function useConfigsApi() {
  async function listConfigs(prefix?: string) {
    const query = prefix ? `?prefix=${encodeURIComponent(prefix)}` : '';
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/configs${query}`, {
      credentials: 'include',
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    return await response.json() as ConfigEntry[];
  }

  async function upsertConfig(key: string, payload: UpsertConfigPayload) {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/configs/${encodePathKey(key)}`, {
      method: 'PUT',
      credentials: 'include',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(payload),
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }
  }

  async function getConfig(key: string) {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/configs/${encodePathKey(key)}`, {
      credentials: 'include',
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    return await response.json() as ConfigEntryDetails;
  }

  async function revealConfig(key: string, source?: 'local' | 'remote') {
    const query = source ? `?source=${encodeURIComponent(source)}` : '';
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/configs/reveal/${encodePathKey(key)}${query}`, {
      credentials: 'include',
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    return await response.json() as ConfigEntryVariant;
  }

  async function listConfigRevisions(key: string) {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/configs/revisions/${encodePathKey(key)}`, {
      credentials: 'include',
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    return await response.json() as ConfigEntryRevisionSummary[];
  }

  async function getConfigRevision(key: string, revisionId: string, reveal?: boolean) {
    const query = reveal ? '?reveal=true' : '';
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/configs/revisions/item/${encodeURIComponent(revisionId)}/${encodePathKey(key)}${query}`, {
      credentials: 'include',
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    return await response.json() as ConfigEntryRevisionDetails;
  }

  async function restoreConfigRevision(key: string, revisionId: string) {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/configs/revisions/restore/${encodeURIComponent(revisionId)}/${encodePathKey(key)}`, {
      method: 'POST',
      credentials: 'include',
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }
  }

  async function deleteConfig(key: string) {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/configs/${encodePathKey(key)}`, {
      method: 'DELETE',
      credentials: 'include',
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }
  }

  async function getTree(path?: string) {
    const safePath = (path ?? '').replace(/^\/+/, '').replace(/\/+$/, '');
    const suffix = safePath ? `/${encodePathKey(safePath)}` : '';
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/configs/tree${suffix}`, {
      credentials: 'include',
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    return await response.json() as ConfigTreeNode[];
  }

  async function renameFolder(fromPath: string, toPath: string) {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/configs/rename-folder`, {
      method: 'POST',
      credentials: 'include',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ fromPath, toPath }),
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }
  }

  async function createLocalOverride(key: string) {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/configs/create-override/${encodePathKey(key)}`, {
      method: 'POST',
      credentials: 'include',
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    return await response.json() as ConfigEntry;
  }

  async function renameKey(fromKey: string, toKey: string) {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/configs/rename-key`, {
      method: 'POST',
      credentials: 'include',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ fromKey, toKey }),
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }
  }

  async function moveConfigs(keys: string[], targetFolder?: string, preserveSourceNames = false) {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/configs/move`, {
      method: 'POST',
      credentials: 'include',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ keys, targetFolder, preserveSourceNames }),
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }
  }

  async function copyConfigs(keys: string[], targetFolder?: string, preserveSourceNames = false) {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/configs/copy`, {
      method: 'POST',
      credentials: 'include',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ keys, targetFolder, preserveSourceNames }),
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }
  }

  async function exportLocalConfigs() {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/configs/export`, {
      credentials: 'include',
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    return await response.json() as ConfigBackupDocument;
  }

  async function restoreLocalConfigs(document: ConfigBackupDocument) {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/configs/restore`, {
      method: 'POST',
      credentials: 'include',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(document),
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }
  }

  async function importFromRemote(url: string) {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/configs/import-remote`, {
      method: 'POST',
      credentials: 'include',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ url }),
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }
  }

  return {
    listConfigs,
    getConfig,
    revealConfig,
    listConfigRevisions,
    getConfigRevision,
    restoreConfigRevision,
    upsertConfig,
    deleteConfig,
    getTree,
    renameKey,
    renameFolder,
    moveConfigs,
    copyConfigs,
    createLocalOverride,
    exportLocalConfigs,
    restoreLocalConfigs,
    importFromRemote,
  };
}

function encodePathKey(key: string): string {
  return key
    .replace(/^\/+|\/+$/g, '')
    .split('/')
    .filter(Boolean)
    .map(segment => encodeURIComponent(segment))
    .join('/');
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
