<template>
  <div class="page-container">
    <Alert v-if="errorMessage" class="mb-4" variant="destructive">
      {{ errorMessage }}
    </Alert>

    <header class="mb-6 flex flex-wrap items-end justify-between gap-4">
      <div>
        <div class="eyebrow">
          Transport security
        </div><h1 class="page-title mt-1">
          Certificates
        </h1><p class="page-lead">
          Manage issued certificates and certificate issuers.
        </p>
      </div>
    </header>

    <Tabs v-model="activeTab">
      <TabsList>
        <TabsTrigger value="certificates">
          Issued certificates
        </TabsTrigger>
        <TabsTrigger value="issuers">
          Issuers
        </TabsTrigger>
      </TabsList>

      <TabsContent class="mt-4" value="certificates">
        <CertificatesTab :items="certificateConfigs" @create="openCreateCertificate" @row-click="editCertificate" />
      </TabsContent>

      <TabsContent class="mt-4" value="issuers">
        <IssuersTab :items="issuerConfigs" @create="openCreateIssuer" @row-click="editIssuer" />
      </TabsContent>
    </Tabs>

    <CertificatesDialog
      v-model="showCertificateDialog" :editing="editingCertificate" :certificate="certificateForm"
      :save-error="dialogErrorMessage" @save="saveCertificate" @delete="deleteCertificateFromDialog"
    />

    <IssuerDialog
      v-model="showIssuerDialog" :editing="editingIssuer" :issuer="issuerForm"
      :save-error="dialogErrorMessage" @save="saveIssuer" @delete="deleteIssuerFromDialog"
    />

    <Alert v-if="showSavedSnackbar" class="fixed bottom-4 right-4 z-50 w-auto min-w-72 shadow-lg">
      Saved
    </Alert>
  </div>
</template>

<script lang="ts" setup>
import type { StoredCertificateConfig } from '@/composables/useCertificatesApi';
import { computed, onMounted, ref } from 'vue';
import CertificatesTab from '@/components/certificates/CertificatesTab.vue';
import IssuersTab from '@/components/certificates/IssuersTab.vue';
import { useCertificatesApi } from '@/composables/useCertificatesApi';
import CertificatesDialog from '@/dialogs/CertificatesDialog.vue';
import IssuerDialog from '@/dialogs/IssuerDialog.vue';

const activeTab = ref<'certificates' | 'issuers'>('certificates');
const errorMessage = ref('');
const showSavedSnackbar = ref(false);
const certificatesApi = useCertificatesApi();
const allCertificateConfigs = ref<StoredCertificateConfig[]>([]);
const certificateConfigs = computed(() => allCertificateConfigs.value.filter(x => x.mode !== 'issuer'));
const issuerConfigs = computed(() => allCertificateConfigs.value.filter(x => x.mode === 'issuer'));
const showCertificateDialog = ref(false);
const editingCertificate = ref(false);
const showIssuerDialog = ref(false);
const editingIssuer = ref(false);
const dialogErrorMessage = ref('');
const certificateForm = ref<StoredCertificateConfig>(emptyCertificate());
const issuerForm = ref<StoredCertificateConfig>(emptyIssuer());

onMounted(loadCertificates);

async function loadCertificates() {
  try {
    errorMessage.value = '';
    allCertificateConfigs.value = await certificatesApi.listCertificates();
  }
  catch (error) {
    errorMessage.value = (error as Error).message;
  }
}

function openCreateCertificate() {
  dialogErrorMessage.value = '';
  editingCertificate.value = false;
  certificateForm.value = emptyCertificate();
  showCertificateDialog.value = true;
}

function editCertificate(config: StoredCertificateConfig) {
  dialogErrorMessage.value = '';
  editingCertificate.value = true;
  certificateForm.value = JSON.parse(JSON.stringify(config)) as StoredCertificateConfig;
  showCertificateDialog.value = true;
}

async function saveCertificate(payload: {
  certificate: StoredCertificateConfig;
  files: {
    certificateFile?: File | null;
    keyFile?: File | null;
    intermediateFile?: File | null;
    pfxFile?: File | null;
    caCertificateFile?: File | null;
    caKeyFile?: File | null;
    caPfxFile?: File | null;
  };
}) {
  try {
    dialogErrorMessage.value = '';

    await certificatesApi.upsertCertificate(payload.certificate);
    await certificatesApi.uploadCertificateFiles(payload.certificate.id, payload.files);

    closeDialog(showCertificateDialog);
    await loadCertificates();
    showSavedSnackbar.value = true;
  }
  catch (error) {
    dialogErrorMessage.value = (error as Error).message;
  }
}

