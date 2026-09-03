<template>
  <Dialog
    :open="modelValue" @update:open="emit('update:modelValue', $event)"
  >
    <DialogContent size="4xl" scrollable class="compose-project-dialog-card">
      <DialogHeader>
        <DialogTitle>{{ isEdit ? 'Edit Compose Project' : 'Create Compose Project' }}</DialogTitle>
        <DialogDescription class="sr-only">
          Configure the Compose definition and managed files for this project.
        </DialogDescription>
      </DialogHeader>
      <CardContent class="compose-project-dialog-body dialog-body-content">
        <Alert v-if="error" class="mb-4" variant="destructive">
          {{ error }}
        </Alert>

        <Field>
          <FieldLabel for="compose-project-id">
            Project Id
          </FieldLabel><Input
            id="compose-project-id" :model-value="localModel.id" :disabled="isEdit"
            @update:model-value="updateProjectName"
          /><FieldDescription>Used as the QuickProxy id, compose project name, and workspace name. Automatically normalized to kebab-case.</FieldDescription>
        </Field>

        <section class="mt-5 space-y-2">
          <div>
            <h3 class="text-base font-semibold">
              Compose YAML
            </h3>
            <p class="text-sm text-muted-foreground">
              Define the services managed by this project.
            </p>
          </div>
          <MonacoEditorField
            v-model="localModel.composeYaml" language="yaml"
            model-uri="inmemory://quickproxy/compose-project.compose.yaml" yaml-schema-uri="/schemas/compose-spec.json"
            :height="440" :font-size="14"
          />
        </section>

        <section class="mt-5 space-y-3">
          <div class="flex flex-wrap items-center justify-between gap-3">
            <div>
              <h3 class="text-base font-semibold">
                Managed Files
              </h3>
              <p class="text-sm text-muted-foreground">
                Add files stored alongside the Compose definition.
              </p>
            </div>
            <Button size="sm" variant="secondary" @click="addManagedFile">
              <Plus />
              Add File
            </Button>
          </div>

          <div v-if="localModel.managedFiles.length > 0" class="space-y-3">
            <Card v-for="(file, index) in localModel.managedFiles" :key="`managed-file-${index}`" variant="secondary">
              <CardContent class="space-y-4">
                <div class="grid gap-3 md:grid-cols-[minmax(0,1fr)_auto] md:items-end">
                  <Field>
                    <FieldLabel :for="`managed-file-${index}-path`">
                      Relative Path
                    </FieldLabel><Input :id="`managed-file-${index}-path`" v-model="file.path" />
                  </Field>
                  <Button size="sm" @click="removeManagedFile(index)" variant="destructive">
                    Delete File
                  </Button>
                </div>
                <Field>
                  <FieldLabel :for="`managed-file-${index}-content`">
                    Content
                  </FieldLabel><Textarea :id="`managed-file-${index}-content`" v-model="file.content" rows="5" />
                </Field>
              </CardContent>
            </Card>
          </div>
          <div v-else class="rounded-md border border-dashed px-4 py-6 text-center text-sm text-muted-foreground">
            No additional managed files.
          </div>
        </section>

        <div v-if="validationResult" class="mt-4">
          <Alert>
            {{ validationResult.valid ? 'Validation passed.' : 'Validation failed.' }}
          </Alert>
          <Field>
            <FieldLabel for="compose-validation-output">
              Validation Output
            </FieldLabel><Textarea
              id="compose-validation-output" :model-value="validationResult.output" readonly auto-grow rows="6"
            />
          </Field>
        </div>
      </CardContent>
      <DialogFooter>
        <Button v-if="isEdit" :disabled="saving" @click="emit('delete')" variant="destructive">
          Delete
        </Button>
        <span class="ml-auto" />
        <Button variant="ghost" :disabled="saving" @click="emit('update:modelValue', false)">
          Cancel
        </Button>
        <Button
          variant="secondary" :disabled="saving || validating"
          @click="emit('validate', clone(localModel))"
        >
          <Spinner v-if="validating" />
          Validate
        </Button>
        <Button
          :disabled="saving"
          @click="emit('deploy', clone(localModel))" variant="info"
        >
          <Spinner v-if="saving" />
          Save + Deploy
        </Button>
        <Button
          :disabled="saving"
          @click="emit('save', clone(localModel))"
        >
          <Spinner v-if="saving" />
          Save
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>

<script lang="ts" setup>
import type { ComposeProject, ComposeProjectRuntimeSnapshot, ComposeProjectValidationResult } from '@/composables/useContainersApi';

import { ref, watch } from 'vue';

import MonacoEditorField from '@/components/MonacoEditorField.vue';

const props = defineProps<{
  modelValue: boolean;
  isEdit: boolean;
  model: ComposeProject;
  runtime?: ComposeProjectRuntimeSnapshot | null;
  validationResult?: ComposeProjectValidationResult | null;
  saving?: boolean;
  validating?: boolean;
  error?: string;
}>();

const emit = defineEmits<{
  'update:modelValue': [value: boolean];
  'save': [value: ComposeProject];
  'deploy': [value: ComposeProject];
  'validate': [value: ComposeProject];
  'delete': [];
  'logs': [value: { id: string; service?: string }];
}>();

const localModel = ref<ComposeProject>(clone(props.model));

watch(() => props.model, (value) => {
  localModel.value = clone(value);
}, { deep: true, immediate: true });

function addManagedFile() {
  localModel.value.managedFiles.push({
    path: '',
    content: '',
  });
}

function removeManagedFile(index: number) {
  localModel.value.managedFiles.splice(index, 1);
}

function updateProjectName(value: string | null) {
  const normalizedValue = toKebabCase(value ?? '');

  localModel.value.id = normalizedValue;
  localModel.value.displayName = normalizedValue;
  localModel.value.slug = normalizedValue;
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

function clone(value: ComposeProject): ComposeProject {
  return JSON.parse(JSON.stringify(value)) as ComposeProject;
}
</script>

<style scoped>
.compose-project-dialog-card {
  max-height: min(92vh, 1100px);
  display: flex;
  flex-direction: column;
}

.compose-project-dialog-body {
  overflow-y: auto;
}
</style>
