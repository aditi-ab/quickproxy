<template>
  <Dialog :open="modelValue" @update:open="onDialogModelUpdate">
    <DialogContent size="4xl" scrollable>
      <DialogHeader>
        <DialogTitle>{{ editing ? 'Edit Issuer' : 'Create Issuer' }}</DialogTitle>
        <DialogDescription class="sr-only">
          Configure a certificate issuer, matching domains, and its certificate authority source.
        </DialogDescription>
      </DialogHeader>
      <div data-slot="dialog-body" class="dialog-body-content">
        <Alert v-if="saveError" class="mb-4" variant="destructive">
          {{ saveError }}
        </Alert>
        <div class="grid grid-cols-12 gap-4">
          <div class="col-span-12">
            <Field>
              <FieldLabel for="issuer-id">
                Issuer ID
              </FieldLabel>
              <Input id="issuer-id" v-model="localForm.id" :disabled="editing" />
              <FieldDescription>lowercase kebab-case (example: internal-ca)</FieldDescription>
            </Field>
          </div>
          <div class="col-span-12">
            <Field>
              <FieldLabel>Match Domains</FieldLabel><TagsInput v-model="localForm.issuerMatchDomains">
                <TagsInputItem v-for="value in localForm.issuerMatchDomains" :key="value" :value="value">
                  <TagsInputItemText /><TagsInputItemDelete />
                </TagsInputItem><TagsInputInput placeholder="Add a domain" />
              </TagsInput><FieldDescription>Matches the apex and its subdomains.</FieldDescription>
            </Field>
          </div>
          <div class="col-span-12">
            <Field>
              <FieldLabel>CA Source</FieldLabel><Select v-model="localForm.issuerCaSource">
                <SelectTrigger><SelectValue placeholder="CA Source" /></SelectTrigger><SelectContent>
                  <SelectItem v-for="option in issuerCaSources" :key="String(option.value)" :value="option.value">
                    {{ option.title }}
                  </SelectItem>
                </SelectContent>
              </Select>
            </Field>
          </div>

          <template v-if="localForm.issuerCaSource === 'pathPem'">
            <div class="col-span-12 md:col-span-6">
              <Field>
                <FieldLabel for="issuer-ca-cert-path">
                  CA Certificate Path (.pem)
                </FieldLabel><Input id="issuer-ca-cert-path" v-model="localForm.issuerCaCertPath" />
              </Field>
            </div>
            <div class="col-span-12 md:col-span-6">
              <Field>
                <FieldLabel for="issuer-ca-key-path">
                  CA Private Key Path (.pem)
                </FieldLabel><Input id="issuer-ca-key-path" v-model="localForm.issuerCaKeyPath" />
              </Field>
            </div>
          </template>

          <template v-if="localForm.issuerCaSource === 'pathPfx'">
            <div class="col-span-12 md:col-span-6">
              <Field>
                <FieldLabel for="issuer-ca-pfx-path">
                  CA PFX Path (.pfx)
                </FieldLabel><Input id="issuer-ca-pfx-path" v-model="localForm.issuerCaPfxPath" />
              </Field>
            </div>
            <div class="col-span-12 md:col-span-6">
              <Field>
                <FieldLabel for="issuer-ca-pfx-password">
                  CA PFX Password (optional)
                </FieldLabel><Input id="issuer-ca-pfx-password" v-model="localForm.issuerCaPfxPassword" />
              </Field>
            </div>
            <div class="col-span-12 md:col-span-6">
              <Field>
                <FieldLabel for="issuer-ca-pfx-password-env">
                  CA PFX Password env var (optional)
                </FieldLabel><Input id="issuer-ca-pfx-password-env" v-model="localForm.issuerCaPfxPasswordEnvVar" />
              </Field>
            </div>
          </template>

          <template v-if="localForm.issuerCaSource === 'storeThumbprint'">
            <div class="col-span-12 md:col-span-6">
              <Field>
                <FieldLabel for="issuer-store-name">
                  Store Name
                </FieldLabel><Input id="issuer-store-name" v-model="localForm.issuerCaStoreName" />
              </Field>
            </div>
            <div class="col-span-12 md:col-span-6">
              <Field>
                <FieldLabel for="issuer-store-location">
                  Store Location
                </FieldLabel><Input id="issuer-store-location" v-model="localForm.issuerCaStoreLocation" />
              </Field>
            </div>
            <div class="col-span-12">
              <Field>
                <FieldLabel for="issuer-ca-thumbprint">
                  CA Thumbprint
                </FieldLabel><Input id="issuer-ca-thumbprint" v-model="localForm.issuerCaThumbprint" /><FieldDescription>Certificate must include a private key in store.</FieldDescription>
              </Field>
            </div>
          </template>

          <template v-if="localForm.issuerCaSource === 'uploadPem'">
            <div class="col-span-12 md:col-span-6">
              <Field><FieldLabel>CA Certificate File (.pem/.crt)</FieldLabel><Input type="file" @change="caCertificateFile = (($event.target as HTMLInputElement).files?.[0] ?? null)" /></Field>
            </div>
            <div class="col-span-12 md:col-span-6">
              <Field><FieldLabel>CA Private Key File (.pem/.key)</FieldLabel><Input type="file" @change="caKeyFile = (($event.target as HTMLInputElement).files?.[0] ?? null)" /></Field>
            </div>
          </template>

          <template v-if="localForm.issuerCaSource === 'uploadPfx'">
            <div class="col-span-12 md:col-span-6">
              <Field><FieldLabel>CA PFX File (.pfx)</FieldLabel><Input type="file" @change="caPfxFile = (($event.target as HTMLInputElement).files?.[0] ?? null)" /></Field>
            </div>
            <div class="col-span-12 md:col-span-6">
              <Field>
                <FieldLabel for="issuer-upload-pfx-password">
                  CA PFX Password (optional)
                </FieldLabel><Input id="issuer-upload-pfx-password" v-model="localForm.issuerCaPfxPassword" />
              </Field>
            </div>
            <div class="col-span-12 md:col-span-6">
              <Field>
                <FieldLabel for="issuer-upload-pfx-password-env">
                  CA PFX Password env var (optional)
                </FieldLabel><Input id="issuer-upload-pfx-password-env" v-model="localForm.issuerCaPfxPasswordEnvVar" />
              </Field>
            </div>
          </template>
        </div>
      </div>
      <DialogFooter>
        <div class="mr-auto flex items-center gap-4">
          <div class="flex items-center gap-2">
            <Switch id="issuer-enabled" v-model="localForm.issuerEnabled" />
            <FieldLabel for="issuer-enabled">
              Enabled
            </FieldLabel>
          </div>
          <Button v-if="editing" @click="emit('delete')" variant="destructive">
            Delete
          </Button>
        </div>
        <Button variant="ghost" @click="close">
          Cancel
        </Button>
        <Button @click="save">
          Save Issuer
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>

