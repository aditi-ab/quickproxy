<template>
  <Dialog :open="modelValue" @update:open="emit('update:modelValue', $event)">
    <DialogContent size="4xl" scrollable>
      <DialogHeader>
        <DialogTitle>{{ isEdit ? 'Edit Defaults Set' : 'Create Defaults Set' }}</DialogTitle>
        <DialogDescription class="sr-only">
          Configure reusable defaults for container labels, environment, mounts, hosts, and networks.
        </DialogDescription>
      </DialogHeader>
      <CardContent class="dialog-body-content">
        <Alert v-if="error" class="mb-4" variant="destructive">
          {{ error }}
        </Alert>

        <Field>
          <FieldLabel for="defaults-set-id">
            Set Id
          </FieldLabel><Input id="defaults-set-id" v-model="localModel.id" :disabled="isEdit" /><FieldDescription>Used by label quickproxy.defaults=&lt;id&gt;</FieldDescription>
        </Field>

        <Separator class="my-4" />
        <div class="flex items-center mb-2">
          <span class="text-base font-semibold">Labels</span>
          <span class="ml-auto" />
          <Button size="sm" variant="secondary" @click="addLabel">
            <Plus />
            Add Label
          </Button>
        </div>
        <div class="grid grid-cols-12 gap-4" v-if="localModel.labels.length > 0">
          <div class="col-span-12" v-for="(label, index) in localModel.labels" :key="`defaults-label-${index}`">
            <div class="items-center grid grid-cols-12 gap-4">
              <div class="col-span-12 md:col-span-5">
                <Field><FieldLabel>Key<Input v-model="label.key" /></FieldLabel></Field>
              </div>
              <div class="col-span-12 md:col-span-5">
                <Field><FieldLabel>Value<Input v-model="label.value" /></FieldLabel></Field>
              </div>
              <div class="flex items-end justify-end col-span-12 md:col-span-2">
                <Button @click="removeLabel(index)" variant="destructive">
                  Delete
                </Button>
              </div>
            </div>
          </div>
        </div>

        <Separator class="my-4" />
        <div class="flex items-center mb-2">
          <span class="text-base font-semibold">Environment Variables</span>
          <span class="ml-auto" />
          <Button size="sm" variant="secondary" @click="addEnvVar">
            <Plus />
            Add Variable
          </Button>
        </div>
        <div class="grid grid-cols-12 gap-4" v-if="localModel.envVars.length > 0">
          <div class="col-span-12" v-for="(envVar, index) in localModel.envVars" :key="`defaults-env-${index}`">
            <div class="items-center grid grid-cols-12 gap-4">
              <div class="col-span-12 md:col-span-5">
                <Field><FieldLabel>Key<Input v-model="envVar.key" /></FieldLabel></Field>
              </div>
              <div class="col-span-12 md:col-span-5">
                <Field><FieldLabel>Value<Input v-model="envVar.value" /></FieldLabel></Field>
              </div>
              <div class="flex items-end justify-end col-span-12 md:col-span-2">
                <Button @click="removeEnvVar(index)" variant="destructive">
                  Delete
                </Button>
              </div>
            </div>
          </div>
        </div>

        <Separator class="my-4" />
        <div class="flex items-center mb-2">
          <span class="text-base font-semibold">Mount Bindings</span>
          <span class="ml-auto" />
          <Button size="sm" variant="secondary" @click="addMountBinding">
            <Plus />
            Add Binding
          </Button>
        </div>
        <div class="grid grid-cols-12 gap-4" v-if="localModel.mountBindings.length > 0">
          <div class="col-span-12" v-for="(mount, index) in localModel.mountBindings" :key="`defaults-mount-${index}`">
            <div class="items-center grid grid-cols-12 gap-4">
              <div class="col-span-12 md:col-span-4">
                <Field><FieldLabel>Host Path<Input v-model="mount.hostPath" /></FieldLabel></Field>
              </div>
              <div class="col-span-12 md:col-span-4">
                <Field><FieldLabel>Container Path<Input v-model="mount.containerPath" /></FieldLabel></Field>
              </div>
              <div class="flex items-center col-span-12 md:col-span-2">
                <Field orientation="horizontal">
                  <FieldLabel>Read-only<Switch v-model="mount.readOnly" /></FieldLabel>
                </Field>
              </div>
              <div class="flex items-end justify-end col-span-12 md:col-span-2">
                <Button @click="removeMountBinding(index)" variant="destructive">
                  Delete
                </Button>
              </div>
            </div>
          </div>
        </div>

        <Separator class="my-4" />
        <div class="flex items-center mb-2">
          <span class="text-base font-semibold">Host Mappings</span>
          <span class="ml-auto" />
          <Button size="sm" variant="secondary" @click="addHostMapping">
            <Plus />
            Add Host
          </Button>
        </div>
        <div class="grid grid-cols-12 gap-4" v-if="localModel.hostMappings.length > 0">
          <div class="col-span-12" v-for="(hostMapping, index) in localModel.hostMappings" :key="`defaults-host-mapping-${index}`">
            <div class="items-center grid grid-cols-12 gap-4">
              <div class="col-span-12 md:col-span-5">
                <Field><FieldLabel>Hostname<Input v-model="hostMapping.hostname" /></FieldLabel><FieldDescription>Example: my.internal</FieldDescription></Field>
              </div>
              <div class="col-span-12 md:col-span-5">
                <Field><FieldLabel>Address<Input v-model="hostMapping.address" /></FieldLabel><FieldDescription>Example: host-gateway or 172.17.0.1</FieldDescription></Field>
              </div>
              <div class="flex items-end justify-end col-span-12 md:col-span-2">
                <Button @click="removeHostMapping(index)" variant="destructive">
                  Delete
                </Button>
              </div>
            </div>
          </div>
        </div>

        <Separator class="my-4" />
        <div class="flex items-center mb-2">
          <span class="text-base font-semibold">Network Aliases</span>
          <span class="ml-auto" />
          <Button size="sm" variant="secondary" @click="addNetworkAlias">
            <Plus />
            Add Alias
          </Button>
        </div>
        <div class="grid grid-cols-12 gap-4" v-if="localModel.networkAliases.length > 0">
          <div class="col-span-12" v-for="(networkAlias, index) in localModel.networkAliases" :key="`defaults-network-alias-${index}`">
            <div class="items-center grid grid-cols-12 gap-4">
              <div class="col-span-12 md:col-span-5">
                <Field><FieldLabel>Network<Input v-model="networkAlias.network" /></FieldLabel></Field>
              </div>
              <div class="col-span-12 md:col-span-5">
                <Field><FieldLabel>Alias<Input v-model="networkAlias.alias" /></FieldLabel></Field>
              </div>
              <div class="flex items-end justify-end col-span-12 md:col-span-2">
                <Button @click="removeNetworkAlias(index)" variant="destructive">
                  Delete
                </Button>
              </div>
            </div>
          </div>
        </div>
      </CardContent>
      <Separator />
      <DialogFooter>
        <Button v-if="isEdit" :disabled="saving" @click="emit('delete')" variant="destructive">
          Delete
        </Button>
        <span class="ml-auto" />
        <Button variant="ghost" :disabled="saving" @click="emit('update:modelValue', false)">
          Cancel
        </Button>
        <Button @click="onSave" :disabled="saving">
          <Spinner v-if="saving" />
          Save
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>

