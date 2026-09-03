import { INTERNAL_ADMIN_API_ROOT } from './apiRoots';

type TlsMode = 'none' | 'pfx' | 'thumbprint';
export type ProxyHostUpstreamMode = 'manual' | 'container';
export type ProxyRouteRewriteMode = 'preserve' | 'stripPrefix' | 'replacePrefix';
export type ProxyHostMode = 'manual' | 'automaticContainer';

export interface UpstreamTarget {
  scheme: 'http' | 'https';
  host: string;
  port: number;
}

export interface ContainerUpstreamTarget {
  containerName: string;
  scheme: 'http' | 'https';
  port: number;
  portResolutionMode: 'container' | 'published';
  networkName?: string | null;
}

export interface ProxyRouteConfig {
  path: string;
  rewriteMode: ProxyRouteRewriteMode;
  rewriteTargetPath?: string | null;
  preserveOriginalHostHeader: boolean;
  sendForwardedHeaders: boolean;
  ignoreBadCertificates: boolean;
  upstreamMode: ProxyHostUpstreamMode;
  upstream: UpstreamTarget;
  container: ContainerUpstreamTarget;
}

export interface AutomaticContainerLabelSelector {
  key: string;
  valuePattern?: string | null;
  valuePatterns: string[];
}

export interface AutomaticContainerProxyHostConfig {
  labelSelectors: AutomaticContainerLabelSelector[];
  domainTemplates: string[];
}

interface TlsBindingConfig {
  mode: TlsMode;
  pfxPath?: string;
  pfxPassword?: string;
  pfxPasswordEnvVar?: string;
  thumbprint?: string;
  storeName?: string;
  storeLocation?: string;
}

export interface ProxyHostConfig {
  id: string;
  mode: ProxyHostMode;
  enabled: boolean;
  domainNames: string[];
  automaticContainer: AutomaticContainerProxyHostConfig;
  forceSsl: boolean;
  cacheAssets: boolean;
  websockets: boolean;
  certificateId?: string | null;
  routes: ProxyRouteConfig[];
  tls: TlsBindingConfig;
}

export interface ProxyHostRuntimeMetadata {
  readOnly: boolean;
  isGenerated: boolean;
  sourceTemplateId?: string | null;
  matchedContainerId?: string | null;
  matchedContainerName?: string | null;
  matchedComposeService?: string | null;
  activeMatchCount: number;
}

export interface AdminProxyHostDto extends ProxyHostConfig {
  runtime: ProxyHostRuntimeMetadata;
}

export interface ProxyHostLinkSettings {
  httpPort: number;
  httpsPort: number;
}

export function useProxyHostsApi() {
  async function listHosts() {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/proxy-hosts`);

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    return await response.json() as AdminProxyHostDto[];
  }

  async function getLinkSettings() {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/proxy-hosts/link-settings`);

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    return await response.json() as ProxyHostLinkSettings;
  }

  async function createHost(host: ProxyHostConfig) {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/proxy-hosts`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(host),
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }
  }

  async function updateHost(host: ProxyHostConfig) {
    const id = encodeURIComponent(host.id);
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/proxy-hosts/${id}`, {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(host),
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }
  }

  async function deleteHost(id: string) {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/proxy-hosts/${encodeURIComponent(id)}`, {
      method: 'DELETE',
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }
  }

  async function setHostEnabled(host: ProxyHostConfig | AdminProxyHostDto, enabled: boolean) {
    const payload = JSON.parse(JSON.stringify(host)) as ProxyHostConfig & { runtime?: unknown };

    delete payload.runtime;
    payload.enabled = enabled;
    await updateHost(payload);
  }

  return {
    listHosts,
    getLinkSettings,
    createHost,
    updateHost,
    deleteHost,
    setHostEnabled,
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
