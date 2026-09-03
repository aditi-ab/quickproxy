<template>
  <div class="page-container">
    <Alert v-if="errorMessage" class="mb-4" variant="destructive">
      <CircleAlert /><AlertDescription>{{ errorMessage }}</AlertDescription>
    </Alert>

    <header class="mb-6 flex flex-wrap items-end justify-between gap-4">
      <div>
        <div class="eyebrow">
          Administration
        </div><h1 class="page-title mt-1">
          Audit
        </h1><p class="page-lead">
          Review administrative and runtime activity across QuickProxy.
        </p>
      </div><div class="flex flex-wrap items-center gap-2">
        <Button variant="secondary" @click="loadAuditEvents" :disabled="loading">
          <Spinner v-if="loading" /><RefreshCw />
          Refresh
        </Button>
      </div>
    </header>

    <Card class="mb-4">
      <CardHeader class="border-b">
        <CardTitle>Filters</CardTitle>
      </CardHeader>
      <CardContent>
        <div class="grid grid-cols-12 gap-4">
          <div class="col-span-12 md:col-span-2">
            <Field><FieldLabel>Module</FieldLabel><Input v-model="filters.module" /></Field>
          </div>
          <div class="col-span-12 md:col-span-2">
            <Field><FieldLabel>Action</FieldLabel><Input v-model="filters.action" /></Field>
          </div>
          <div class="col-span-12 md:col-span-3">
            <Field><FieldLabel>Actor</FieldLabel><Input v-model="filters.actor" /></Field>
          </div>
          <div class="col-span-12 md:col-span-3">
            <Field><FieldLabel>Target</FieldLabel><Input v-model="filters.target" /></Field>
          </div>
          <div class="col-span-12 md:col-span-2">
            <Field>
              <FieldLabel>Outcome</FieldLabel><Select v-model="outcomeFilterModel">
                <SelectTrigger><SelectValue placeholder="Outcome" /></SelectTrigger><SelectContent>
                  <SelectItem :value="ALL_OUTCOMES_VALUE">
                    All outcomes
                  </SelectItem>
                  <SelectItem value="success">
                    Success
                  </SelectItem>
                  <SelectItem value="failure">
                    Failure
                  </SelectItem>
                </SelectContent>
              </Select>
            </Field>
          </div>
        </div>
      </CardContent>
    </Card>

    <Card>
      <CardHeader class="border-b py-4">
        <CardTitle>Events</CardTitle>
        <CardAction class="text-sm text-muted-foreground">
          {{ auditEvents.length }} of {{ totalCount }}
        </CardAction>
      </CardHeader>
      <CardContent class="px-0">
        <Table>
          <TableHeader><TableRow><TableHead>Time</TableHead><TableHead>Module</TableHead><TableHead>Action</TableHead><TableHead>Target</TableHead><TableHead>Actor</TableHead><TableHead>Outcome</TableHead><TableHead>Summary</TableHead></TableRow></TableHeader><TableBody>
            <TableRow v-for="item in auditEvents" :key="item.id" class="cursor-pointer" @click="onRowClick(item)">
              <TableCell><span class="whitespace-nowrap">{{ formatTimestamp(item.occurredAtUtc) }}</span></TableCell><TableCell>
                <Badge variant="secondary">
                  {{ item.module }}
                </Badge>
              </TableCell><TableCell>
                <Badge variant="info">
                  {{ item.action }}
                </Badge>
              </TableCell><TableCell>{{ formatTarget(item.targetType, item.targetId) }}</TableCell><TableCell>{{ item.actor.displayName || item.actor.id || '-' }}</TableCell><TableCell>
                <Badge :variant="item.outcome === 'success' ? 'success' : 'destructive'">
                  {{ item.outcome }}
                </Badge>
              </TableCell><TableCell class="text-muted-foreground">
                {{ item.summary || item.error || '-' }}
              </TableCell>
            </TableRow><TableEmpty v-if="auditEvents.length === 0" :colspan="7">
              No audit events found.
            </TableEmpty>
          </TableBody>
        </Table>
      </CardContent>
    </Card>

    <Dialog v-model:open="detailsDialog">
      <DialogContent size="4xl" scrollable>
        <DialogHeader>
          <DialogTitle class="flex items-center">
            <span>Audit Event</span>
            <span class="ml-auto" />
            <Badge :variant="selectedEvent?.outcome === 'success' ? 'success' : 'destructive'">
              {{ selectedEvent?.outcome ?? 'unknown' }}
            </Badge>
          </DialogTitle>
          <DialogDescription>Review event metadata and the redacted summary of recorded changes.</DialogDescription>
        </DialogHeader>
        <div data-slot="dialog-body" class="dialog-body-content -mx-4 overflow-x-hidden px-4">
          <template v-if="selectedEvent">
            <div class="audit-detail-grid mb-4">
              <div>
                <div class="metadata-label">
                  Time
                </div>
                <div class="metadata-value">
                  {{ formatTimestamp(selectedEvent.occurredAtUtc) }}
                </div>
              </div>
              <div>
                <div class="metadata-label">
                  Module
                </div>
                <div class="metadata-value">
                  {{ selectedEvent.module }}
                </div>
              </div>
              <div>
                <div class="metadata-label">
                  Action
                </div>
                <div class="metadata-value">
                  {{ selectedEvent.action }}
                </div>
              </div>
              <div>
                <div class="metadata-label">
                  Target
                </div>
                <div class="metadata-value">
                  {{ formatTarget(selectedEvent.targetType, selectedEvent.targetId) }}
                </div>
              </div>
              <div>
                <div class="metadata-label">
                  Actor
                </div>
                <div class="metadata-value">
                  {{ selectedEvent.actor.displayName || selectedEvent.actor.id || '-' }}
                </div>
              </div>
              <div>
                <div class="metadata-label">
                  Source
                </div>
                <div class="metadata-value">
                  {{ selectedEvent.source }}
                </div>
              </div>
              <div>
                <div class="metadata-label">
                  Status Code
                </div>
                <div class="metadata-value">
                  {{ selectedEvent.statusCode ?? '-' }}
                </div>
              </div>
              <div>
                <div class="metadata-label">
                  Correlation Id
                </div>
                <div class="metadata-value metadata-mono">
                  {{ selectedEvent.correlationId || '-' }}
                </div>
              </div>
            </div>

            <Alert v-if="selectedEvent.error" class="mb-4" variant="destructive">
              <CircleAlert /><AlertDescription>{{ selectedEvent.error }}</AlertDescription>
            </Alert>

            <div v-if="selectedEvent.changes?.summary" class="mb-4">
              <div class="metadata-label">
                Summary
              </div>
              <div class="metadata-value">
                {{ selectedEvent.changes.summary }}
              </div>
            </div>

            <div>
              <div class="metadata-label mb-2">
                Change Summary
              </div>
              <Table>
                <thead>
                  <TableRow>
                    <TableHead>Path</TableHead>
                    <TableHead>Kind</TableHead>
                    <TableHead>Before</TableHead>
                    <TableHead>After</TableHead>
                  </TableRow>
                </thead>
                <tbody>
                  <TableRow v-for="field in selectedEvent.changes?.fields ?? []" :key="field.path">
                    <TableCell class="metadata-mono">
                      {{ field.path }}
                    </TableCell>
                    <TableCell>{{ field.kind }}</TableCell>
                    <TableCell>{{ field.before || '-' }}</TableCell>
                    <TableCell>{{ field.after || '-' }}</TableCell>
                  </TableRow>
                  <TableRow v-if="!selectedEvent.changes?.fields?.length">
                    <TableCell colspan="4" class="text-muted-foreground">
                      No change summary available.
                    </TableCell>
                  </TableRow>
                </tbody>
              </Table>
            </div>
          </template>
        </div>
        <DialogFooter>
          <span class="ml-auto" />
          <Button variant="ghost" @click="detailsDialog = false">
            Close
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>

