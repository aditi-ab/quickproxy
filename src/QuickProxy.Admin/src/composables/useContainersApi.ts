import { INTERNAL_ADMIN_API_ROOT } from './apiRoots';

export interface ContainerComposeInfo {
  project?: string | null;
  service?: string | null;
  containerNumber?: string | null;
}

export interface ContainerNetworkInfo {
  name: string;
  ipAddress?: string | null;
}

export interface ContainerPortInfo {
  containerPort: number;
  protocol: string;
  isExposed: boolean;
  publishedPorts: number[];
  publishedBindings: PublishedPortBinding[];
}

export interface PublishedPortBinding {
  hostIp: string;
  hostPort: number;
}

export interface ContainerInventoryItem {
  id: string;
  name: string;
  image: string;
  imageId: string;
  imageDigest?: string | null;
  imageArchitecture?: string | null;
  imageOs?: string | null;
  state: string;
  status: string;
  containerLabels: Record<string, string>;
  imageLabels: Record<string, string>;
  stats?: ContainerStatsSnapshot | null;
  imageUpdate?: ContainerImageUpdateInfo | null;
  ports: ContainerPortInfo[];
  networks: ContainerNetworkInfo[];
  compose: ContainerComposeInfo;
  logsSupported: boolean;
  logsUnavailableReason?: string | null;
  lastSeenUtc: string;
}

export interface ContainerInventoryStatus {
  enabled: boolean;
  lastRefreshStartedUtc?: string | null;
  lastRefreshCompletedUtc?: string | null;
  lastSuccessfulRefreshUtc?: string | null;
  statsEnabled: boolean;
  lastStatsRefreshStartedUtc?: string | null;
  lastStatsRefreshCompletedUtc?: string | null;
  lastSuccessfulStatsRefreshUtc?: string | null;
  lastStatsError?: string | null;
  imageUpdatesEnabled: boolean;
  lastImageUpdateStartedUtc?: string | null;
  lastImageUpdateCompletedUtc?: string | null;
  lastSuccessfulImageUpdateUtc?: string | null;
  lastImageUpdateError?: string | null;
  eventStreamConnected: boolean;
  lastError?: string | null;
}

export interface ContainerStatsSnapshot {
  collectedAtUtc: string;
  cpuPercent?: number | null;
  memoryUsageBytes?: number | null;
  memoryLimitBytes?: number | null;
  memoryPercent?: number | null;
  networkRxBytes?: number | null;
  networkTxBytes?: number | null;
  blockReadBytes?: number | null;
  blockWriteBytes?: number | null;
  pidsCurrent?: number | null;
}

export interface ContainerImageUpdateInfo {
  status: string;
  updateAvailable: boolean;
  source?: string | null;
  localDigest?: string | null;
  remoteDigest?: string | null;
  error?: string | null;
  checkedAtUtc?: string | null;
  remoteCreatedUtc?: string | null;
  remoteArchitecture?: string | null;
  remoteOs?: string | null;
  remoteLabels: Record<string, string>;
}

export interface ContainerInventorySnapshot {
  status: ContainerInventoryStatus;
  containers: ContainerInventoryItem[];
}

export interface ContainerImageInventoryItem {
  id: string;
  repoTags: string[];
  repoDigests: string[];
  createdUtc: string;
  sizeBytes: number;
  sharedSizeBytes: number;
  virtualSizeBytes: number;
  containers: number;
  labels: Record<string, string>;
}

export interface ContainerKeyValuePair {
  key: string;
  value: string;
}

export interface ContainerPublishedPortRequest {
  containerPort: number;
  hostPort: number;
  protocol: 'tcp' | 'udp';
  hostIp?: string | null;
}

export interface ContainerMountBindingRequest {
  hostPath: string;
  containerPath: string;
  readOnly: boolean;
}

export interface ContainerHostMappingRequest {
  hostname: string;
  address: string;
}

export interface ContainerNetworkAliasRequest {
  network: string;
  alias: string;
}

export interface ContainerEditRequest {
  name: string;
  image: string;
  labels: ContainerKeyValuePair[];
  envVars: ContainerKeyValuePair[];
  mountBindings: ContainerMountBindingRequest[];
  hostMappings: ContainerHostMappingRequest[];
  networkAliases: ContainerNetworkAliasRequest[];
  restartPolicy: 'no' | 'always' | 'unless-stopped' | 'on-failure';
  publishedPorts: ContainerPublishedPortRequest[];
}

export interface ContainerSaveRequest {
  request: ContainerEditRequest;
  imageArchive?: File | null;
}

export interface ContainerImageArchiveInfo {
  repoTags: string[];
  suggestedImage?: string | null;
}

export interface ContainerLogEntry {
  stream: 'stdout' | 'stderr' | string;
  message: string;
  timestamp: string;
}

export interface ContainerDefaultsSet {
  id: string;
  labels: ContainerKeyValuePair[];
  envVars: ContainerKeyValuePair[];
  mountBindings: ContainerMountBindingRequest[];
  hostMappings: ContainerHostMappingRequest[];
  networkAliases: ContainerNetworkAliasRequest[];
  updatedAtUtc: string;
}