<script lang="ts" setup>
import type {
  ContainerDefaultsSet,
  ContainerHostMappingRequest,
  ContainerKeyValuePair,
  ContainerMountBindingRequest,
  ContainerNetworkAliasRequest,
} from '@/composables/useContainersApi';

import { ref, watch } from 'vue';

const props = defineProps<{
  modelValue: boolean;
  model: ContainerDefaultsSet;
  isEdit: boolean;
  error: string;
  saving: boolean;
}>();

const emit = defineEmits<{
  'update:modelValue': [value: boolean];
  'save': [value: ContainerDefaultsSet];
  'delete': [];
}>();

const localModel = ref<ContainerDefaultsSet>(cloneModel(props.model));

watch(
  () => props.model,
  (value) => {
    localModel.value = cloneModel(value);
  },
  { deep: true, immediate: true },
);

function addLabel() {
  localModel.value.labels.push(emptyKeyValue());
}

function removeLabel(index: number) {
  localModel.value.labels.splice(index, 1);
}

function addEnvVar() {
  localModel.value.envVars.push(emptyKeyValue());
}

function removeEnvVar(index: number) {
  localModel.value.envVars.splice(index, 1);
}

function addMountBinding() {
  localModel.value.mountBindings.push(emptyMountBinding());
}

function removeMountBinding(index: number) {
  localModel.value.mountBindings.splice(index, 1);
}

function addNetworkAlias() {
  localModel.value.networkAliases.push(emptyNetworkAlias());
}

function removeNetworkAlias(index: number) {
  localModel.value.networkAliases.splice(index, 1);
}

function addHostMapping() {
  localModel.value.hostMappings.push(emptyHostMapping());
}

function removeHostMapping(index: number) {
  localModel.value.hostMappings.splice(index, 1);
}

function emptyKeyValue(): ContainerKeyValuePair {
  return {
    key: '',
    value: '',
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

function cloneModel(value: ContainerDefaultsSet): ContainerDefaultsSet {
  return {
    id: value.id,
    labels: (value.labels ?? []).map(x => ({ key: x.key, value: x.value })),
    envVars: (value.envVars ?? []).map(x => ({ key: x.key, value: x.value })),
    mountBindings: (value.mountBindings ?? []).map(x => ({
      hostPath: x.hostPath,
      containerPath: x.containerPath,
      readOnly: x.readOnly,
    })),
    hostMappings: (value.hostMappings ?? []).map(x => ({
      hostname: x.hostname,
      address: x.address,
    })),
    networkAliases: (value.networkAliases ?? []).map(x => ({
      network: x.network,
      alias: x.alias,
    })),
    updatedAtUtc: value.updatedAtUtc,
  };
}

function onSave() {
  emit('save', cloneModel(localModel.value));
}
</script>
