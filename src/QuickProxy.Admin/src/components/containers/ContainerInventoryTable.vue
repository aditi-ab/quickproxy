<template>
  <Table>
    <TableHeader>
      <TableRow>
        <TableHead v-if="selectionEnabled" class="text-center">
          <Checkbox
            :model-value="someVisibleSelected ? 'indeterminate' : allVisibleSelected"
            aria-label="Select all visible containers" @update:model-value="toggleSelectAll($event === true)"
          />
        </TableHead>
        <TableHead v-for="header in containerHeaders.filter(header => header.key !== 'select')" :key="header.key">
          {{ header.title }}
        </TableHead>
      </TableRow>
    </TableHeader><TableBody>
      <template v-for="item in items" :key="item.name">
        <TableRow
          class="cursor-pointer"
          :class="{ 'container-drop-row': dragOverContainerName === item.name }" :data-container-name="item.name"
          tabindex="0"
          @click="emit('row-click', item)"
          @keydown.enter.prevent="emit('row-click', item)"
          @keydown.space.prevent="emit('row-click', item)"
        >
          <TableCell v-if="selectionEnabled" class="text-center" @click.stop>
            <Checkbox
              :model-value="isSelected(item.name)"
              @update:model-value="toggleSelected(item.name, $event)"
            />
          </TableCell>
          <TableCell>
            <div class="flex flex-col">
              <span>{{ item.name }}</span>
              <span v-if="item.compose.project && item.compose.service" class="text-xs text-muted-foreground">
                {{ `${item.compose.project}/${item.compose.service}` }}
              </span>
            </div>
          </TableCell>
          <TableCell>
            <div class="flex flex-wrap gap-1 py-1">
              <Badge :variant="item.state === 'running' ? 'success' : 'warning'">
                {{ item.state }}
              </Badge>
              <Badge v-if="item.imageUpdate?.updateAvailable" variant="warning">
                outdated
              </Badge>
            </div>
          </TableCell>
          <TableCell>
            <div class="flex flex-wrap gap-1 py-1">
              <Badge variant="secondary">
                {{ imageRepository(item.image) }}
              </Badge>
              <Badge variant="info">
                {{ imageTag(item.image) }}
              </Badge>
            </div>
          </TableCell>
          <TableCell>
            <div class="py-1 whitespace-nowrap">
              <div>{{ formatCpuPercent(item.stats?.cpuPercent) }}</div>
              <div class="text-xs text-muted-foreground">
                {{ formatBytes(item.stats?.memoryUsageBytes) }}
              </div>
            </div>
          </TableCell>
          <TableCell>
            <div class="py-1 whitespace-nowrap">
              <div>RX {{ formatBytes(item.stats?.networkRxBytes) }}</div>
              <div class="text-xs text-muted-foreground">
                TX {{ formatBytes(item.stats?.networkTxBytes) }}
              </div>
            </div>
          </TableCell>
          <TableCell>
            <div v-if="tableLabelEntries(item).length > 0" class="flex flex-wrap gap-1 p-1">
              <Badge v-for="label in tableLabelEntries(item)" :key="`${item.id}-${label.key}`" variant="secondary">
                {{ label.key }}={{ label.value }}
              </Badge>
            </div>
            <div v-else>
              -
            </div>
          </TableCell>
          <TableCell>
            <div class="flex items-center justify-end gap-1 py-1">
              <Tooltip>
                <TooltipTrigger as-child>
                  <Button
                    :aria-label="isExpanded(item.name) ? 'Collapse details' : 'Expand details'" size="icon-sm" variant="ghost"
                    @click.stop="toggleExpanded(item.name)"
                  >
                    <ChevronUp v-if="isExpanded(item.name)" /><ChevronDown v-else />
                  </Button>
                </TooltipTrigger><TooltipContent>{{ isExpanded(item.name) ? 'Collapse details' : 'Expand details' }}</TooltipContent>
              </Tooltip>
              <Tooltip v-if="item.state !== 'running'">
                <TooltipTrigger as-child>
                  <Button
                    aria-label="Start container" size="icon-sm" variant="ghost"
                    :disabled="busyContainerName === item.name && busyAction === 'start'"
                    @click.stop="emit('run-action', { name: item.name, action: 'start' })"
                  >
                    <Play />
                  </Button>
                </TooltipTrigger><TooltipContent>Start container</TooltipContent>
              </Tooltip>
              <Tooltip v-else>
                <TooltipTrigger as-child>
                  <Button
                    aria-label="Stop container" size="icon-sm" variant="ghost"
                    :disabled="busyContainerName === item.name && busyAction === 'stop'"
                    @click.stop="emit('run-action', { name: item.name, action: 'stop' })"
                  >
                    <Pause />
                  </Button>
                </TooltipTrigger><TooltipContent>Stop container</TooltipContent>
              </Tooltip>
              <Tooltip v-if="canRepull(item)">
                <TooltipTrigger as-child>
                  <Button
                    aria-label="Re-pull image and restart" size="icon-sm" variant="ghost"
                    :disabled="busyContainerName === item.name && busyAction === 'repull-restart'"
                    @click.stop="emit('run-action', { name: item.name, action: 'repull-restart' })"
                  >
                    <RefreshCw />
                  </Button>
                </TooltipTrigger><TooltipContent>Re-pull image and restart</TooltipContent>
              </Tooltip>
              <Tooltip v-if="item.state === 'running'">
                <TooltipTrigger as-child>
                  <Button aria-label="Open shell" size="icon-sm" variant="ghost" @click.stop="emit('open-shell', item.name)">
                    <SquareTerminal />
                  </Button>
                </TooltipTrigger><TooltipContent>Open shell</TooltipContent>
              </Tooltip>
              <Tooltip v-if="item.logsSupported">
                <TooltipTrigger as-child>
                  <span>
                    <Button
                      :aria-label="item.logsUnavailableReason || 'View live logs'"
                      size="icon-sm" variant="ghost" @click.stop="emit('open-logs', item.name)"
                    ><FileSearch /></Button>
                  </span>
                </TooltipTrigger><TooltipContent>{{ item.logsUnavailableReason || 'View live logs' }}</TooltipContent>
              </Tooltip>
            </div>
          </TableCell>
        </TableRow>
        <TableRow v-if="isExpanded(item.name)" class="container-metadata-row">
          <TableCell :colspan="containerHeaders.length">
            <div class="container-metadata-panel">
              <div class="text-base font-semibold mb-3">
                Runtime Metadata
              </div>
              <div class="container-metadata-grid mb-4">
                <div>
                  <div class="metadata-label">
                    Ports
                  </div>
                  <div class="metadata-value">
                    <div v-if="item.ports.length > 0" class="flex flex-wrap gap-1">
                      <Badge v-for="port in item.ports" :key="`${item.id}-${port.containerPort}-${port.protocol}`" variant="secondary">
                        <span v-if="port.publishedPorts.length > 0">
                          {{ port.publishedPorts.join(', ') }} > {{ port.containerPort }}/{{ port.protocol }}
                        </span>
                        <span v-else>
                          {{ port.containerPort }}/{{ port.protocol }}
                        </span>
                      </Badge>
                    </div>
                    <span v-else class="text-muted-foreground">No ports exposed.</span>
                  </div>
                </div>
                <div>
                  <div class="metadata-label">
                    Networks
                  </div>
                  <div class="metadata-value">
                    <div v-if="item.networks.length > 0" class="flex flex-wrap gap-1">
                      <Badge v-for="network in item.networks" :key="`${item.id}-${network.name}`" variant="secondary">
                        {{ network.name }}{{ network.ipAddress ? ` (${network.ipAddress})` : '' }}
                      </Badge>
                    </div>
                    <span v-else class="text-muted-foreground">No networks available.</span>
                  </div>
                </div>
                <div>
                  <div class="metadata-label">
                    Compose
                  </div>
                  <div class="metadata-value">
                    {{ item.compose.project && item.compose.service ? `${item.compose.project}/${item.compose.service}` : '-' }}
                  </div>
                </div>
                <div>
                  <div class="metadata-label">
                    Stats
                  </div>
                  <div class="metadata-value">
                    CPU {{ formatCpuPercent(item.stats?.cpuPercent) }},
                    Memory {{ formatBytes(item.stats?.memoryUsageBytes) }},
                    RX {{ formatBytes(item.stats?.networkRxBytes) }},
                    TX {{ formatBytes(item.stats?.networkTxBytes) }}
                  </div>
                </div>
              </div>
              <div class="mb-4">
                <div class="metadata-label mb-2">
                  Labels
                </div>
                <div v-if="expandedLabelEntries(item).length > 0" class="flex flex-wrap gap-1">
                  <Badge v-for="label in expandedLabelEntries(item)" :key="`${item.id}-${label.key}`" variant="secondary">
                    {{ label.key }}={{ label.value }}
                  </Badge>
                </div>
                <div v-else class="metadata-value">
                  No container-only labels.
                </div>
              </div>
              <div class="text-base font-semibold mb-3">
                Image Metadata
              </div>
              <div class="container-metadata-grid">
                <div>
                  <div class="metadata-label">
                    Update Status
                  </div>
                  <div class="metadata-value">
                    <Badge :variant="metadataColor(item)">
                      {{ item.imageUpdate?.status ?? 'unknown' }}
                    </Badge>
                  </div>
                </div>
                <div>
                  <div class="metadata-label">
                    Source
                  </div>
                  <div class="metadata-value">
                    {{ item.imageUpdate?.source ?? '-' }}
                  </div>
                </div>
                <div>
                  <div class="metadata-label">
                    Checked
                  </div>
                  <div class="metadata-value">
                    {{ formatUtc(item.imageUpdate?.checkedAtUtc) }}
                  </div>
                </div>
                <div>
                  <div class="metadata-label">
                    Remote Created
                  </div>
                  <div class="metadata-value">
                    {{ formatUtc(item.imageUpdate?.remoteCreatedUtc) }}
                  </div>
                </div>
                <div>
                  <div class="metadata-label">
                    Local Platform
                  </div>
                  <div class="metadata-value">
                    {{ formatPlatform(item.imageOs, item.imageArchitecture) }}
                  </div>
                </div>
                <div>
                  <div class="metadata-label">
                    Remote Platform
                  </div>
                  <div class="metadata-value">
                    {{ formatPlatform(item.imageUpdate?.remoteOs, item.imageUpdate?.remoteArchitecture) }}
                  </div>
                </div>
              </div>
              <div class="digest-metadata-grid mt-3">
                <div class="digest-metadata-item">
                  <div class="metadata-label">
                    Local Digest
                  </div>
                  <code class="metadata-value metadata-mono">
                    {{ item.imageUpdate?.localDigest ?? item.imageDigest ?? '-' }}
                  </code>
                </div>
                <div class="digest-metadata-item">
                  <div class="metadata-label">
                    Remote Digest
                  </div>
                  <code class="metadata-value metadata-mono">
                    {{ item.imageUpdate?.remoteDigest ?? '-' }}
                  </code>
                </div>
              </div>
              <div v-if="item.imageUpdate?.error" class="mt-3">
                <div class="metadata-label">
                  Error
                </div>
                <div class="metadata-value text-amber-600 dark:text-amber-300">
                  {{ item.imageUpdate.error }}
                </div>
              </div>
              <div class="mt-3">
                <div class="metadata-label mb-2">
                  Remote Labels
                </div>
                <div v-if="remoteLabels(item).length > 0" class="flex flex-wrap gap-1">
                  <Badge v-for="label in remoteLabels(item)" :key="`${item.id}-remote-${label.key}`" variant="info">
                    {{ label.key }}={{ label.value }}
                  </Badge>
                </div>
                <div v-else class="metadata-value">
                  No remote labels available.
                </div>
              </div>
            </div>
          </TableCell>
        </TableRow>
      </template>
      <TableEmpty v-if="items.length === 0" :colspan="containerHeaders.length">
        {{ noDataText }}
      </TableEmpty>
    </TableBody>
  </Table>
