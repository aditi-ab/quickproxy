<template>
  <Dialog :open="modelValue" @update:open="emit('update:modelValue', $event)">
    <DialogContent size="4xl" scrollable>
      <DialogHeader>
        <DialogTitle>{{ isEdit ? 'Edit Domain Translation' : 'Create Domain Translation' }}</DialogTitle>
        <DialogDescription class="sr-only">
          Map an incoming source domain to a target domain and configure TLS rewriting.
        </DialogDescription>
      </DialogHeader>

      <CardContent class="dialog-body-content">
        <Alert v-if="saveError" class="mb-4" variant="destructive">
          {{ saveError }}
        </Alert>

        <div class="flex flex-col gap-4">
          <Field>
            <FieldLabel for="domain-translation-id">
              Rule ID
            </FieldLabel>
            <Input
              id="domain-translation-id" v-model="localForm.id"
              :disabled="isEdit"
            />
          </Field>
          <Field orientation="horizontal">
            <Switch id="domain-translation-enabled" v-model="localForm.enabled" />
            <FieldLabel for="domain-translation-enabled">
              Enabled
            </FieldLabel>
          </Field>
          <Field>
            <FieldLabel for="domain-translation-source">
              Source Domain
            </FieldLabel>
            <Input
              id="domain-translation-source" v-model="localForm.sourceDomain"
              aria-describedby="domain-translation-source-description"
            />
            <FieldDescription id="domain-translation-source-description">
              Matches the apex domain and all subdomains
            </FieldDescription>
          </Field>
          <Field>
            <FieldLabel for="domain-translation-target">
              Target Domain
            </FieldLabel>
            <Input
              id="domain-translation-target" v-model="localForm.targetDomain"
              aria-describedby="domain-translation-target-description"
            />
            <FieldDescription id="domain-translation-target-description">
              Leading subdomains are preserved automatically
            </FieldDescription>
          </Field>
          <Field>
            <FieldLabel for="domain-translation-certificate">
              Certificate / Issuer
            </FieldLabel><Select v-model="localForm.certificateId">
              <SelectTrigger id="domain-translation-certificate">
                <SelectValue placeholder="Certificate / Issuer" />
              </SelectTrigger><SelectContent>
                <SelectItem v-for="option in certificateOptions" :key="String(option.id)" :value="option.id">
                  {{ option.id }}
                </SelectItem>
              </SelectContent>
            </Select>
          </Field>
          <Field orientation="horizontal">
            <Switch id="domain-translation-rewrite-host" v-model="localForm.rewriteHostHeader" />
            <FieldLabel for="domain-translation-rewrite-host">
              Rewrite upstream Host header and TLS SNI
            </FieldLabel>
          </Field>
        </div>
      </CardContent>

      <Separator />
      <DialogFooter>
        <Button v-if="isEdit" @click="emit('delete')" variant="destructive">
          Delete
        </Button>
        <span class="ml-auto" />
        <Button variant="ghost" @click="emit('update:modelValue', false)">
          Cancel
        </Button>
        <Button @click="emit('save', cloneRule(localForm))">
          Save
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>

<script setup lang="ts">
import type { StoredCertificateConfig } from '@/composables/useCertificatesApi';

import type { DomainTranslationRule } from '@/composables/useDomainTranslationsApi';

import { ref, watch } from 'vue';
import { useCertificatesApi } from '@/composables/useCertificatesApi';

const props = defineProps<{
  modelValue: boolean;
  rule: DomainTranslationRule;
  isEdit: boolean;
  saveError?: string;
}>();

const emit = defineEmits<{
  'update:modelValue': [value: boolean];
  'save': [rule: DomainTranslationRule];
  'delete': [];
}>();

const certificatesApi = useCertificatesApi();
const certificateOptions = ref<StoredCertificateConfig[]>([]);
const localForm = ref<DomainTranslationRule>(cloneRule(props.rule));

watch(
  () => props.rule,
  (value) => {
    localForm.value = cloneRule(value);
  },
  { deep: true, immediate: true },
);

watch(
  () => props.modelValue,
  async (open) => {
    if (!open) {
      return;
    }

    certificateOptions.value = await certificatesApi.listCertificates();
  },
);

watch(
  () => localForm.value.id,
  (value) => {
    if (props.isEdit) {
      return;
    }

    localForm.value.id = toKebabCase(value);
  },
);

function cloneRule(rule: DomainTranslationRule): DomainTranslationRule {
  return JSON.parse(JSON.stringify(rule)) as DomainTranslationRule;
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
