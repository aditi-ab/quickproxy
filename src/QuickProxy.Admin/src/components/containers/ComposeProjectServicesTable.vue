<template>
  <div v-if="services.length > 0" class="service-list grid divide-y rounded-md border">
    <div v-for="service in services" :key="service.name" class="service-list-item flex items-center gap-3 p-3">
      <Box class="size-4 text-primary" />
      <div class="min-w-0 grow">
        <div class="flex items-center flex-wrap gap-1 text-sm">
          <span class="font-medium">{{ service.name }}</span>
          <Badge :variant="statusColor(service)">
            {{ statusText(service) }}
          </Badge>
        </div><div>
          <div v-if="service.containerNames.length > 0" class="flex flex-wrap gap-1 pt-1">
            <Badge
              v-for="containerName in service.containerNames"
              :key="`${service.name}-${containerName}`"
              class="service-container-chip" variant="secondary"
            >
              {{ containerName }}
            </Badge>
          </div>
          <span v-else class="text-muted-foreground text-xs">No containers</span>
        </div>
      </div>
      <Tooltip>
        <TooltipTrigger as-child>
          <Button
            aria-label="View service logs"
            size="icon-sm" variant="ghost"
            @click.stop="emit('logs', { projectId, service: service.name })"
          >
            <FileSearch />
          </Button>
        </TooltipTrigger><TooltipContent>View service logs</TooltipContent>
      </Tooltip>
    </div>
  </div>
  <div v-else class="text-center py-6 text-muted-foreground">
    No runtime services available.
  </div>
</template>

<script lang="ts" setup>
import type { ComposeProjectServiceRuntime } from '@/composables/useContainersApi';
import { Box, FileSearch } from '@lucide/vue';

const props = defineProps<{
  projectId: string;
  services: ComposeProjectServiceRuntime[];
}>();

const emit = defineEmits<{
  logs: [value: { projectId: string; service: string }];
}>();

function statusText(item: ComposeProjectServiceRuntime) {
  if (item.containerCount <= 0) {
    return 'not created';
  }

  if (item.runningCount === item.containerCount) {
    return `${item.runningCount}/${item.containerCount} running`;
  }

  if (item.runningCount === 0) {
    return `stopped (${item.containerCount})`;
  }

  return `partial (${item.runningCount}/${item.containerCount})`;
}

function statusColor(item: ComposeProjectServiceRuntime) {
  if (item.containerCount <= 0) {
    return 'secondary';
  }

  if (item.runningCount === item.containerCount) {
    return 'success';
  }

  if (item.runningCount === 0) {
    return 'warning';
  }

  return 'info';
}
</script>

<style scoped>
.service-list {
  background: transparent;
  padding: 0;
}

.service-list-item {
  border: 1px solid color-mix(in srgb, var(--foreground) calc(0.08 * 100%), transparent);
  padding-top: 2px;
  padding-bottom: 2px;
}

.service-list-item + .service-list-item {
  margin-top: 6px;
}

.service-container-chip {
  max-width: 100%;
}

.service-container-chip {
  overflow: hidden;
  text-overflow: ellipsis;
}
</style>
