<template>
  <div class="flex items-center">
    <span class="text-base font-semibold">Published Ports</span>
    <span class="ml-auto" />
    <Button size="sm" variant="secondary" @click="emit('add')">
      <Plus />
      Add Port
    </Button>
  </div>
  <div class="grid grid-cols-12 gap-4" v-if="ports.length > 0">
    <div class="col-span-12" v-for="(port, index) in ports" :key="`port-${index}`">
      <div class="items-center grid grid-cols-12 gap-4">
        <div class="col-span-12 md:col-span-3">
          <Field>
            <FieldLabel :for="`port-${index}-container-port`">
              Container Port
            </FieldLabel><Input
              :id="`port-${index}-container-port`" :model-value="port.containerPort" type="number"
              @update:model-value="emit('update-number', { port, field: 'containerPort', value: $event })"
            />
          </Field>
        </div>
        <div class="col-span-12 md:col-span-3">
          <Field>
            <FieldLabel :for="`port-${index}-host-port`">
              Host Port
            </FieldLabel><Input
              :id="`port-${index}-host-port`" :model-value="port.hostPort" type="number"
              @update:model-value="emit('update-number', { port, field: 'hostPort', value: $event })"
            />
          </Field>
        </div>
        <div class="col-span-12 md:col-span-2">
          <Field>
            <FieldLabel :for="`port-${index}-protocol`">
              Protocol
            </FieldLabel><Select v-model="port.protocol">
              <SelectTrigger :id="`port-${index}-protocol`">
                <SelectValue placeholder="Protocol" />
              </SelectTrigger><SelectContent>
                <SelectItem v-for="option in ['tcp', 'udp']" :key="String((typeof option === 'string' ? option : (option as any).value))" :value="(typeof option === 'string' ? option : (option as any).value)">
                  {{ (typeof option === 'string' ? option : (option as any).title ?? (option as any).value) }}
                </SelectItem>
              </SelectContent>
            </Select>
          </Field>
        </div>
        <div class="col-span-12 md:col-span-2">
          <Field>
            <FieldLabel :for="`port-${index}-host-ip`">
              Host IP
            </FieldLabel><Input :id="`port-${index}-host-ip`" v-model="port.hostIp" /><FieldDescription>Optional</FieldDescription>
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
import type { ContainerPublishedPortRequest } from '@/composables/useContainersApi';

defineProps<{
  ports: ContainerPublishedPortRequest[];
}>();

const emit = defineEmits<{
  'add': [];
  'remove': [index: number];
  'update-number': [value: {
    port: ContainerPublishedPortRequest;
    field: 'containerPort' | 'hostPort';
    value: string | number | null;
  }];
}>();
</script>
