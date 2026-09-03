<template>
  <div class="flex items-center mb-2">
    <span class="text-base font-semibold">Mount Bindings</span>
    <span class="ml-auto" />
    <Button size="sm" variant="secondary" @click="emit('add')">
      <Plus />
      Add Binding
    </Button>
  </div>
  <div class="grid grid-cols-12 gap-4" v-if="mountBindings.length > 0">
    <div class="col-span-12" v-for="(mount, index) in mountBindings" :key="`mount-binding-${index}`">
      <div class="items-center grid grid-cols-12 gap-4">
        <div class="col-span-12 md:col-span-4">
          <Field>
            <FieldLabel :for="`mount-${index}-host-path`">
              Host Path
            </FieldLabel><Input :id="`mount-${index}-host-path`" v-model="mount.hostPath" />
          </Field>
        </div>
        <div class="col-span-12 md:col-span-4">
          <Field>
            <FieldLabel :for="`mount-${index}-container-path`">
              Container Path
            </FieldLabel><Input :id="`mount-${index}-container-path`" v-model="mount.containerPath" />
          </Field>
        </div>
        <div class="flex items-center col-span-12 md:col-span-2">
          <Field orientation="horizontal">
            <FieldLabel :for="`mount-${index}-read-only`">
              Read-only
            </FieldLabel><Switch :id="`mount-${index}-read-only`" v-model="mount.readOnly" />
          </Field>
        </div>
        <div class="flex items-end justify-end col-span-12 md:col-span-2">
          <Button @click="emit('remove', index)" variant="destructive">
            Delete
          </Button>
        </div>
      </div>
    </div>
  </div>
</template>

<script lang="ts" setup>
import type { ContainerMountBindingRequest } from '@/composables/useContainersApi';

defineProps<{
  mountBindings: ContainerMountBindingRequest[];
}>();

const emit = defineEmits<{
  add: [];
  remove: [index: number];
}>();
</script>
