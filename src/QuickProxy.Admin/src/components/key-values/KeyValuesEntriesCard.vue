<template>
  <Card
    :aria-busy="loadingSelectedEntry"
    :class="[{ 'keys-drop-active': isDragOverKeys }]" @dragenter.prevent="emit('keys-dragenter')"
    @dragleave.prevent="emit('keys-dragleave', $event)" @dragover.prevent="emit('keys-dragover')"
    @drop.prevent="emit('keys-drop', $event)"
  >
    <CardContent class="key-values-location">
      <div class="text-xs text-muted-foreground">
        Current location
      </div>
      <nav class="flex flex-wrap items-center text-sm font-medium" aria-label="Current configuration path">
        <Button
          size="sm" class="px-1" variant="ghost" style="min-width: auto"
          @click="emit('selected-path-click', '')"
        >
          <span>All configuration</span>
        </Button>
        <span v-if="selectedFolderSegments.length > 0" class="px-1 text-muted-foreground">/</span>
        <template v-for="(segment, si) in selectedFolderSegments" :key="`${segment.path}:${segment.type}`">
          <Button
            size="sm" class="px-1" variant="ghost" :disabled="segment.type !== 'folder'"
            style="min-width: auto" @click="emit('selected-path-click', segment.path)"
          >
            <span>{{ segment.name }}</span>
          </Button>
          <span v-if="si < selectedFolderSegments.length - 1" class="px-1 text-muted-foreground">/</span>
        </template>
      </nav>
    </CardContent>

    <Separator />

    <CardHeader class="border-b py-4">
      <CardTitle>
        <div>
          <div>Entries</div>
          <div class="text-xs text-muted-foreground font-normal">
            {{ visibleEntries.length }} {{ visibleEntries.length === 1 ? 'item' : 'items' }} in this location
          </div>
        </div>
      </CardTitle>
      <CardAction class="entry-header-actions">
        <Badge v-if="currentFolderReadOnly" variant="warning">
          Remote folder
        </Badge>
        <span v-if="selectedKeysModel.length > 0" class="entry-selection-count text-sm text-muted-foreground">
          {{ selectedKeysModel.length }} selected
        </span>
        <DropdownMenu v-if="selectedKeysModel.length > 0">
          <DropdownMenuTrigger as-child>
            <Button
              variant="outline" size="sm"
              class="entry-header-action"
            >
              Actions
            </Button>
          </DropdownMenuTrigger><DropdownMenuContent align="start">
            <DropdownMenuItem @select="emit('open-move-selected')">
              Move Selected
            </DropdownMenuItem>
            <DropdownMenuItem @select="emit('open-copy-selected')">
              Copy Selected
            </DropdownMenuItem>
            <DropdownMenuItem class="text-destructive" @select="emit('delete-selected')">
              Delete Selected
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
        <Button
          size="sm" class="entry-header-action"
          :disabled="currentFolderReadOnly" @click="emit('open-create')"
        >
          <Plus />
          Create Entry
        </Button>
      </CardAction>
    </CardHeader>

    <CardContent class="px-0">
      <Table class="key-values-table" style="user-select: none">
        <TableHeader><TableRow><TableHead class="w-10" /><TableHead>Key</TableHead><TableHead>Type</TableHead><TableHead>Source</TableHead><TableHead>Details</TableHead><TableHead>Updated</TableHead></TableRow></TableHeader><TableBody>
          <TableRow v-if="parentFolderPath !== null" class="cursor-pointer up-directory-row" tabindex="0" aria-label="Open parent folder" @click="navigateUp" @keydown.enter.prevent="navigateUp" @keydown.space.prevent="navigateUp">
            <TableCell /><TableCell :colspan="5">
              <div class="flex items-center gap-2">
                <Folder class="size-4" /><span aria-hidden="true">..</span>
              </div>
            </TableCell>
          </TableRow>
          <TableRow v-for="item in visibleEntries" :key="item.key" class="cursor-pointer" @click="emit('row-click', item)">
            <TableCell @click.stop>
              <Checkbox v-if="item.selectable" :model-value="selectedKeysModel.includes(item.key)" :aria-label="`Select ${item.key}`" @update:model-value="toggleSelectedKey(item.key, Boolean($event))" />
            </TableCell>
            <TableCell>
              <div class="flex items-center gap-2 flex-wrap entry-drag-handle h-full" :draggable="true" @dragstart="onEntryDragStart($event, item)" @dragend="emit('entry-drag-end')">
                <Folder v-if="item.kind === 'folder'" class="size-4" /><FileDown v-else-if="item.payloadKind === 'binary'" class="size-4" /><FileText v-else class="size-4" /><span>{{ getDisplayKey(item.key) }}</span>
              </div>
            </TableCell>
            <TableCell>
              <div v-if="item.kind !== 'folder'" class="flex items-center gap-2 flex-wrap">
                <Badge variant="secondary">
                  {{ item.entryType === 'secret' ? 'Secret' : 'Data' }}
                </Badge><Badge variant="secondary">
                  {{ item.payloadKind === 'binary' ? 'Binary' : 'Text' }}
                </Badge>
              </div><span v-else>-</span>
            </TableCell>
            <TableCell>{{ item.source === 'remote' ? 'Remote' : item.hasLocalOverride ? 'Local override' : 'Local' }}</TableCell><TableCell>{{ item.kind === 'folder' ? '-' : `${item.labels?.length ?? 0} label(s)` }}</TableCell><TableCell>{{ item.kind === 'folder' ? '-' : format(new Date(item.updatedAtUtc), 'yyyy-MM-dd HH:mm:ss') }}</TableCell>
          </TableRow>
          <TableEmpty v-if="!loadingEntries && visibleEntries.length === 0 && parentFolderPath === null" :colspan="6">
            This location is empty. Create an entry or choose another folder.
          </TableEmpty><TableEmpty v-if="loadingEntries" :colspan="6">
            Loading keys...
          </TableEmpty>
        </TableBody>
      </Table>
    </CardContent>
  </Card>

  <Dialog
    :open="editDialog"
    @update:open="!$event && emit('cancel-edit')"
  >
    <DialogContent size="4xl" scrollable>
      <DialogHeader>
        <DialogTitle class="flex items-center gap-2">
          <span>Edit entry</span>
          <span class="text-sm text-muted-foreground font-mono">{{ editorFormModel.key }}</span>
        </DialogTitle>
        <DialogDescription>Update the entry payload, storage type, and labels.</DialogDescription>
      </DialogHeader>
      <div data-slot="dialog-body" class="dialog-body-content -mx-4 overflow-x-hidden px-4">
        <div v-if="editorSource === 'remote' || editorHasLocalOverride" class="mb-4 flex flex-wrap items-center gap-2 rounded-lg border bg-muted/50 p-3">
          <Badge v-if="editorSource === 'remote'" variant="warning">
            Remote
            read-only
          </Badge>
          <Badge v-else-if="editorHasLocalOverride" variant="default">
            Local
            override
          </Badge>
          <span class="ml-auto" />
          <Button
            v-if="editingExisting && editorReadOnly && !editorHasLocalOverride" variant="secondary"
            @click="emit('create-local-override')"
          >
            Create Local Override
          </Button>
          <ButtonGroup
            v-if="editorAvailableSources.length > 1" class="ml-2"
          >
            <Button
              v-for="source in editorAvailableSources" :key="source" size="sm"
              :variant="editorSelectedSource === source ? 'default' : 'outline'"
              @click="emit('update:editor-selected-source', source)"
            >
              {{ source === 'remote' ? 'Remote master' : 'Local override' }}
            </Button>
          </ButtonGroup>
        </div>
        <Alert v-if="editorErrorMessage" class="mb-4" variant="destructive">
          {{ editorErrorMessage }}
        </Alert>

        <div class="grid grid-cols-12 gap-4">
          <div class="col-span-12 md:col-span-12">
            <Field>
              <FieldLabel>Key</FieldLabel><Input
                v-model="editorFormModel.key"
                :readonly="editorReadOnly"
              />
            </Field>
          </div>
          <div class="col-span-12 md:col-span-12">
            <div class="flex gap-4 flex-wrap items-end">
              <div>
                <div class="text-xs text-muted-foreground mb-2">
                  Entry Type
                </div>
                <ButtonGroup
                  :aria-disabled="editorReadOnly"
                >
                  <Button
                    v-for="option in entryTypeOptions" :key="option.value"
                    :variant="editorFormModel.entryType === option.value ? 'default' : 'outline'"
                    :disabled="editorReadOnly" @click="editorFormModel.entryType = option.value"
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
                  :aria-disabled="editorReadOnly"
                >
                  <Button
                    v-for="option in payloadKindOptions" :key="option.value"
                    :variant="editorFormModel.payloadKind === option.value ? 'default' : 'outline'"
                    :disabled="editorReadOnly" @click="onPayloadKindChanged(option.value)"
                  >
                    {{ option.title }}
                  </Button>
                </ButtonGroup>
              </div>

              <Field v-if="!editorReadOnly && editorFormModel.payloadKind !== 'text'">
                <FieldLabel>Binary File</FieldLabel><Input type="file" @change="emit('upload-binary', (($event.target as HTMLInputElement).files?.[0] ?? null))" />
              </Field>
            </div>
          </div>
        </div>

        <div class="mt-4 grid grid-cols-12 gap-4">
          <template v-if="editorFormModel.payloadKind === 'text'">
            <div class="col-span-12 md:col-span-12">
              <Alert
                v-if="editorFormModel.entryType === 'secret' && !editorFormModel.isRevealed"
              >
                Secret value is hidden until you press Reveal.
              </Alert>
              <MonacoEditorField
                v-else v-model="editorFormModel.value" :language="editorLanguage" :height="360"
                label="Value" :font-size="13" :read-only="editorReadOnly"
              />
            </div>
          </template>
          <template v-else>
            <div class="col-span-12 md:col-span-12" v-if="hasBinaryPayload">
              <Button variant="secondary" @click="emit('download-binary')">
                <Download />
                Download Binary
              </Button>
            </div>
          </template>

          <div class="col-span-12 md:col-span-12">
            <ConfigLabelsEditor
              :labels="editorFormModel.labels" @add="emit('add-label')"
              @remove="emit('remove-label', $event)"
            />
          </div>
        </div>
      </div>
      <DialogFooter>
        <Button
          v-if="editingExisting && !editorReadOnly"
          @click="emit('delete-entry', editorFormModel.key)" variant="destructive"
        >
          Delete
        </Button>
        <span class="ml-auto" />
        <Button
          v-if="editingExisting && showRevisionHistoryAction" variant="secondary" @click="emit('open-revisions')" :disabled="loadingRevisionHistory"
        >
          <Spinner v-if="loadingRevisionHistory" /><History />
          Revisions
        </Button>
        <Button variant="ghost" @click="emit('cancel-edit')">
          {{ editorHasChanges ? 'Cancel' : 'Close' }}
        </Button>
        <Button
          v-if="editorFormModel.entryType === 'secret' && !editorFormModel.isRevealed" @click="emit('reveal-secret')" variant="warning" :disabled="revealingSecret"
        >
          <Spinner v-if="revealingSecret" />
          Reveal
        </Button>
        <Button
          :disabled="editorReadOnly || !editorHasChanges"
          @click="emit('save-editor')"
        >
          <Spinner v-if="savingEditor" />
          Save
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>