<script lang="ts" setup>
import type { StoredCertificateConfig } from '@/composables/useCertificatesApi';
import { ref, watch } from 'vue';

const props = defineProps<{
  modelValue: boolean;
  editing: boolean;
  issuer: StoredCertificateConfig;
  saveError?: string;
}>();

const emit = defineEmits<{
  'update:modelValue': [value: boolean];
  'delete': [];
  'save': [payload: {
    issuer: StoredCertificateConfig;
    files: {
      caCertificateFile?: File | null;
      caKeyFile?: File | null;
      caPfxFile?: File | null;
    };
  }];
}>();

const issuerCaSources = [
  { title: 'Upload PEM (cert + key)', value: 'uploadPem' },
  { title: 'Upload PFX', value: 'uploadPfx' },
  { title: 'Path PEM (cert + key)', value: 'pathPem' },
  { title: 'Path PFX', value: 'pathPfx' },
  { title: 'Windows Store Thumbprint', value: 'storeThumbprint' },
] as const;

function onDialogModelUpdate(value: boolean) {
  if (!value) {
    releaseDialogFocus();
  }

  emit('update:modelValue', value);
}

function close() {
  onDialogModelUpdate(false);
}

function releaseDialogFocus() {
  const activeElement = document.activeElement;

  if (activeElement instanceof HTMLElement && activeElement.closest('[data-slot="dialog-content"]')) {
    activeElement.blur();
  }
}

const localForm = ref<StoredCertificateConfig>(cloneIssuer(props.issuer));
const caCertificateFile = ref<File | null>(null);
const caKeyFile = ref<File | null>(null);
const caPfxFile = ref<File | null>(null);

watch(
  () => props.issuer,
  (value) => {
    localForm.value = cloneIssuer(value);
    resetFiles();
  },
  { deep: true, immediate: true },
);

watch(
  () => localForm.value.id,
  (value) => {
    if (props.editing) {
      return;
    }

    localForm.value.id = toKebabCase(value);
  },
);

function save() {
  localForm.value.mode = 'issuer';
  emit('save', {
    issuer: cloneIssuer(localForm.value),
    files: {
      caCertificateFile: caCertificateFile.value,
      caKeyFile: caKeyFile.value,
      caPfxFile: caPfxFile.value,
    },
  });
}

function resetFiles() {
  caCertificateFile.value = null;
  caKeyFile.value = null;
  caPfxFile.value = null;
}

function cloneIssuer(value: StoredCertificateConfig): StoredCertificateConfig {
  return JSON.parse(JSON.stringify(value)) as StoredCertificateConfig;
}

function toKebabCase(value: string) {
  return value
    .normalize('NFKD')
    .replace(/[\u0300-\u036F]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .replace(/-{2,}/g, '-');
}
</script>
