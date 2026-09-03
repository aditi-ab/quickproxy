<template>
  <Dialog
    :open="modelValue" @update:open="emit('update:modelValue', $event)"
  >
    <DialogContent size="4xl" scrollable class="container-dialog-card">
      <DialogHeader>
        <DialogTitle class="flex items-center">
          <span>{{ isEdit ? 'Edit Container' : 'Create Container' }}</span>
        </DialogTitle>
        <DialogDescription class="sr-only">
          Configure the container image, runtime settings, networking, and metadata.
        </DialogDescription>
      </DialogHeader>
      <CardContent class="container-dialog-body dialog-body-content">
        <Alert v-if="saveError" class="mb-4" variant="destructive">
          {{ saveError }}
        </Alert>

        <div class="divide-y divide-border">
          <section class="py-4 first:pt-0">
            <ContainerImageBasicsSection
              :is-edit="isEdit"
              :form="localForm"
              :image-archive="imageArchive"
              :archive-inspecting="archiveInspecting"
              :archive-inspect-error="archiveInspectError"
              :archive-info="archiveInfo"
              :archive-repository-mismatch-message="archiveRepositoryMismatchMessage"
              :trigger-defaults-set-id="triggerDefaultsSetId"
              :restart-policies="restartPolicies"
              @update:image-archive="imageArchive = $event"
            />
          </section>

          <section class="py-4">
            <ContainerKeyValuePairsSection
              title="Labels"
              add-label="Add Label"
              id-prefix="label"
              :items="localForm.labels"
              @add="addLabel"
              @remove="removeLabel"
            />
          </section>

          <section class="py-4">
            <ContainerKeyValuePairsSection
              title="Environment Variables"
              add-label="Add Variable"
              id-prefix="env"
              :items="localForm.envVars"
              @add="addEnvVar"
              @remove="removeEnvVar"
            />
          </section>

          <section class="py-4">
            <ContainerMountBindingsSection
              :mount-bindings="localForm.mountBindings"
              @add="addMountBinding"
              @remove="removeMountBinding"
            />
          </section>

          <section class="py-4">
            <ContainerPublishedPortsSection
              :ports="localForm.publishedPorts"
              @add="addPublishedPort"
              @remove="removePublishedPort"
              @update-number="updatePortNumber($event.port, $event.field, $event.value)"
            />
          </section>

          <section class="py-4">
            <div class="flex items-center mb-2">
              <span class="text-base font-semibold">Host Mappings</span>
              <span class="ml-auto" />
              <Button size="sm" variant="secondary" @click="addHostMapping">
                <Plus />
                Add Host
              </Button>
            </div>
            <div class="grid grid-cols-12 gap-4" v-if="localForm.hostMappings.length > 0">
              <div class="col-span-12" v-for="(hostMapping, index) in localForm.hostMappings" :key="`host-mapping-${index}`">
                <div class="items-center grid grid-cols-12 gap-4">
                  <div class="col-span-12 md:col-span-5">
                    <Field>
                      <FieldLabel :for="`host-mapping-${index}-hostname`">
                        Hostname
                      </FieldLabel><Input :id="`host-mapping-${index}-hostname`" v-model="hostMapping.hostname" /><FieldDescription>Example: my.internal</FieldDescription>
                    </Field>
                  </div>
                  <div class="col-span-12 md:col-span-5">
                    <Field>
                      <FieldLabel :for="`host-mapping-${index}-address`">
                        Address
                      </FieldLabel><Input :id="`host-mapping-${index}-address`" v-model="hostMapping.address" /><FieldDescription>Example: host-gateway or 172.17.0.1</FieldDescription>
                    </Field>
                  </div>
                  <div class="flex items-end justify-end col-span-12 md:col-span-2">
                    <Button @click="removeHostMapping(index)" variant="destructive">
                      Delete
                    </Button>
                  </div>
                </div>
              </div>
            </div>
          </section>

          <section class="py-4 last:pb-0">
            <div class="flex items-center mb-2">
              <span class="text-base font-semibold">Network Aliases</span>
              <span class="ml-auto" />
              <Button size="sm" variant="secondary" @click="addNetworkAlias">
                <Plus />
                Add Alias
              </Button>
            </div>
            <div class="grid grid-cols-12 gap-4" v-if="localForm.networkAliases.length > 0">
              <div class="col-span-12" v-for="(networkAlias, index) in localForm.networkAliases" :key="`network-alias-${index}`">
                <div class="items-center grid grid-cols-12 gap-4">
                  <div class="col-span-12 md:col-span-5">
                    <Field>
                      <FieldLabel :for="`network-alias-${index}-network`">
                        Network
                      </FieldLabel><Input :id="`network-alias-${index}-network`" v-model="networkAlias.network" /><FieldDescription>Example: quickproxy</FieldDescription>
                    </Field>
                  </div>
                  <div class="col-span-12 md:col-span-5">
                    <Field>
                      <FieldLabel :for="`network-alias-${index}-alias`">
                        Alias
                      </FieldLabel><Input :id="`network-alias-${index}-alias`" v-model="networkAlias.alias" /><FieldDescription>Example: api</FieldDescription>
                    </Field>
                  </div>
                  <div class="flex items-end justify-end col-span-12 md:col-span-2">
                    <Button @click="removeNetworkAlias(index)" variant="destructive">
                      Delete
                    </Button>
                  </div>
                </div>
              </div>
            </div>
          </section>
        </div>
      </CardContent>

      <DialogFooter>
        <Button v-if="isEdit" :disabled="props.saving" @click="emit('delete')" variant="destructive">
          Delete
        </Button>
        <span class="ml-auto" />
        <Button variant="ghost" :disabled="props.saving" @click="emit('update:modelValue', false)">
          Cancel
        </Button>
        <Button :disabled="props.saving || !!archiveRepositoryMismatchMessage" @click="save">
          <Spinner v-if="props.saving" />
          Save
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>