<script setup lang="ts">
import type { ConfigEntryType, ConfigLabel, ConfigPayloadKind } from '@/composables/useConfigsApi';
import { FileDown, FileText, Folder } from '@lucide/vue';
import { format } from 'date-fns';
import { computed } from 'vue';
import MonacoEditorField from '@/components/MonacoEditorField.vue';
import ConfigLabelsEditor from './ConfigLabelsEditor.vue';

interface SelectedPathSegment {
  name: string;
  path: string;
  type: 'folder' | 'key';
}

interface EditorForm {
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
  loadingSelectedEntry: boolean;
  loadingEntries: boolean;
  isDragOverKeys: boolean;
  selectedFolderSegments: SelectedPathSegment[];
  editDialog: boolean;
  selectedTableKeys: string[];
  visibleEntries: Array<Record<string, any>>;
  currentFolderReadOnly: boolean;
  editingExisting: boolean;
  savingEditor: boolean;
  revealingSecret: boolean;
  editorErrorMessage: string;
  editorForm: EditorForm;
  editorLanguage: string;
  editorReadOnly: boolean;
  editorSource: 'local' | 'remote' | '';
  editorHasLocalOverride: boolean;
  editorHasChanges: boolean;
  showRevisionHistoryAction: boolean;
  loadingRevisionHistory: boolean;
  editorSelectedSource: 'local' | 'remote' | '';
  editorAvailableSources: Array<'local' | 'remote'>;
  getDisplayKey: (key: string) => string;
  getRowProps: (args: { item: any }) => { class: string };
}>();