</template>

<script lang="ts" setup>
import type { ContainerInventoryItem } from '@/composables/useContainersApi';
import { ChevronDown, ChevronUp, FileSearch, Pause, Play, RefreshCw, SquareTerminal } from '@lucide/vue';

import { formatISO } from 'date-fns';

import { computed, ref } from 'vue';

const props = withDefaults(defineProps<{
  items: ContainerInventoryItem[];
  busyContainerName: string;
  busyAction: string;
  selectedContainerNames?: string[];
  selectionEnabled?: boolean;
  dragOverContainerName?: string;
  noDataText?: string;
}>(), {
  selectedContainerNames: () => [],
  selectionEnabled: false,
  dragOverContainerName: '',
  noDataText: 'No containers discovered.',
});

const emit = defineEmits<{
  'update:selectedContainerNames': [value: string[]];
  'row-click': [item: ContainerInventoryItem];
  'run-action': [value: { name: string; action: 'start' | 'stop' | 'repull-restart' }];
  'open-shell': [name: string];
  'open-logs': [name: string];
}>();

const expandedRows = ref<string[]>([]);
const visibleSelectableNames = computed(() => props.items.map(item => item.name));
const allVisibleSelected = computed(() => visibleSelectableNames.value.length > 0
  && visibleSelectableNames.value.every(name => props.selectedContainerNames.includes(name)));
