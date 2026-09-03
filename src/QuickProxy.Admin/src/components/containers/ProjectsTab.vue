<template>
  <Card>
    <CardHeader class="border-b py-4">
      <CardTitle>Projects</CardTitle>
      <CardAction class="flex flex-wrap items-center justify-end gap-2">
        <Badge variant="default">
          Projects: {{ projects.length }}
        </Badge>
        <Badge variant="info">
          Running: {{ runningProjects }}
        </Badge>
      </CardAction>
    </CardHeader>

    <CardContent class="px-0">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Project</TableHead><TableHead>Status</TableHead><TableHead>Last deploy</TableHead><TableHead>Last error</TableHead><TableHead class="text-right">
              Actions
            </TableHead>
          </TableRow>
        </TableHeader><TableBody>
          <template v-for="item in projects" :key="item.project.id">
            <TableRow class="cursor-pointer" @click="emit('row-click', item.project.id)">
              <TableCell class="font-medium">
                {{ item.project.id }}
              </TableCell><TableCell>
                <Badge variant="secondary">
                  {{ item.runtime.status }}
                </Badge>
              </TableCell><TableCell>{{ formatUtc(item.project.lastDeployAtUtc) }}</TableCell><TableCell class="text-xs">
                {{ item.project.lastError || '-' }}
              </TableCell><TableCell>
                <div class="flex flex-wrap justify-end gap-1">
                  <Button size="sm" variant="ghost" @click.stop="toggleExpanded(item.project.id)">
                    {{ isExpanded(item.project.id) ? 'Collapse' : 'Details' }}
                  </Button>
                  <Button v-if="isStarted(item)" size="sm" variant="outline" :disabled="isBusy(item.project.id, 'stop')" @click.stop="emit('action', { id: item.project.id, action: 'stop' })">
                    Stop
                  </Button>
                  <Button v-else size="sm" variant="outline" :disabled="isBusy(item.project.id, playAction(item))" @click.stop="emit('action', { id: item.project.id, action: playAction(item) })">
                    {{ playActionLabel(item) }}
                  </Button>
                  <Button size="sm" variant="ghost" :disabled="isBusy(item.project.id, 'restart')" @click.stop="emit('action', { id: item.project.id, action: 'restart' })">
                    Restart
                  </Button>
                  <Button size="sm" variant="ghost" :disabled="isBusy(item.project.id, 'pull')" @click.stop="emit('action', { id: item.project.id, action: 'pull' })">
                    Pull
                  </Button>
                  <Button size="sm" variant="destructive" :disabled="isBusy(item.project.id, 'down')" @click.stop="emit('action', { id: item.project.id, action: 'down' })">
                    Remove
                  </Button>
                  <Button size="sm" variant="ghost" @click.stop="emit('logs', { id: item.project.id })">
                    Logs
                  </Button>
                </div>
              </TableCell>
            </TableRow>
            <TableRow v-if="isExpanded(item.project.id)" class="project-metadata-row">
              <TableCell :colspan="5">
                <div class="project-metadata-panel">
                  <div class="text-base font-semibold mb-3">
                    Runtime
                  </div>
                  <div class="project-metadata-grid mb-4">
                    <div>
                      <div class="metadata-label">
                        Status
                      </div>
                      <div class="metadata-value">
                        <Badge :variant="statusColor(item.runtime.status)">
                          {{ item.runtime.status }}
                        </Badge>
                      </div>
                    </div>
                    <div>
                      <div class="metadata-label">
                        Services
                      </div>
                      <div class="metadata-value">
                        {{ item.runtime.serviceCount }}
                      </div>
                    </div>
                    <div>
                      <div class="metadata-label">
                        Containers
                      </div>
                      <div class="metadata-value">
                        {{ item.runtime.containerCount }}
                      </div>
                    </div>
                    <div>
                      <div class="metadata-label">
                        Project Name
                      </div>
                      <div class="metadata-value">
                        {{ item.runtime.projectName || '-' }}
                      </div>
                    </div>
                  </div>
                  <div>
                    <div class="metadata-label mb-2">
                      Services
                    </div>
                    <ComposeProjectServicesTable
                      :project-id="item.project.id" :services="item.runtime.services"
                      @logs="onServiceLogs"
                    />
                  </div>
                </div>
              </TableCell>
            </TableRow>
          </template>
          <TableEmpty v-if="projects.length === 0" :colspan="5">
            No compose projects configured.
          </TableEmpty>
        </TableBody>
      </Table>
    </CardContent>
  </Card>
</template>

<script lang="ts" setup>
import type { ComposeProjectListItem } from '@/composables/useContainersApi';

import { format } from 'date-fns';

import { computed, ref } from 'vue';
import ComposeProjectServicesTable from '@/components/containers/ComposeProjectServicesTable.vue';

const props = defineProps<{
  projects: ComposeProjectListItem[];
  busyProjectId: string;
  busyAction: string;
}>();

const emit = defineEmits<{
  'row-click': [id: string];
  'action': [value: { id: string; action: 'deploy' | 'start' | 'stop' | 'restart' | 'pull' | 'down' }];
  'logs': [value: { id: string; service?: string }];
}>();

const headers = [
  { title: 'Project', key: 'project.id' },
  { title: 'Status', key: 'status', sortable: false },
  { title: 'Last Deploy', key: 'lastDeployAtUtc' },
  { title: 'Last Error', key: 'lastError', sortable: false },
  { title: 'Actions', key: 'actions', sortable: false, align: 'end' as const, width: 272 },
];

const runningProjects = computed(() => props.projects.filter(x => x.runtime.status === 'running').length);
const expandedRows = ref<string[]>([]);

function onRowClick(_event: Event, row: { item: ComposeProjectListItem }) {
  emit('row-click', row.item.project.id);
}

function statusColor(value: string) {
  switch (value) {
    case 'running':
      return 'success';
    case 'partial':
      return 'warning';
    case 'stopped':
      return 'secondary';
    default:
      return 'info';
  }
}

function isStarted(item: ComposeProjectListItem) {
  return item.runtime.status === 'running' || item.runtime.status === 'partial';
}

function isBusy(id: string, action: 'deploy' | 'start' | 'stop' | 'restart' | 'pull' | 'down') {
  return props.busyProjectId === id && props.busyAction === action;
}

function playAction(item: ComposeProjectListItem): 'deploy' | 'start' {
  return item.runtime.containerCount > 0 ? 'start' : 'deploy';
}

function playActionLabel(item: ComposeProjectListItem) {
  return playAction(item) === 'deploy' ? 'Deploy project' : 'Start project';
}

function isExpanded(id: string) {
  return expandedRows.value.includes(id);
}

function toggleExpanded(id: string) {
  expandedRows.value = isExpanded(id)
    ? expandedRows.value.filter(x => x !== id)
    : [...expandedRows.value, id];
}

function onServiceLogs(value: { projectId: string; service: string }) {
  emit('logs', { id: value.projectId, service: value.service });
}

function formatUtc(value?: string | null) {
  if (!value) {
    return '-';
  }

  const parsed = new Date(value);

  return Number.isNaN(parsed.getTime())
    ? value
    : format(parsed, 'yyyy-MM-dd HH:mm:ss');
}
</script>

<style scoped>
.project-metadata-row > td {
  padding: 0 !important;
  background-color: var(--muted);
}

.project-metadata-panel {
  padding: 16px 20px;
}

.project-metadata-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 12px 20px;
}

.metadata-label {
  color: var(--muted-foreground);
  font-size: 0.78rem;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.metadata-value {
  margin-top: 4px;
  line-height: 1.4;
}
</style>