const emit = defineEmits<{
  'keys-dragenter': [];
  'keys-dragleave': [event: DragEvent];
  'keys-dragover': [];
  'keys-drop': [event: DragEvent];
  'selected-path-click': [path: string];
  'open-move-selected': [];
  'open-copy-selected': [];
  'delete-selected': [];
  'open-create': [];
  'update:selectedTableKeys': [value: string[]];
  'row-click': [item: any];
  'cancel-edit': [];
  'delete-entry': [key: string];
  'create-local-override': [];
  'open-revisions': [];
  'save-editor': [];
  'reveal-secret': [];
  'download-binary': [];
  'add-label': [];
  'remove-label': [index: number];
  'upload-binary': [value: File | File[] | null];
  'payload-kind-change': [value: ConfigPayloadKind];
  'update:editorForm': [value: EditorForm];
  'update:editor-selected-source': [value: 'local' | 'remote' | ''];
  'entry-drag-start': [payload: { event: DragEvent; path: string }];
  'entry-drag-end': [];
}>();

const headers = [
  { title: 'Key', key: 'key' },
  { title: 'Type', key: 'type' },
  { title: 'Source', key: 'source' },
  { title: 'Details', key: 'details' },
  { title: 'Updated', key: 'updatedAtUtc' },
];

const entryTypeOptions = [
  { title: 'Data', value: 'data' },
  { title: 'Secret', value: 'secret' },
] satisfies Array<{ title: string; value: ConfigEntryType }>;

