import { INTERNAL_ADMIN_API_ROOT } from './apiRoots';

export interface StorageInfo {
  provider: 'file' | 'database';
  databaseProvider?: 'sqlite' | 'sqlserver';
  label: string;
  details: string;
  path?: string;
  server?: string;
  database?: string;
}

export interface ModuleStorageInfo {
  enabled: boolean;
  storage: StorageInfo;
}

export interface RemoteConfigInfo {
  enabled: boolean;
  url?: string | null;
}

export interface ConfigModuleStorageInfo extends ModuleStorageInfo {
  remote?: RemoteConfigInfo;
}

export interface ContainersModuleInfo {
  enabled: boolean;
}

export interface AuditModuleStorageInfo extends ModuleStorageInfo {
}

export interface SystemInfo {
  version: string;
  startedAtUtc?: string;
  proxy: ModuleStorageInfo;
  config: ConfigModuleStorageInfo;
  audit: AuditModuleStorageInfo;
  containers: ContainersModuleInfo;
  selfUpdate?: SelfUpdateStatus;
}

export interface SelfUpdateStatus {
  supported: boolean;
  reason?: string | null;
  containerName?: string | null;
  image?: string | null;
  updateAvailable: boolean;
  localDigest?: string | null;
  remoteDigest?: string | null;
  imageUpdateStatus?: string | null;
  imageUpdateError?: string | null;
}

export function useSystemApi() {
  async function getSystemInfo() {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/system/storage`);

    if (!response.ok) {
      throw new Error(`Request failed with status ${response.status}`);
    }

    return await response.json() as SystemInfo;
  }

  async function getSelfUpdateStatus() {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/system/self-update/status`);

    if (!response.ok) {
      throw new Error(`Request failed with status ${response.status}`);
    }

    return await response.json() as SelfUpdateStatus;
  }

  async function triggerSelfUpdate(imageReference?: string | null) {
    const normalizedImageReference = imageReference?.trim() ?? '';
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/system/self-update`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        imageReference: normalizedImageReference || null,
      }),
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    return await response.json() as {
      message: string;
      containerName?: string | null;
      image?: string | null;
    };
  }

  async function triggerReprovision() {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/system/reprovision`, {
      method: 'POST',
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    return await response.json() as {
      message: string;
    };
  }

  return {
    getSystemInfo,
    getSelfUpdateStatus,
    triggerSelfUpdate,
    triggerReprovision,
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
