import { INTERNAL_ADMIN_API_ROOT } from './apiRoots';

export type CertificateConfigMode = 'files' | 'pfx' | 'thumbprint' | 'issuer';
export type IssuerCaSourceMode = 'uploadPem' | 'uploadPfx' | 'pathPem' | 'pathPfx' | 'storeThumbprint';

export interface StoredCertificateConfig {
  id: string;
  mode: CertificateConfigMode;
  pfxPassword?: string;
  pfxPasswordEnvVar?: string;
  thumbprint?: string;
  storeName: string;
  storeLocation: string;
  issuerMatchDomains: string[];
  issuerEnabled: boolean;
  issuerCaSource: IssuerCaSourceMode;
  issuerCaCertPath?: string;
  issuerCaKeyPath?: string;
  issuerCaPfxPath?: string;
  issuerCaPfxPassword?: string;
  issuerCaPfxPasswordEnvVar?: string;
  issuerCaThumbprint?: string;
  issuerCaStoreName?: string;
  issuerCaStoreLocation?: string;
  hasCertificateFile: boolean;
  hasKeyFile: boolean;
  hasIntermediateFile: boolean;
  hasPfxFile: boolean;
  domainNames: string[];
  provider: string;
  expiresAtUtc?: string | null;
  inUse: boolean;
  inUseCount: number;
}

export function useCertificatesApi() {
  async function listCertificates() {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/certificates`);

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    return await response.json() as StoredCertificateConfig[];
  }

  async function upsertCertificate(config: StoredCertificateConfig) {
    const id = encodeURIComponent(config.id);
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/certificates/${id}`, {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(config),
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }
  }

  async function deleteCertificate(id: string) {
    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/certificates/${encodeURIComponent(id)}`, {
      method: 'DELETE',
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }
  }

  async function uploadCertificateFiles(id: string, files: {
    certificateFile?: File | null;
    keyFile?: File | null;
    intermediateFile?: File | null;
    pfxFile?: File | null;
    caCertificateFile?: File | null;
    caKeyFile?: File | null;
    caPfxFile?: File | null;
  }) {
    const formData = new FormData();

    if (files.certificateFile)
      formData.append('certificateFile', files.certificateFile);

    if (files.keyFile)
      formData.append('keyFile', files.keyFile);

    if (files.intermediateFile)
      formData.append('intermediateFile', files.intermediateFile);

    if (files.pfxFile)
      formData.append('pfxFile', files.pfxFile);

    if (files.caCertificateFile)
      formData.append('caCertificateFile', files.caCertificateFile);

    if (files.caKeyFile)
      formData.append('caKeyFile', files.caKeyFile);

    if (files.caPfxFile)
      formData.append('caPfxFile', files.caPfxFile);

    if (Array.from(formData.keys()).length === 0) {
      return;
    }

    const response = await fetch(`${INTERNAL_ADMIN_API_ROOT}/certificates/${encodeURIComponent(id)}/files`, {
      method: 'POST',
      body: formData,
    });

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }
  }

  return {
    listCertificates,
    upsertCertificate,
    deleteCertificate,
    uploadCertificateFiles,
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
