import { INTERNAL_ADMIN_API_ROOT } from './apiRoots';

export type FallbackResponseMode = 'default' | 'statusCode' | 'htmlFile' | 'redirect';
export type GatewayFallbackResponseMode = Exclude<FallbackResponseMode, 'redirect'>;

export interface FallbackSettings {
  enabled: boolean;
  statusCode: number;
  mode: FallbackResponseMode;
  htmlFilePath: string;
  redirectUrl: string;
  contentType: string;
  badGatewayMode: GatewayFallbackResponseMode;
  badGatewayHtmlFilePath: string;
  badGatewayContentType: string;
  gatewayTimeoutMode: GatewayFallbackResponseMode;
  gatewayTimeoutHtmlFilePath: string;
  gatewayTimeoutContentType: string;
  proxyDebugLoggingEnabled: boolean;
}

export function useFallbackSettingsApi() {
  async function getSettings() {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/fallback-settings`);

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    return await response.json() as FallbackSettings;
  }

  async function updateSettings(settings: FallbackSettings) {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/fallback-settings`, {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(settings),
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }
  }

  return {
    getSettings,
    updateSettings,
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