<script lang="ts" setup>
import type { AuditEvent, AuditEventListItem } from '@/composables/useAuditApi';
import { CircleAlert } from '@lucide/vue';
import { format } from 'date-fns';
import { computed, onMounted, reactive, ref, watch } from 'vue';
import { useAuditApi } from '@/composables/useAuditApi';

const auditApi = useAuditApi();

const loading = ref(false);
const errorMessage = ref('');
const totalCount = ref(0);
const auditEvents = ref<AuditEventListItem[]>([]);
const selectedEvent = ref<AuditEvent | null>(null);
const detailsDialog = ref(false);

const filters = reactive({
  module: '',
  action: '',
  actor: '',
  target: '',
  outcome: '',
});

const ALL_OUTCOMES_VALUE = '__all_outcomes__';
const outcomeFilterModel = computed({
  get: () => filters.outcome || ALL_OUTCOMES_VALUE,
  set: value => filters.outcome = value === ALL_OUTCOMES_VALUE ? '' : value,
});

const filterSignature = computed(() => JSON.stringify(filters));

onMounted(async () => {
  await loadAuditEvents();
});

watch(filterSignature, async () => {
  await loadAuditEvents();
});

async function loadAuditEvents() {
  loading.value = true;
  errorMessage.value = '';

  try {
    const response = await auditApi.listAuditEvents({
      ...filters,
      limit: 200,
      offset: 0,
    });

    auditEvents.value = response.items;
    totalCount.value = response.total;
  }
  catch (error) {
    errorMessage.value = toMessage(error);
  }
  finally {
    loading.value = false;
  }
}

async function onRowClick(item: AuditEventListItem) {
  try {
    errorMessage.value = '';
    selectedEvent.value = await auditApi.getAuditEvent(item.id);
    detailsDialog.value = true;
  }
  catch (error) {
    errorMessage.value = toMessage(error);
  }
}

function formatTimestamp(value?: string | null) {
  if (!value) {
    return '-';
  }

  const date = new Date(value);

  return Number.isNaN(date.getTime()) ? value : format(date, 'yyyy-MM-dd HH:mm:ss');
}

function formatTarget(type?: string | null, id?: string | null) {
  if (!type && !id) {
    return '-';
  }

  if (!type) {
    return id ?? '-';
  }

  if (!id) {
    return type;
  }

  return `${type}: ${id}`;
}

function toMessage(error: unknown) {
  return error instanceof Error ? error.message : String(error);
}
</script>

<style scoped>
.audit-detail-grid {
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

.metadata-mono {
  font-family: Consolas, 'Courier New', monospace;
  word-break: break-all;
}
</style>
