<template>
  <div class="flex items-center mb-2">
    <span class="text-base font-semibold">{{ title }}</span>
    <span class="ml-auto" />
    <Button size="sm" variant="secondary" @click="emit('add')">
      <Plus />
      {{ addLabel }}
    </Button>
  </div>
  <div class="grid grid-cols-12 gap-4" v-if="items.length > 0">
    <div class="col-span-12" v-for="(item, index) in items" :key="`${idPrefix}-${index}`">
      <div class="items-center grid grid-cols-12 gap-4">
        <div class="col-span-12 md:col-span-5">
          <Field>
            <FieldLabel :for="`${idPrefix}-${index}-key`">
              Key
            </FieldLabel><Input :id="`${idPrefix}-${index}-key`" v-model="item.key" />
          </Field>
        </div>
        <div class="col-span-12 md:col-span-5">
          <Field>
            <FieldLabel :for="`${idPrefix}-${index}-value`">
              Value
            </FieldLabel><Input :id="`${idPrefix}-${index}-value`" v-model="item.value" />
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

<script setup lang="ts">
import type { ContainerKeyValuePair } from '@/composables/useContainersApi';

defineProps<{
  title: string;
  addLabel: string;
  idPrefix: string;
  items: ContainerKeyValuePair[];
}>();

const emit = defineEmits<{
  add: [];
  remove: [index: number];
}>();
</script>