export interface ComposeManagedFile {
  path: string;
  content: string;
}

export interface ComposeProject {
  id: string;
  displayName: string;
  slug: string;
  status: string;
  composeYaml: string;
  workspacePath: string;
  managedFiles: ComposeManagedFile[];
  createdAtUtc: string;
  updatedAtUtc: string;
  lastDeployAtUtc?: string | null;
  lastError?: string | null;
}

export interface ComposeProjectServiceRuntime {
  name: string;
  containerCount: number;
  runningCount: number;
  containerNames: string[];
}

export interface ComposeProjectContainerRuntime {
  id: string;
  name: string;
  service: string;
  state: string;
  status: string;
}

export interface ComposeProjectRuntimeSnapshot {
  projectName: string;
  status: string;
  serviceCount: number;
  containerCount: number;
  services: ComposeProjectServiceRuntime[];
  containers: ComposeProjectContainerRuntime[];
  lastCommandOutput?: string | null;
}

export interface ComposeProjectListItem {
  project: ComposeProject;
  runtime: ComposeProjectRuntimeSnapshot;
}

export interface ComposeProjectValidationResult {
  valid: boolean;
  output: string;
  errors: string[];
}

export interface ComposeProjectActionResult {
  message: string;
  output: string;
  runtime: ComposeProjectRuntimeSnapshot;
}

export interface ComposeProjectLogEntry {
  service: string;
  message: string;
  timestamp: string;
}