<script lang="ts" setup>
import type {
  ContainerEditRequest,
  ContainerHostMappingRequest,
  ContainerImageArchiveInfo,
  ContainerKeyValuePair,
  ContainerMountBindingRequest,
  ContainerNetworkAliasRequest,
  ContainerPublishedPortRequest,
  ContainerSaveRequest,
} from '@/composables/useContainersApi';
import { computed, ref, watch } from 'vue';
import ContainerImageBasicsSection from '@/components/containers/dialog/ContainerImageBasicsSection.vue';
import ContainerKeyValuePairsSection from '@/components/containers/dialog/ContainerKeyValuePairsSection.vue';
import ContainerMountBindingsSection from '@/components/containers/dialog/ContainerMountBindingsSection.vue';
import ContainerPublishedPortsSection from '@/components/containers/dialog/ContainerPublishedPortsSection.vue';
import { useContainersApi } from '@/composables/useContainersApi';

const props = defineProps<{
  modelValue: boolean;
  isEdit: boolean;
  container: ContainerEditRequest;
  initialImageArchive?: File | null;
  saveError?: string;
  saving?: boolean;
}>();

const emit = defineEmits<{
  'update:modelValue': [value: boolean];
  'save': [value: ContainerSaveRequest];
  'delete': [];
}>();

const restartPolicies: Array<ContainerEditRequest['restartPolicy']> = ['no', 'always', 'unless-stopped', 'on-failure'];

const localForm = ref<ContainerEditRequest>(clone(props.container));
const imageArchive = ref<File | null>(null);
const archiveInfo = ref<ContainerImageArchiveInfo | null>(null);
const archiveInspectError = ref('');
const archiveInspecting = ref(false);
const containersApi = useContainersApi();
const triggerDefaultsSetId = computed(() => {
  const label = localForm.value.labels
    .find(x => (x.key ?? '').trim().toLowerCase() === 'quickproxy.defaults');
  const value = (label?.value ?? '').trim();

  return value || '';
});
const archiveRepositoryMismatchMessage = computed(() => {
  if (!props.isEdit || !archiveInfo.value) {
    return '';
  }

  const currentRepository = extractImageRepository(props.container.image);
  const archiveRepositories = archiveInfo.value.repoTags
    .map(extractImageRepository)
    .filter((value, index, array) => !!value && array.indexOf(value) === index);

  if (!currentRepository || archiveRepositories.length === 0) {
    return '';
  }

  if (archiveRepositories.every(repository => repository !== currentRepository)) {
    return `Replacement archive repository '${archiveRepositories[0]}' does not match the current container image repository '${currentRepository}'.`;
  }

  return '';
});

watch(
  () => props.container,
  (value) => {
    localForm.value = clone(value);
    imageArchive.value = props.initialImageArchive ?? null;
    archiveInfo.value = null;
    archiveInspectError.value = '';
    archiveInspecting.value = false;
  },
  { deep: true, immediate: true },
);

watch(
  () => props.initialImageArchive,
  (value) => {
    imageArchive.value = value ?? null;

    if (!value) {
      archiveInfo.value = null;
      archiveInspectError.value = '';
      archiveInspecting.value = false;
    }
  },
  { immediate: true },
);

