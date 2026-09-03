<template>
  <Dialog :open="modelValue" @update:open="emit('update:modelValue', $event)">
    <DialogContent size="4xl" scrollable>
      <DialogHeader>
        <DialogTitle>{{ title }}</DialogTitle>
        <DialogDescription>Move the selected configuration entries to another folder.</DialogDescription>
      </DialogHeader>
      <div data-slot="dialog-body" class="dialog-body-content -mx-4 overflow-x-hidden px-4">
        <Alert v-if="errorMessage" class="mb-4" variant="destructive">
          {{ errorMessage }}
        </Alert>
        <div class="flex flex-col gap-2">
          <div class="text-sm text-muted-foreground mb-3">
            Selected keys: {{ selectedCount }}
          </div>
          <Card v-if="selectedPaths.length > 0" border rounded class="selected-paths-sheet mb-3">
            <div class="selected-paths-list">
              <div v-for="path in selectedPaths" :key="path" class="selected-path-row font-mono">
                {{ formatPath(path) }}
              </div>
            </div>
          </Card>
          <Field>
            <FieldLabel>Target Folder</FieldLabel>
            <Input v-model="formModel.targetFolder" />
            <FieldDescription>Leave empty for root. Example: app/settings</FieldDescription>
          </Field>
        </div>
      </div>
      <DialogFooter>
        <span class="ml-auto" />
        <Button variant="ghost" @click="emit('update:modelValue', false)">
          Cancel
        </Button>
        <Button @click="emit('save')" :disabled="saving">
          <Spinner v-if="saving" />
          {{ confirmLabel }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>

<script setup lang="ts">
import { computed } from 'vue';

const props = defineProps<{
  modelValue: boolean;
  form: { targetFolder: string };
  selectedCount: number;
  selectedPaths: string[];
  saving: boolean;
  errorMessage: string;
  title?: string;
  confirmLabel?: string;
}>();

const emit = defineEmits<{
  'update:modelValue': [value: boolean];
  'update:form': [value: { targetFolder: string }];
  'save': [];
}>();

const formModel = computed({
  get: () => props.form,
  set: value => emit('update:form', value),
});

function formatPath(path: string) {
  return path.endsWith('/') ? path.replace(/\/$/, '') || '/' : path;
}
</script>

<style scoped>
.selected-paths-sheet {
  max-height: 180px;
  overflow: auto;
  background: var(--muted);
}

.selected-paths-list {
  display: flex;
  flex-direction: column;
}

.selected-path-row {
  padding: 8px 12px;
  font-size: 0.85rem;
  line-height: 1.35;
  border-bottom: 1px solid color-mix(in srgb, var(--foreground) calc(0.08 * 100%), transparent);
}

.selected-path-row:last-child {
  border-bottom: none;
}
</style>