export function useContainersApi() {
  async function listContainers() {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/containers`);

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    return await response.json() as ContainerInventorySnapshot;
  }

  async function getContainer(name: string) {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/containers/${encodeURIComponent(name)}`);

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    return await response.json() as {
      status: ContainerInventoryStatus;
      container: ContainerInventoryItem;
    };
  }

  async function getEditableContainer(name: string) {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/containers/${encodeURIComponent(name)}/edit`);

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    return await response.json() as ContainerEditRequest;
  }

  async function listImages(includeAll = false) {
    const query = includeAll ? '?all=true' : '';
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/containers/images${query}`);

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    return await response.json() as {
      images: ContainerImageInventoryItem[];
    };
  }

  async function pruneUnusedImages() {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/containers/images/prune`, {
      method: 'POST',
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    return await response.json() as {
      removedCount: number;
      message: string;
    };
  }

  async function listDefaultSets() {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/containers/default-sets`);

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    return await response.json() as {
      sets: ContainerDefaultsSet[];
    };
  }

  async function getDefaultSet(id: string) {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/containers/default-sets/${encodeURIComponent(id)}`);

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    return await response.json() as ContainerDefaultsSet;
  }

  async function upsertDefaultSet(id: string, payload: {
    labels: ContainerKeyValuePair[];
    envVars: ContainerKeyValuePair[];
    mountBindings: ContainerMountBindingRequest[];
    hostMappings: ContainerHostMappingRequest[];
    networkAliases: ContainerNetworkAliasRequest[];
  }) {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/containers/default-sets/${encodeURIComponent(id)}`, {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        id,
        labels: payload.labels,
        envVars: payload.envVars,
        mountBindings: payload.mountBindings,
        hostMappings: payload.hostMappings,
        networkAliases: payload.networkAliases,
      }),
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }
  }

  async function deleteDefaultSet(id: string) {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/containers/default-sets/${encodeURIComponent(id)}`, {
      method: 'DELETE',
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }
  }

  async function listComposeProjects() {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/containers/projects`);

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    return await response.json() as {
      projects: ComposeProjectListItem[];
    };
  }

  async function getComposeProject(id: string) {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/containers/projects/${encodeURIComponent(id)}`);

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    return await response.json() as ComposeProjectListItem;
  }

  async function upsertComposeProject(id: string, payload: {
    displayName: string;
    slug: string;
    status: string;
    composeYaml: string;
    managedFiles: ComposeManagedFile[];
  }) {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/containers/projects/${encodeURIComponent(id)}`, {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        id,
        displayName: payload.displayName,
        slug: payload.slug,
        status: payload.status,
        composeYaml: payload.composeYaml,
        managedFiles: payload.managedFiles,
      }),
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    return await response.json() as ComposeProject;
  }

  async function deleteComposeProject(id: string) {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/containers/projects/${encodeURIComponent(id)}`, {
      method: 'DELETE',
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }
  }

  async function validateComposeProject(id: string) {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/containers/projects/${encodeURIComponent(id)}/validate`, {
      method: 'POST',
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    return await response.json() as ComposeProjectValidationResult;
  }

  async function runComposeProjectAction(id: string, action: 'deploy' | 'start' | 'stop' | 'restart' | 'pull' | 'down') {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/containers/projects/${encodeURIComponent(id)}/${action}`, {
      method: 'POST',
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    return await response.json() as ComposeProjectActionResult;
  }

  async function* streamComposeProjectLogs(id: string, service?: string, signal?: AbortSignal): AsyncGenerator<ComposeProjectLogEntry> {
    const searchParams = new URLSearchParams();

    if (service) {
      searchParams.set('service', service);
    }

    const query = searchParams.size > 0 ? `?${searchParams.toString()}` : '';
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/containers/projects/${encodeURIComponent(id)}/logs/stream${query}`, {
      credentials: 'include',
      signal,
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    const reader = response.body?.getReader();

    if (!reader) {
      throw new Error('Log stream is unavailable.');
    }

    const decoder = new TextDecoder();
    let pending = '';

    try {
      while (true) {
        const { value, done } = await reader.read();

        if (done) {
          break;
        }

        pending += decoder.decode(value, { stream: true });

        let newlineIndex = pending.indexOf('\n');

        while (newlineIndex >= 0) {
          const line = pending.slice(0, newlineIndex).trim();

          pending = pending.slice(newlineIndex + 1);

          if (line) {
            yield JSON.parse(line) as ComposeProjectLogEntry;
          }

          newlineIndex = pending.indexOf('\n');
        }
      }

      pending += decoder.decode();

      const finalLine = pending.trim();

      if (finalLine) {
        yield JSON.parse(finalLine) as ComposeProjectLogEntry;
      }
    }
    finally {
      reader.releaseLock();
    }
  }

  async function createContainer(payload: ContainerSaveRequest) {
    const body = createContainerRequestBody(payload);
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/containers`, {
      method: 'POST',
      headers: body instanceof FormData
        ? undefined
        : {
            'Content-Type': 'application/json',
          },
      body,
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }
  }

  async function inspectImageArchive(imageArchive: File) {
    const formData = new FormData();

    formData.append('imageArchive', imageArchive, imageArchive.name);

    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/containers/inspect-image-archive`, {
      method: 'POST',
      body: formData,
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    return await response.json() as ContainerImageArchiveInfo;
  }

  async function updateContainer(existingName: string, payload: ContainerSaveRequest) {
    const body = createContainerRequestBody(payload);
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/containers/${encodeURIComponent(existingName)}`, {
      method: 'PUT',
      headers: body instanceof FormData
        ? undefined
        : {
            'Content-Type': 'application/json',
          },
      body,
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }
  }

  async function deleteContainer(name: string) {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/containers/${encodeURIComponent(name)}`, {
      method: 'DELETE',
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }
  }

  async function startContainer(name: string) {
    await postContainerAction(name, 'start');
  }

  async function stopContainer(name: string) {
    await postContainerAction(name, 'stop');
  }

  async function repullAndRestartContainer(name: string) {
    await postContainerAction(name, 'repull-restart');
  }

  async function* streamContainerLogs(name: string, signal?: AbortSignal): AsyncGenerator<ContainerLogEntry> {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/containers/${encodeURIComponent(name)}/logs/stream`, {
      credentials: 'include',
      signal,
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    const reader = response.body?.getReader();

    if (!reader) {
      throw new Error('Log stream is unavailable.');
    }

    const decoder = new TextDecoder();
    let pending = '';

    try {
      while (true) {
        const { value, done } = await reader.read();

        if (done) {
          break;
        }

        pending += decoder.decode(value, { stream: true });

        let newlineIndex = pending.indexOf('\n');

        while (newlineIndex >= 0) {
          const line = pending.slice(0, newlineIndex).trim();

          pending = pending.slice(newlineIndex + 1);

          if (line) {
            yield JSON.parse(line) as ContainerLogEntry;
          }

          newlineIndex = pending.indexOf('\n');
        }
      }

      pending += decoder.decode();

      const finalLine = pending.trim();

      if (finalLine) {
        yield JSON.parse(finalLine) as ContainerLogEntry;
      }
    }
    finally {
      reader.releaseLock();
    }
  }

  return {
    listContainers,
    getContainer,
    getEditableContainer,
    listImages,
    pruneUnusedImages,
    listDefaultSets,
    getDefaultSet,
    upsertDefaultSet,
    deleteDefaultSet,
    listComposeProjects,
    getComposeProject,
    upsertComposeProject,
    deleteComposeProject,
    validateComposeProject,
    runComposeProjectAction,
    streamComposeProjectLogs,
    createContainer,
    inspectImageArchive,
    updateContainer,
    deleteContainer,
    startContainer,
    stopContainer,
    repullAndRestartContainer,
    streamContainerLogs,
  };
}

function createContainerRequestBody(payload: ContainerSaveRequest): BodyInit {
  if (!payload.imageArchive) {
    return JSON.stringify(payload.request);
  }

  const formData = new FormData();

  formData.append('request', JSON.stringify(payload.request));
  formData.append('imageArchive', payload.imageArchive, payload.imageArchive.name);
  return formData;
}

async function postContainerAction(name: string, action: string) {
  const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/containers/${encodeURIComponent(name)}/${action}`, {
    method: 'POST',
  });

  if (!response.ok) {
    throw new Error(await readApiError(response));
  }
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
