<template>
  <Card>
    <CardHeader class="border-b py-4">
      <CardTitle>Containers</CardTitle>
      <CardAction class="flex flex-wrap items-center justify-end gap-2">
        <Badge :variant="status?.eventStreamConnected ? 'success' : 'warning'">
          Events: {{ status?.eventStreamConnected ? 'Connected' : 'Disconnected' }}
        </Badge>
        <Badge variant="default">
          Containers: {{ filteredContainers.length }}
        </Badge>
        <Badge variant="secondary">
          Last refresh: {{ status?.lastSuccessfulRefreshUtc
            ? format(status.lastSuccessfulRefreshUtc, 'yyyy-MM-dd HH:mm:ss')
            : 'Never' }}
        </Badge>
      </CardAction>
    </CardHeader>
    <CardContent class="container-toolbar">
      <div class="container-toolbar__filters">
        <Field class="container-toolbar__toggle" orientation="horizontal">
          <Switch id="show-running-only" v-model="showOnlyRunningModel" /><FieldLabel for="show-running-only">
            Show running only
          </FieldLabel>
        </Field>
        <Field class="container-toolbar__toggle" orientation="horizontal">
          <Switch id="show-system-containers" v-model="showSystemModel" /><FieldLabel for="show-system-containers">
            Show system containers
          </FieldLabel>
        </Field>
      </div>
      <div class="container-toolbar__actions">
        <div class="container-toolbar__project-filter">
          <Select v-model="selectedProjectModel">
            <SelectTrigger aria-label="Filter by project">
              <SelectValue placeholder="All projects" />
            </SelectTrigger><SelectContent>
              <SelectItem v-for="option in projectFilterOptions" :key="option.value" :value="option.value">
                {{ option.title }}
              </SelectItem>
            </SelectContent>
          </Select>
        </div>
        <div class="container-toolbar__bulk-actions">
          <DropdownMenu>
            <DropdownMenuTrigger as-child>
              <Button
                variant="outline" :disabled="bulkActionRunning || selectedContainerNames.length === 0"
              >
                <Spinner v-if="bulkActionRunning" />
                Actions{{ selectedContainerNames.length > 0 ? ` (${selectedContainerNames.length})` : '' }}
              </Button>
            </DropdownMenuTrigger><DropdownMenuContent align="end">
              <DropdownMenuItem
                v-for="action in bulkActionOptions.filter(x => x.value !== 'delete')" :key="action.value"
                @select="emit('bulk-action', action.value)"
              >
                {{ action.title }}
              </DropdownMenuItem>
              <DropdownMenuSeparator />
              <DropdownMenuItem
                v-for="action in bulkActionOptions.filter(x => x.value === 'delete')" :key="action.value"
                class="text-destructive" @select="emit('bulk-action', action.value)"
              >
                {{ action.title }}
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        </div>
      </div>
      <div v-if="status?.lastError" class="container-toolbar__error text-sm text-destructive">
        {{ status.lastError }}
      </div>
    </CardContent>

    <Separator />

    <CardContent class="px-0">
      <ContainerInventoryTable
        :items="filteredContainers" :selected-container-names="selectedContainerNames"
        :selection-enabled="true" :busy-container-name="busyContainerName" :busy-action="busyAction"
        :drag-over-container-name="dragOverContainerName"
        @update:selected-container-names="emit('update:selectedContainerNames', $event)"
        @row-click="emit('row-click', $event)" @run-action="emit('run-action', $event)"
        @open-shell="emit('open-shell', $event)" @open-logs="emit('open-logs', $event)"
      />
    </CardContent>
  </Card>
</template>

<script lang="ts" setup>
import type { ContainerInventoryItem, ContainerInventoryStatus } from '@/composables/useContainersApi';

import { format } from 'date-fns';
import { computed } from 'vue';
import ContainerInventoryTable from '@/components/containers/ContainerInventoryTable.vue';

const props = defineProps<{
  status: ContainerInventoryStatus | null;
  filteredContainers: ContainerInventoryItem[];
  selectedContainerNames: string[];
  bulkActionRunning: boolean;
  bulkActionOptions: ReadonlyArray<{ value: 'restart' | 'start' | 'stop' | 'delete' | 'repull-restart'; title: string; icon: string }>;
  showOnlyRunningContainers: boolean;
  showSystemContainers: boolean;
  selectedProjectFilter: string;
  projectFilterOptions: { title: string; value: string }[];
  busyContainerName: string;
  busyAction: string;
  isDraggingArchive: boolean;
  dragOverContainerName: string;
}>();

const emit = defineEmits<{
  'update:showOnlyRunningContainers': [value: boolean];
  'update:showSystemContainers': [value: boolean];
  'update:selectedProjectFilter': [value: string];
  'update:selectedContainerNames': [value: string[]];
  'bulk-action': [value: 'restart' | 'start' | 'stop' | 'delete' | 'repull-restart'];
  'row-click': [item: ContainerInventoryItem];
  'run-action': [value: { name: string; action: 'start' | 'stop' | 'repull-restart' }];
  'open-shell': [name: string];
  'open-logs': [name: string];
}>();

const showOnlyRunningModel = computed({
  get: () => props.showOnlyRunningContainers,
  set: value => emit('update:showOnlyRunningContainers', value),
});

const showSystemModel = computed({
  get: () => props.showSystemContainers,
  set: value => emit('update:showSystemContainers', value),
});

const selectedProjectModel = computed({
  get: () => props.selectedProjectFilter,
  set: value => emit('update:selectedProjectFilter', value),
});
</script>

<style scoped>
.container-table-surface--dragging {
  background-color: rgba(33, 150, 243, 0.03);
}
</style>
