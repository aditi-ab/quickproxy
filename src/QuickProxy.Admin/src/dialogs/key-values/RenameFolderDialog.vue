<template>
  <Dialog :open="modelValue" @update:open="emit('update:modelValue', $event)">
    <DialogContent size="4xl" scrollable>
      <DialogHeader>
        <DialogTitle>Rename Folder</DialogTitle>
        <DialogDescription>Change the folder path for this configuration group.</DialogDescription>
      </DialogHeader>
      <div data-slot="dialog-body" class="dialog-body-content -mx-4 overflow-x-hidden px-4">
        <Alert v-if="errorMessage" class="mb-4" variant="destructive">
          {{ errorMessage }}
        </Alert>
        <div class="flex flex-col gap-2">
          <Field>
            <FieldLabel>Current Folder</FieldLabel>
            <Input :model-value="form.from" readonly class="mb-2" />
          </Field>
          <Field>
            <FieldLabel>New Folder Path</FieldLabel>
            <Input v-model="formModel.to" />
            <FieldDescription>Example: app/settings</FieldDescription>
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
          Rename
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>

<script setup lang="ts">
import { computed } from 'vue';

const props = defineProps<{
  modelValue: boolean;
  form: { from: string; to: string };
  saving: boolean;
  errorMessage: string;
}>();

const emit = defineEmits<{
  'update:modelValue': [value: boolean];
  'update:form': [value: { from: string; to: string }];
  'save': [];
}>();

const formModel = computed({
  get: () => props.form,
  set: value => emit('update:form', value),
});
</script>
