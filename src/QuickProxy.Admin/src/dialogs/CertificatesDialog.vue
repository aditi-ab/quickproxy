<template>
  <Dialog :open="modelValue" @update:open="onDialogModelUpdate">
    <DialogContent size="4xl" scrollable>
      <DialogHeader>
        <DialogTitle>{{ editing ? 'Edit Certificate' : 'Create Certificate' }}</DialogTitle>
        <DialogDescription class="sr-only">
          Configure the certificate source, credentials, and certificate store settings.
        </DialogDescription>
      </DialogHeader>
      <div data-slot="dialog-body" class="dialog-body-content">
        <Alert v-if="saveError" class="mb-4" variant="destructive">
          {{ saveError }}
        </Alert>
        <div class="grid grid-cols-12 gap-4">
          <div class="col-span-12 md:col-span-6">
            <Field>
              <FieldLabel for="certificate-id">
                Certificate ID
              </FieldLabel>
              <Input id="certificate-id" v-model="localForm.id" :disabled="editing" />
              <FieldDescription>lowercase kebab-case (example: my-certificate)</FieldDescription>
            </Field>
          </div>
          <div class="col-span-12 md:col-span-6">
            <Field>
              <FieldLabel for="certificate-mode">
                Mode
              </FieldLabel><Select v-model="certificateMode">
                <SelectTrigger id="certificate-mode">
                  <SelectValue placeholder="Mode" />
                </SelectTrigger><SelectContent>
                  <SelectItem v-for="option in certificateModes" :key="String((typeof option === 'string' ? option : (option as any).value))" :value="(typeof option === 'string' ? option : (option as any).value)">
                    {{ (typeof option === 'string' ? option : (option as any).title ?? (option as any).value) }}
                  </SelectItem>
                </SelectContent>
              </Select>
            </Field>
          </div>

          <template v-if="localForm.mode === 'files'">
            <div class="col-span-12">
              <Field>
                <FieldLabel for="certificate-file">
                  Certificate File (.pem/.crt)
                </FieldLabel><Input id="certificate-file" type="file" @change="certificateFile = (($event.target as HTMLInputElement).files?.[0] ?? null)" />
              </Field>
            </div>
            <div class="col-span-12">
              <Field>
                <FieldLabel for="certificate-key-file">
                  Key File (.key/.pem)
                </FieldLabel><Input id="certificate-key-file" type="file" @change="keyFile = (($event.target as HTMLInputElement).files?.[0] ?? null)" />
              </Field>
            </div>
            <div class="col-span-12">
              <Field>
                <FieldLabel for="certificate-intermediate-file">
                  Intermediate File (optional)
                </FieldLabel><Input id="certificate-intermediate-file" type="file" @change="intermediateFile = (($event.target as HTMLInputElement).files?.[0] ?? null)" />
              </Field>
            </div>
          </template>

          <template v-if="localForm.mode === 'pfx'">
            <div class="col-span-12">
              <Field>
                <FieldLabel for="certificate-pfx-file">
                  PFX File (.pfx)
                </FieldLabel><Input id="certificate-pfx-file" type="file" @change="pfxFile = (($event.target as HTMLInputElement).files?.[0] ?? null)" />
              </Field>
            </div>
            <div class="col-span-12 md:col-span-6">
              <Field>
                <FieldLabel for="certificate-pfx-password">
                  PFX Password (optional)
                </FieldLabel><Input id="certificate-pfx-password" v-model="localForm.pfxPassword" /><FieldDescription>Stored as plain text, be cautious!</FieldDescription>
              </Field>
            </div>
            <div class="col-span-12 md:col-span-6">
              <Field>
                <FieldLabel for="certificate-pfx-password-env">
                  PFX Password env var (optional)
                </FieldLabel><Input id="certificate-pfx-password-env" v-model="localForm.pfxPasswordEnvVar" /><FieldDescription>Read password from environment variable</FieldDescription>
              </Field>
            </div>
          </template>

          <template v-if="localForm.mode === 'thumbprint'">
            <div class="col-span-12 md:col-span-6">
              <Field>
                <FieldLabel for="certificate-store-name">
                  Store Name
                </FieldLabel><Input id="certificate-store-name" v-model="localForm.storeName" />
              </Field>
            </div>
            <div class="col-span-12 md:col-span-6">
              <Field>
                <FieldLabel for="certificate-store-location">
                  Store Location
                </FieldLabel><Input id="certificate-store-location" v-model="localForm.storeLocation" />
              </Field>
            </div>
            <div class="col-span-12">
              <Field>
                <FieldLabel for="certificate-thumbprint">
                  Thumbprint
                </FieldLabel><Input id="certificate-thumbprint" v-model="localForm.thumbprint" />
              </Field>
            </div>
          </template>
        </div>
      </div>
      <DialogFooter>
        <Button v-if="editing" @click="emit('delete')" variant="destructive">
          Delete
        </Button>
        <span class="ml-auto" />
        <Button variant="ghost" @click="close">
          Cancel
        </Button>
        <Button @click="save">
          Save Certificate
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>

<script lang="ts" setup>
import type { StoredCertificateConfig } from '@/composables/useCertificatesApi';
import { computed, ref, watch } from 'vue';

const props = defineProps<{
  modelValue: boolean;
  editing: boolean;
  certificate: StoredCertificateConfig;
  saveError?: string;
}>();

const emit = defineEmits<{
  'update:modelValue': [value: boolean];
  'delete': [];
  'save': [payload: {
    certificate: StoredCertificateConfig;
    files: {
      certificateFile?: File | null;
      keyFile?: File | null;
      intermediateFile?: File | null;
      pfxFile?: File | null;
    };
  }];
}>();

const certificateModes: Array<'files' | 'pfx' | 'thumbprint'> = ['files', 'pfx', 'thumbprint'];
const localForm = ref<StoredCertificateConfig>(cloneCertificate(props.certificate));
const certificateFile = ref<File | null>(null);
const keyFile = ref<File | null>(null);
const intermediateFile = ref<File | null>(null);
const pfxFile = ref<File | null>(null);
const certificateMode = computed<'files' | 'pfx' | 'thumbprint'>({
  get() {
    return localForm.value.mode === 'issuer' ? 'files' : localForm.value.mode;
  },
  set(value) {
    localForm.value.mode = value;
  },
});

watch(
  () => props.certificate,
  (value) => {
    localForm.value = cloneCertificate(value);
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

function onDialogModelUpdate(value: boolean) {
  if (!value) {
    releaseDialogFocus();
  }

  emit('update:modelValue', value);
}

function close() {
  onDialogModelUpdate(false);
}

function save() {
  emit('save', {
    certificate: cloneCertificate(localForm.value),
    files: {
      certificateFile: certificateFile.value,
      keyFile: keyFile.value,
      intermediateFile: intermediateFile.value,
      pfxFile: pfxFile.value,
    },
  });
}

function resetFiles() {
  certificateFile.value = null;
  keyFile.value = null;
  intermediateFile.value = null;
  pfxFile.value = null;
}

function releaseDialogFocus() {
  const activeElement = document.activeElement;

  if (activeElement instanceof HTMLElement && activeElement.closest('[data-slot="dialog-content"]')) {
    activeElement.blur();
  }
}

function cloneCertificate(value: StoredCertificateConfig): StoredCertificateConfig {
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
