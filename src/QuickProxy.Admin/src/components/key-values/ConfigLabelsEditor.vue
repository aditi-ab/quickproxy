<template>
  <div class="flex items-center mb-2">
    <span class="text-base font-semibold">Labels</span>
    <span class="ml-auto" />
    <Button size="sm" variant="secondary" @click="emit('add')">
      <Plus />
      Add Label
    </Button>
  </div>
  <div v-if="labels.length > 0" class="label-list">
    <div v-for="(item, index) in labels" :key="index" class="label-row">
      <Field><FieldLabel>Key</FieldLabel><Input v-model="item.key" /></Field>
      <Field><FieldLabel>Value</FieldLabel><Input v-model="item.value" /></Field>
      <Tooltip>
        <TooltipTrigger as-child>
          <Button
            class="label-remove text-destructive" size="icon-sm" variant="ghost"
            aria-label="Remove label" @click="emit('remove', index)"
          >
            <Trash2 />
          </Button>
        </TooltipTrigger><TooltipContent>Remove label</TooltipContent>
      </Tooltip>
    </div>
  </div>
</template>

<script setup lang="ts">
import type { ConfigLabel } from '@/composables/useConfigsApi';

defineProps<{
  labels: ConfigLabel[];
}>();

const emit = defineEmits<{
  add: [];
  remove: [index: number];
}>();
</script>

<style scoped>
.label-list {
  display: grid;
  gap: 0.75rem;
}

.label-row {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr) auto;
  gap: 0.75rem;
  align-items: end;
}

.label-remove {
  margin-bottom: 0.125rem;
}

@media (max-width: 767px) {
  .label-row {
    grid-template-columns: 1fr;
  }

  .label-remove {
    justify-self: end;
  }
}
</style>