watch(imageArchive, async (value) => {
  archiveInfo.value = null;
  archiveInspectError.value = '';

  if (!value) {
    return;
  }

  try {
    archiveInspecting.value = true;

    const info = await containersApi.inspectImageArchive(value);

    archiveInfo.value = info;

    if (!localForm.value.image && info.suggestedImage) {
      localForm.value.image = info.suggestedImage;
    }

    if (!props.isEdit && !localForm.value.name) {
      const suggestedName = deriveContainerName(info.suggestedImage, value.name);

      if (suggestedName) {
        localForm.value.name = suggestedName;
      }
    }
  }
  catch (error) {
    archiveInspectError.value = (error as Error).message;
  }
  finally {
    archiveInspecting.value = false;
  }
});

function save() {
  emit('save', {
    request: clone(localForm.value),
    imageArchive: imageArchive.value,
  });
}

function addLabel() {
  localForm.value.labels.push(emptyKeyValue());
}

function removeLabel(index: number) {
  localForm.value.labels.splice(index, 1);
}

function addEnvVar() {
  localForm.value.envVars.push(emptyKeyValue());
}

function removeEnvVar(index: number) {
  localForm.value.envVars.splice(index, 1);
}

function addPublishedPort() {
  localForm.value.publishedPorts.push(emptyPublishedPort());
}

function addMountBinding() {
  localForm.value.mountBindings.push(emptyMountBinding());
}

function removePublishedPort(index: number) {
  localForm.value.publishedPorts.splice(index, 1);
}

function removeMountBinding(index: number) {
  localForm.value.mountBindings.splice(index, 1);
}

function addNetworkAlias() {
  localForm.value.networkAliases.push(emptyNetworkAlias());
}

function removeNetworkAlias(index: number) {
  localForm.value.networkAliases.splice(index, 1);
}

function addHostMapping() {
  localForm.value.hostMappings.push(emptyHostMapping());
}

function removeHostMapping(index: number) {
  localForm.value.hostMappings.splice(index, 1);
}

function updatePortNumber(port: ContainerPublishedPortRequest, field: 'containerPort' | 'hostPort', value: string | number | null) {
  const parsed = typeof value === 'number' ? value : Number.parseInt(value ?? '', 10);

  if (!Number.isFinite(parsed) || parsed <= 0) {
    return;
  }

  port[field] = parsed;
}

function clone(value: ContainerEditRequest): ContainerEditRequest {
  return JSON.parse(JSON.stringify(value)) as ContainerEditRequest;
}

function emptyKeyValue(): ContainerKeyValuePair {
  return {
    key: '',
    value: '',
  };
}

function emptyPublishedPort(): ContainerPublishedPortRequest {
  return {
    containerPort: 80,
    hostPort: 80,
    protocol: 'tcp',
    hostIp: '',
  };
}

function emptyMountBinding(): ContainerMountBindingRequest {
  return {
    hostPath: '',
    containerPath: '',
    readOnly: false,
  };
}

function emptyHostMapping(): ContainerHostMappingRequest {
  return {
    hostname: '',
    address: '',
  };
}

function emptyNetworkAlias(): ContainerNetworkAliasRequest {
  return {
    network: '',
    alias: '',
  };
}

function deriveContainerName(suggestedImage?: string | null, fileName?: string | null) {
  let imageCandidate: string | null = null;

  if (suggestedImage) {
    const withoutDigest = suggestedImage.split('@')[0] ?? suggestedImage;
    const imageSegments = withoutDigest.split('/');
    const lastImageSegment = imageSegments.length > 0 ? (imageSegments[imageSegments.length - 1] ?? '') : '';

    imageCandidate = lastImageSegment.split(':')[0] ?? null;
  }

  const fileCandidate = fileName
    ?.replace(/\.tar\.gz$/i, '')
    .replace(/\.tgz$/i, '')
    .replace(/\.tar$/i, '')
    .replace(/\.gz$/i, '');

  const rawCandidate = imageCandidate || fileCandidate || '';
  const normalized = rawCandidate
    .toLowerCase()
    .replace(/[^a-z0-9_.-]+/g, '-')
    .replace(/-+/g, '-')
    .replace(/^[-_.]+|[-_.]+$/g, '');

  return normalized || '';
}

function extractImageRepository(imageReference?: string | null) {
  if (!imageReference) {
    return '';
  }

  const withoutDigest = imageReference.split('@', 2)[0] ?? imageReference;
  const lastSlash = withoutDigest.lastIndexOf('/');
  const lastColon = withoutDigest.lastIndexOf(':');

  return lastColon > lastSlash
    ? withoutDigest.slice(0, lastColon)
    : withoutDigest;
}
</script>

<style scoped>
.container-dialog-card {
  max-height: min(90vh, 1000px);
  display: flex;
  flex-direction: column;
}

.container-dialog-body {
  overflow-y: auto;
}
</style>
