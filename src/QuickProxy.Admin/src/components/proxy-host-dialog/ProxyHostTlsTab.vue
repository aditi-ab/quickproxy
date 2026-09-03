<template>
  <div class="grid grid-cols-12 gap-4">
    <div class="col-span-12">
      <p class="text-muted-foreground mb-2">
        Select a stored certificate configuration from Settings.
      </p>
    </div>
    <div class="col-span-12 md:col-span-8">
      <Field>
        <FieldLabel>Certificate Configuration</FieldLabel><Select v-model="localForm.certificateId">
          <SelectTrigger><SelectValue placeholder="Certificate Configuration" /></SelectTrigger><SelectContent>
            <SelectItem v-for="option in certificateOptions" :key="String(option.id)" :value="option.id">
              {{ option.id }}
            </SelectItem>
          </SelectContent>
        </Select>
      </Field>
    </div>
    <div class="flex items-end col-span-12 md:col-span-4">
      <Button variant="secondary" @click="emit('reload-certificates')">
        Reload Certificates
      </Button>
    </div>
  </div>
</template>

<script lang="ts" setup>
import type { StoredCertificateConfig } from '@/composables/useCertificatesApi';
import type { ProxyHostConfig } from '@/composables/useProxyHostsApi';

defineProps<{
  localForm: ProxyHostConfig;
  certificateOptions: StoredCertificateConfig[];
}>();

const emit = defineEmits<{
  'reload-certificates': [];
}>();
</script>
