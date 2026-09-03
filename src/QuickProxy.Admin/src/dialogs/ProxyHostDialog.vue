<template>
  <Dialog :open="modelValue" @update:open="onDialogModelUpdate">
    <DialogContent size="4xl" scrollable>
      <DialogHeader>
        <DialogTitle>{{ isEdit ? 'Edit Proxy Host' : 'Create Proxy Host' }}</DialogTitle>
        <DialogDescription class="sr-only">
          Configure host matching, upstream routes, and TLS settings.
        </DialogDescription>
        <Tabs v-model="activeTab">
          <TabsList>
            <TabsTrigger value="general">
              General
            </TabsTrigger>
            <TabsTrigger value="routes">
              Routes
            </TabsTrigger>
            <TabsTrigger value="tls">
              SSL/TLS
            </TabsTrigger>
          </TabsList>
        </Tabs>
      </DialogHeader>

      <div data-slot="dialog-body" class="dialog-body-content -mx-4 overflow-x-hidden px-4">
        <Alert v-if="saveError" class="mb-4" variant="destructive">
          <CircleAlert /><AlertDescription>{{ saveError }}</AlertDescription>
        </Alert>

        <Tabs v-model="activeTab">
          <TabsContent value="general" class="mt-0">
            <ProxyHostGeneralTab
              :local-form="localForm" :is-edit="isEdit" :host-mode-options="hostModeOptions"
              :label-key-options="labelKeyOptions"
            />
          </TabsContent>

          <TabsContent value="routes" class="mt-0">
            <ProxyHostRoutesTab
              :local-form="localForm" :container-options="containerOptions"
              :container-error="containerError" :rewrite-options="rewriteOptions"
              :upstream-mode-options="upstreamModeOptions" :port-resolution-options="portResolutionOptions"
              @reload-containers="loadContainers"
            />
          </TabsContent>

          <TabsContent value="tls" class="mt-0">
            <ProxyHostTlsTab
              :local-form="localForm" :certificate-options="certificateOptions"
              @reload-certificates="loadCertificates"
            />
          </TabsContent>
        </Tabs>
      </div>

      <DialogFooter>
        <Button v-if="isEdit" @click="emit('delete')" variant="destructive">
          Delete
        </Button>
        <span class="ml-auto" />
        <Button variant="ghost" @click="close">
          Cancel
        </Button>
        <Button @click="save">
          Save
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>

<script lang="ts" setup>
import type { StoredCertificateConfig } from '@/composables/useCertificatesApi';
import type { ContainerInventoryItem } from '@/composables/useContainersApi';
import type { ProxyHostConfig } from '@/composables/useProxyHostsApi';
import { CircleAlert } from '@lucide/vue';
import { computed, ref, watch } from 'vue';
import ProxyHostGeneralTab from '@/components/proxy-host-dialog/ProxyHostGeneralTab.vue';
import ProxyHostRoutesTab from '@/components/proxy-host-dialog/ProxyHostRoutesTab.vue';
import ProxyHostTlsTab from '@/components/proxy-host-dialog/ProxyHostTlsTab.vue';
import { useCertificatesApi } from '@/composables/useCertificatesApi';
import { useContainersApi } from '@/composables/useContainersApi';

const props = defineProps<{
  modelValue: boolean;
  host: ProxyHostConfig;
  isEdit: boolean;
  saveError?: string;
}>();

const emit = defineEmits<{
  'update:modelValue': [value: boolean];
  'save': [host: ProxyHostConfig];
  'delete': [];
}>();

const hostModeOptions = [
  { title: 'Manual', value: 'manual' },
  { title: 'Automatic from container labels', value: 'automaticContainer' },
];

const rewriteOptions = [
  { title: 'Preserve path', value: 'preserve' },
  { title: 'Strip matched prefix', value: 'stripPrefix' },
  { title: 'Replace matched prefix', value: 'replacePrefix' },
];

const upstreamModeOptions = [
  { title: 'Manual', value: 'manual' },
  { title: 'Container', value: 'container' },
];

const portResolutionOptions = [
  { title: 'Container port', value: 'container' },
  { title: 'Published host port', value: 'published' },
];

const activeTab = ref<'general' | 'routes' | 'tls'>('general');
const localForm = ref<ProxyHostConfig>(cloneHost(props.host));
const certificatesApi = useCertificatesApi();
const containersApi = useContainersApi();
const certificateOptions = ref<StoredCertificateConfig[]>([]);
const containerOptions = ref<ContainerInventoryItem[]>([]);
const containerError = ref('');

const labelKeyOptions = computed(() =>
  Array.from(new Set(containerOptions.value.flatMap(container => Object.keys(container.containerLabels))))
    .sort((left, right) => left.localeCompare(right)),
);

const isEdit = computed(() => props.isEdit);

watch(
  () => props.host,
  (value) => {
    localForm.value = cloneHost(value);
  },
  { deep: true, immediate: true },
);

watch(
  () => localForm.value.id,
  (value) => {
    if (isEdit.value) {
      return;
    }

    localForm.value.id = toKebabCase(value);
  },
);

watch(
  () => props.modelValue,
  async (open) => {
    if (open) {
      await loadCertificates();
      await loadContainers();
    }
  },
);

watch(
  () => localForm.value.mode,
  (value) => {
    if (value === 'automaticContainer' && localForm.value.automaticContainer.labelSelectors.length === 0) {
      localForm.value.automaticContainer.labelSelectors.push({
        key: '',
        valuePattern: null,
        valuePatterns: [],
      });
    }
  },
);

function onDialogModelUpdate(value: boolean) {
  if (!value) {
    activeTab.value = 'general';
  }

  emit('update:modelValue', value);
}

function close() {
  activeTab.value = 'general';
  emit('update:modelValue', false);
}

function save() {
  emit('save', cloneHost(localForm.value));
}

async function loadCertificates() {
  const all = await certificatesApi.listCertificates();

  certificateOptions.value = all.filter(x => x.mode !== 'issuer');
}

async function loadContainers() {
  try {
    containerError.value = '';

    const response = await containersApi.listContainers();

    containerOptions.value = response.containers;
  }
  catch (error) {
    containerError.value = (error as Error).message;
    containerOptions.value = [];
  }
}

function cloneHost(host: ProxyHostConfig): ProxyHostConfig {
  return JSON.parse(JSON.stringify(host)) as ProxyHostConfig;
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