const someVisibleSelected = computed(() => !allVisibleSelected.value
  && visibleSelectableNames.value.some(name => props.selectedContainerNames.includes(name)));
const containerHeaders = computed(() => {
  const headers = [
    { title: 'Name', key: 'name' },
    { title: 'State', key: 'state' },
    { title: 'Image', key: 'image' },
    { title: 'CPU/MEM', key: 'cpuMem', sortable: false },
    { title: 'Network I/O', key: 'networkIo', sortable: false },
    { title: 'Labels', key: 'labels', sortable: false },
    { title: 'Actions', key: 'actions', sortable: false, align: 'end' as const, width: 184 },
  ];

  return props.selectionEnabled
    ? [{ title: '', key: 'select', sortable: false, width: 48 }, ...headers]
    : headers;
});

function imageRepository(value: string) {
  const atIndex = value.indexOf('@');
  const normalized = atIndex >= 0 ? value.slice(0, atIndex) : value;
  const lastColon = normalized.lastIndexOf(':');
  const lastSlash = normalized.lastIndexOf('/');

  return lastColon > lastSlash ? normalized.slice(0, lastColon) : normalized;
}

function imageTag(value: string) {
  const atIndex = value.indexOf('@');

  if (atIndex >= 0) {
    return value.slice(atIndex + 1);
  }

  const lastColon = value.lastIndexOf(':');
  const lastSlash = value.lastIndexOf('/');

  return lastColon > lastSlash ? value.slice(lastColon + 1) : 'latest';
}

