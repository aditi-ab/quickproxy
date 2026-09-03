<template>
  <Dialog
    :open="modelValue"
    @update:open="emit('update:modelValue', $event)"
  >
    <DialogContent size="4xl" scrollable>
      <DialogHeader><DialogTitle>Create Entry</DialogTitle><DialogDescription>Create a configuration entry and choose how its payload is stored.</DialogDescription></DialogHeader>
      <div data-slot="dialog-body" class="dialog-body-content -mx-4 overflow-x-hidden px-4">
        <Alert v-if="errorMessage" class="mb-4" variant="destructive">
          {{ errorMessage }}
        </Alert>

        <div class="grid grid-cols-12 gap-4">
          <div class="col-span-12 md:col-span-12">
            <Field><FieldLabel>Key</FieldLabel><Input v-model="formModel.key" /></Field>
          </div>
          <div class="col-span-12 md:col-span-12">
            <div class="flex gap-4 flex-wrap items-end">
              <div>
                <div class="text-xs text-muted-foreground mb-2">
                  Entry Type
                </div>
                <ButtonGroup
                  aria-label="Entry type"
                >
                  <Button
                    v-for="option in entryTypeOptions" :key="option.value"
                    :variant="formModel.entryType === option.value ? 'default' : 'outline'"
                    @click="formModel.entryType = option.value"
                  >
                    {{ option.title }}
                  </Button>
                </ButtonGroup>
              </div>

              <div>
                <div class="text-xs text-muted-foreground mb-2">
                  Payload Kind
                </div>
                <ButtonGroup
                  aria-label="Payload kind"
                >
                  <Button
                    v-for="option in payloadKindOptions" :key="option.value"
                    :variant="formModel.payloadKind === option.value ? 'default' : 'outline'"
                    @click="onPayloadKindChanged(option.value)"
                  >
                    {{ option.title }}
                  </Button>
                </ButtonGroup>
              </div>

              <Field v-if="formModel.payloadKind !== 'text'">
                <FieldLabel>Binary File</FieldLabel><Input type="file" @change="emit('upload-binary', (($event.target as HTMLInputElement).files?.[0] ?? null))" />
              </Field>
            </div>
          </div>
        </div>

        <div class="mt-4 grid grid-cols-12 gap-4">
          <template v-if="formModel.payloadKind === 'text'">
            <div class="col-span-12 md:col-span-12">
              <MonacoEditorField v-model="formModel.value" label="Value" :language="editorLanguage" :height="360" :font-size="13" />
            </div>
          </template>
          <template v-else>
            <div class="col-span-12 md:col-span-12" v-if="hasBinaryPayload">
              <Button variant="secondary" disabled>
                <Download />
                Binary File Ready
              </Button>
            </div>
          </template>

          <div class="col-span-12 md:col-span-12">
            <ConfigLabelsEditor
              :labels="formModel.labels" @add="emit('add-label')"
              @remove="emit('remove-label', $event)"
            />
          </div>
        </div>
      </div>
      <DialogFooter>
        <span class="ml-auto" />
        <Button variant="ghost" @click="emit('update:modelValue', false)">
          Cancel
        </Button>
        <Button @click="emit('save')" :disabled="saving">
          <Spinner v-if="saving" />
          Create
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>

<script setup lang="ts">
import type { ConfigEntryType, ConfigLabel, ConfigPayloadKind } from '@/composables/useConfigsApi';
import { computed } from 'vue';
import ConfigLabelsEditor from '@/components/key-values/ConfigLabelsEditor.vue';
import MonacoEditorField from '@/components/MonacoEditorField.vue';

interface CreateForm {
  key: string;
  value: string;
  binaryBase64: string;
  mediaType: string;
  entryType: ConfigEntryType;
  payloadKind: ConfigPayloadKind;
  labels: ConfigLabel[];
  isRevealed: boolean;
}

const props = defineProps<{
  modelValue: boolean;
  form: CreateForm;
  editorLanguage: string;
  saving: boolean;
  errorMessage: string;
}>();

const emit = defineEmits<{
  'update:modelValue': [value: boolean];
  'update:form': [value: CreateForm];
  'upload-binary': [value: File | File[] | null];
  'payload-kind-change': [value: ConfigPayloadKind];
  'add-label': [];
  'remove-label': [index: number];
  'save': [];
}>();

const entryTypeOptions = [
  { title: 'Data', value: 'data' },
  { title: 'Secret', value: 'secret' },
] satisfies Array<{ title: string; value: ConfigEntryType }>;

const payloadKindOptions = [
  { title: 'Text', value: 'text' },
  { title: 'Binary', value: 'binary' },
] satisfies Array<{ title: string; value: ConfigPayloadKind }>;

const formModel = computed({
  get: () => props.form,
  set: value => emit('update:form', value),
});

const hasBinaryPayload = computed(() => Boolean(formModel.value.binaryBase64));

function onPayloadKindChanged(value: ConfigPayloadKind) {
  emit('payload-kind-change', value);
}
</script>