const payloadKindOptions = [
  { title: 'Text', value: 'text' },
  { title: 'Binary', value: 'binary' },
] satisfies Array<{ title: string; value: ConfigPayloadKind }>;

const selectedKeysModel = computed({
  get: () => props.selectedTableKeys,
  set: value => emit('update:selectedTableKeys', value),
});

function toggleSelectedKey(key: string, selected: boolean) {
  selectedKeysModel.value = selected
    ? Array.from(new Set([...selectedKeysModel.value, key]))
    : selectedKeysModel.value.filter(value => value !== key);
}

const parentFolderPath = computed<string | null>(() => {
  const folders = props.selectedFolderSegments.filter(segment => segment.type === 'folder');

  if (folders.length === 0)
    return null;

  return folders.length === 1 ? '' : folders[folders.length - 2]?.path ?? '';
});

function navigateUp() {
  if (parentFolderPath.value !== null)
    emit('selected-path-click', parentFolderPath.value);
}

const editorFormModel = computed({
  get: () => props.editorForm,
  set: value => emit('update:editorForm', value),
});

const hasBinaryPayload = computed(() => Boolean(editorFormModel.value.binaryBase64));

function onRowClick(_event: Event, row: { item: any }) {
  emit('row-click', row.item);
}

function onPayloadKindChanged(value: ConfigPayloadKind) {
  emit('payload-kind-change', value);
}

function onEntryDragStart(event: DragEvent, item: any) {
  const path = typeof item?.key === 'string' ? item.key : '';

  if (!path) {
    event.preventDefault();
    return;
  }

  if (event.dataTransfer) {
    event.dataTransfer.effectAllowed = 'copyMove';
  }

  emit('entry-drag-start', { event, path });
}
</script>

<style scoped>
.keys-drop-active {
  outline: 2px dashed color-mix(in srgb, var(--primary) calc(0.7 * 100%), transparent);
  outline-offset: -2px;
}

.key-values-location {
  padding-block: 0.75rem;
  background: var(--muted);
}

.entry-header-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.75rem;
}

.entry-selection-count {
  margin-right: 0.25rem;
}

.entry-header-action {
  min-height: 2.25rem;
}

.up-directory-row {
  cursor: pointer;
}

.key-values-table {
  font-size: 0.75rem;
  line-height: 1rem;
}

.key-values-table :deep([data-slot='table'] th),
.key-values-table :deep([data-slot='table'] td) {
  line-height: 1rem;
}

.entry-drag-handle {
  /* cursor: grab; */
}

.entry-drag-handle:active {
  /* cursor: grabbing; */
}
</style>