function tableLabelEntries(item: ContainerInventoryItem) {
  const merged = new Map<string, string>();

  for (const [key, value] of Object.entries(item.imageLabels)) {
    if (key && value && !key.startsWith('com.docker.') && !key.includes('.')) {
      merged.set(key, value);
    }
  }

  for (const [key, value] of Object.entries(item.containerLabels)) {
    if (!key.startsWith('quickproxy.internal.') && !key.startsWith('com.docker.') && item.imageLabels[key] !== value) {
      merged.set(key, value);
    }
  }

  return Array.from(merged.entries())
    .sort(([left], [right]) => left.localeCompare(right))
    .slice(0, 8)
    .map(([key, value]) => ({ key, value }));
}

function expandedLabelEntries(item: ContainerInventoryItem) {
  const merged = new Map<string, string>();

  for (const [key, value] of Object.entries(item.imageLabels)) {
    if (key && value) {
      merged.set(key, value);
    }
  }

  for (const [key, value] of Object.entries(item.containerLabels)) {
    if (!key.startsWith('quickproxy.internal.') && item.imageLabels[key] !== value) {
      merged.set(key, value);
    }
  }

  return Array.from(merged.entries())
    .sort(([left], [right]) => left.localeCompare(right))
    .map(([key, value]) => ({ key, value }));
}