async function removeCertificate(id: string) {
  try {
    errorMessage.value = '';
    await certificatesApi.deleteCertificate(id);
    await loadCertificates();
    showSavedSnackbar.value = true;
  }
  catch (error) {
    errorMessage.value = (error as Error).message;
  }
}

async function deleteCertificateFromDialog() {
  if (!editingCertificate.value || !certificateForm.value.id) {
    return;
  }

  await removeCertificate(certificateForm.value.id);
  closeDialog(showCertificateDialog);
}

function openCreateIssuer() {
  dialogErrorMessage.value = '';
  editingIssuer.value = false;
  issuerForm.value = emptyIssuer();
  showIssuerDialog.value = true;
}

function editIssuer(config: StoredCertificateConfig) {
  dialogErrorMessage.value = '';
  editingIssuer.value = true;
  issuerForm.value = JSON.parse(JSON.stringify(config)) as StoredCertificateConfig;
  showIssuerDialog.value = true;
}

async function saveIssuer(payload: {
  issuer: StoredCertificateConfig;
  files: {
    caCertificateFile?: File | null;
    caKeyFile?: File | null;
    caPfxFile?: File | null;
  };
}) {
  try {
    dialogErrorMessage.value = '';
    payload.issuer.mode = 'issuer';
    await certificatesApi.upsertCertificate(payload.issuer);
    await certificatesApi.uploadCertificateFiles(payload.issuer.id, {
      caCertificateFile: payload.files.caCertificateFile,
      caKeyFile: payload.files.caKeyFile,
      caPfxFile: payload.files.caPfxFile,
    });
    closeDialog(showIssuerDialog);
    await loadCertificates();
    showSavedSnackbar.value = true;
  }
  catch (error) {
    dialogErrorMessage.value = (error as Error).message;
  }
}

async function removeIssuer(id: string) {
  try {
    errorMessage.value = '';
    await certificatesApi.deleteCertificate(id);
    await loadCertificates();
    showSavedSnackbar.value = true;
  }
  catch (error) {
    errorMessage.value = (error as Error).message;
  }
}

async function deleteIssuerFromDialog() {
  if (!editingIssuer.value || !issuerForm.value.id) {
    return;
  }

  await removeIssuer(issuerForm.value.id);
  closeDialog(showIssuerDialog);
}

function closeDialog(dialog: { value: boolean }) {
  const activeElement = document.activeElement;

  if (activeElement instanceof HTMLElement && activeElement.closest('[data-slot="dialog-content"]')) {
    activeElement.blur();
  }

  dialog.value = false;
}

function emptyCertificate(): StoredCertificateConfig {
  return {
    id: '',
    mode: 'files',
    pfxPassword: '',
    pfxPasswordEnvVar: '',
    thumbprint: '',
    storeName: 'My',
    storeLocation: 'LocalMachine',
    issuerMatchDomains: [],
    issuerEnabled: true,
    issuerCaSource: 'uploadPem',
    issuerCaCertPath: '',
    issuerCaKeyPath: '',
    issuerCaPfxPath: '',
    issuerCaPfxPassword: '',
    issuerCaPfxPasswordEnvVar: '',
    hasCertificateFile: false,
    hasKeyFile: false,
    hasIntermediateFile: false,
    hasPfxFile: false,
    domainNames: [],
    provider: '',
    expiresAtUtc: null,
    inUse: false,
    inUseCount: 0,
  };
}

function emptyIssuer(): StoredCertificateConfig {
  return {
    id: '',
    mode: 'issuer',
    pfxPassword: '',
    pfxPasswordEnvVar: '',
    thumbprint: '',
    storeName: 'My',
    storeLocation: 'LocalMachine',
    issuerMatchDomains: [],
    issuerEnabled: true,
    issuerCaSource: 'uploadPem',
    issuerCaCertPath: '',
    issuerCaKeyPath: '',
    issuerCaPfxPath: '',
    issuerCaPfxPassword: '',
    issuerCaPfxPasswordEnvVar: '',
    issuerCaThumbprint: '',
    issuerCaStoreName: 'My',
    issuerCaStoreLocation: 'LocalMachine',
    hasCertificateFile: false,
    hasKeyFile: false,
    hasIntermediateFile: false,
    hasPfxFile: false,
    domainNames: [],
    provider: '',
    expiresAtUtc: null,
    inUse: false,
    inUseCount: 0,
  };
}
</script>