function canRepull(item: ContainerInventoryItem) {
  return item.containerLabels['quickproxy.internal.image-source'] !== 'archive';
}

function isSelected(name: string) {
  return props.selectedContainerNames.includes(name);
}

function toggleSelected(name: string, value: boolean | null) {
  if (value) {
    if (!isSelected(name)) {
      emit('update:selectedContainerNames', [...props.selectedContainerNames, name]);
    }

    return;
  }

  emit('update:selectedContainerNames', props.selectedContainerNames.filter(x => x !== name));
}

function toggleSelectAll(value: boolean | null) {
  if (value) {
    emit('update:selectedContainerNames', Array.from(new Set([
      ...props.selectedContainerNames,
      ...visibleSelectableNames.value,
    ])));
    return;
  }

  const visible = new Set(visibleSelectableNames.value);

  emit('update:selectedContainerNames', props.selectedContainerNames.filter(name => !visible.has(name)));
}

function isExpanded(name: string) {
  return expandedRows.value.includes(name);
}

function toggleExpanded(name: string) {
  expandedRows.value = isExpanded(name)
    ? expandedRows.value.filter(x => x !== name)
    : [...expandedRows.value, name];
}

function metadataColor(item: ContainerInventoryItem) {
  switch (item.imageUpdate?.status ?? 'unknown') {
    case 'current': return 'success';
    case 'outdated': return 'warning';
    case 'unsupported': return 'secondary';
    case 'error': return 'destructive';
    default: return 'secondary';
  }
}

function remoteLabels(item: ContainerInventoryItem) {
  return Object.entries(item.imageUpdate?.remoteLabels ?? {})
    .sort(([left], [right]) => left.localeCompare(right))
    .map(([key, value]) => ({ key, value }));
}

function formatPlatform(os?: string | null, architecture?: string | null) {
  if (!os && !architecture) {
    return '-';
  }

  if (!os) {
    return architecture ?? '-';
  }

  if (!architecture) {
    return os;
  }

  return `${os}/${architecture}`;
}

function formatCpuPercent(value?: number | null) {
  if (value == null || Number.isNaN(value)) {
    return '-';
  }

  return `${value.toFixed(value >= 10 ? 0 : 1)}%`;
}

function formatBytes(value?: number | null) {
  if (value == null || Number.isNaN(value)) {
    return '-';
  }

  if (value === 0) {
    return '0 B';
  }

  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let size = value;
  let unitIndex = 0;

  while (size >= 1024 && unitIndex < units.length - 1) {
    size /= 1024;
    unitIndex += 1;
  }

  return `${size.toFixed(size >= 10 || unitIndex === 0 ? 0 : 1)} ${units[unitIndex]}`;
}

function formatUtc(value?: string | null) {
  if (!value) {
    return '-';
  }

  const date = new Date(value);

  return Number.isNaN(date.getTime())
    ? value
    : formatISO(date, { representation: 'complete' }).replace('T', ' ').replace('Z', ' UTC');
}
</script>

<style scoped>
.container-drop-row > td {
  background-color: rgba(33, 150, 243, 0.08) !important;
}

:deep(tbody tr[data-container-name]) {
  cursor: pointer;
}

.container-metadata-row > td {
  max-width: 0;
  padding: 0 !important;
  background-color: var(--muted);
}

.container-metadata-panel {
  min-width: 0;
  overflow: hidden;
  padding: 16px 20px;
}

.container-metadata-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 12px 20px;
}

.container-metadata-grid > * {
  min-width: 0;
}

.digest-metadata-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.digest-metadata-item {
  min-width: 0;
  padding: 12px;
  border: 1px solid var(--border);
  border-radius: var(--radius);
  background-color: var(--background);
}

.metadata-label {
  color: var(--muted-foreground);
  font-size: 0.78rem;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.metadata-value {
  min-width: 0;
  margin-top: 4px;
  line-height: 1.4;
  overflow-wrap: anywhere;
}

.metadata-mono {
  display: block;
  white-space: normal;
  font-family: Consolas, 'Courier New', monospace;
  font-size: 0.78rem;
  word-break: break-all;
}

@media (max-width: 900px) {
  .digest-metadata-grid {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
