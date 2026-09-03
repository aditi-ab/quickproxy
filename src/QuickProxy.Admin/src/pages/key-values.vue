<template>
  <div class="page-container">
    <header class="mb-6 flex flex-wrap items-end justify-between gap-4">
      <div>
        <div class="eyebrow">
          Configuration
        </div><h1 class="page-title mt-1">
          Key/Values
        </h1><p class="page-lead">
          Browse and manage hierarchical application configuration.
        </p>
      </div><div class="page-action-buttons">
        <Button
          variant="secondary" :disabled="toolbarBusy"
          @click="openImportRemoteDialog"
        >
          <CloudDownload />
          Import from remote
        </Button>
        <Button
          variant="secondary"
          :disabled="toolbarBusy" @click="backupLocalConfigs"
        >
          <Spinner v-if="backingUpConfigs" /><ArchiveRestore />
          Backup
        </Button>
        <Button
          variant="secondary" :disabled="toolbarBusy"
          @click="openRestoreDialog"
        >
          <Upload />
          Restore
        </Button>
      </div>
    </header>
    <div v-if="actionErrorMessage || actionSuccessMessage" class="mb-4">
      <Alert v-if="actionErrorMessage" variant="destructive">
        <CircleAlert /><AlertDescription>{{ actionErrorMessage }}</AlertDescription>
      </Alert>
      <Alert v-else class="border-emerald-500/40 text-emerald-700 dark:text-emerald-300">
        <CircleCheck /><AlertDescription>{{ actionSuccessMessage }}</AlertDescription>
      </Alert>
    </div>

    <Alert v-if="errorMessage" class="mb-4" variant="destructive">
      <CircleAlert /><AlertDescription>{{ errorMessage }}</AlertDescription>
    </Alert>

    <Alert class="mb-4">
      <Info /><AlertDescription>
        Open a folder to browse its entries. Use Parent folder at the top of the list to go up one level.
        Open an entry to view or edit it.
      </AlertDescription>
    </Alert>

    <KeyValuesEntriesCard
      :loading-selected-entry="loadingSelectedEntry" :loading-entries="loadingEntries"
      :is-drag-over-keys="isDragOverKeys" :selected-folder-segments="selectedFolderSegments" :edit-dialog="editDialog"
      :selected-table-keys="selectedTableKeys" :visible-entries="visibleEntries"
      :current-folder-read-only="currentFolderReadOnly" :editing-existing="editingExisting"
      :saving-editor="savingEditor" :revealing-secret="revealingSecret" :editor-error-message="editorErrorMessage"
      :editor-form="editorForm" :editor-language="editorLanguage" :editor-read-only="editorReadOnly"
      :editor-source="editorSource" :editor-has-local-override="editorHasLocalOverride"
      :editor-has-changes="editorHasChanges"
      :show-revision-history-action="showRevisionHistoryAction" :loading-revision-history="loadingRevisionHistory"
      :editor-selected-source="editorSelectedSource" :editor-available-sources="editorAvailableSources"
      :get-display-key="getDisplayKey" :get-row-props="getRowProps"
      @keys-dragenter="isDragOverKeys = true" @keys-dragleave="onKeysDragLeave" @keys-dragover="isDragOverKeys = true"
      @keys-drop="onKeysDrop" @selected-path-click="onSelectedPathSegmentClick"
      @open-move-selected="openMoveSelectedDialog" @open-copy-selected="openCopySelectedDialog"
      @delete-selected="deleteSelected" @open-create="openCreate"
      @update:selected-table-keys="selectedTableKeys = $event" @row-click="handleVisibleEntryRowClick"
      @cancel-edit="editDialog = false" @delete-entry="remove" @create-local-override="createLocalOverride"
      @open-revisions="openRevisionHistory"
      @save-editor="saveEditor" @reveal-secret="revealEditorSecret" @download-binary="downloadEditorBinary"
      @add-label="editorForm.labels.push({ key: '', value: '' })"
      @remove-label="editorForm.labels.splice($event, 1)" @upload-binary="onEditorBinaryUpload"
      @payload-kind-change="onEditorPayloadKindChange" @entry-drag-start="onConfigDragStart($event.event, $event.path)"
      @entry-drag-end="onConfigDragEnd"
      @update:editor-form="editorForm = $event" @update:editor-selected-source="setEditorSelectedSource"
    />

    <CreateKeyValueDialog
      v-model="createDialog" :form="createForm" :editor-language="createEditorLanguage"
      :saving="savingCreate" :error-message="createErrorMessage" @update:form="createForm = $event" @save="saveCreate"
      @upload-binary="onCreateBinaryUpload" @add-label="createForm.labels.push({ key: '', value: '' })"
      @remove-label="createForm.labels.splice($event, 1)" @payload-kind-change="onCreatePayloadKindChange"
    />

    <RenameFolderDialog
      v-model="renameFolderDialog" :form="renameFolderForm" :saving="renamingFolder"
      :error-message="renameFolderErrorMessage" @update:form="renameFolderForm = $event" @save="saveRenameFolder"
    />

    <MoveSelectedKeysDialog
      v-model="moveSelectedDialog" :form="moveSelectedForm"
      :selected-count="selectedActionPaths.length" :selected-paths="selectedActionPaths"
      :saving="movingSelected" :error-message="moveSelectedErrorMessage"
      :title="moveDialogTitle" :confirm-label="moveDialogConfirmLabel" @update:form="moveSelectedForm = $event"
      @save="saveMoveSelected"
    />

    <Dialog v-model:open="deleteConfirmDialog">
      <DialogContent size="4xl" scrollable>
        <DialogHeader>
          <DialogTitle>Delete Entry{{ pendingDeletePaths.length === 1 ? '' : 'ies' }}</DialogTitle>
          <DialogDescription class="sr-only">
            Confirm permanent deletion of the selected configuration entries.
          </DialogDescription>
        </DialogHeader>
        <CardContent class="dialog-body-content">
          <Alert v-if="deleteConfirmErrorMessage" class="mb-4" variant="destructive">
            <CircleAlert /><AlertDescription>{{ deleteConfirmErrorMessage }}</AlertDescription>
          </Alert>
          <div class="text-sm mb-3">
            Are you sure you want to delete {{ deleteConfirmSummary }}?
          </div>
          <div class="text-sm text-muted-foreground">
            This cannot be undone.
          </div>
        </CardContent>
        <Separator />
        <DialogFooter>
          <span class="ml-auto" />
          <Button variant="ghost" :disabled="deletingConfirmed" @click="deleteConfirmDialog = false">
            Cancel
          </Button>
          <Button @click="confirmDelete" variant="destructive" :disabled="deletingConfirmed">
            <Spinner v-if="deletingConfirmed" />
            Delete
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="importRemoteDialog">
      <DialogContent size="4xl">
        <DialogHeader>
          <DialogTitle>Import from remote</DialogTitle>
          <DialogDescription>Replace the local Key/Values dataset with data from another QuickProxy server.</DialogDescription>
        </DialogHeader>
        <div class="dialog-body-content">
          <Alert v-if="importRemoteErrorMessage" class="mb-4" variant="destructive">
            <CircleAlert /><AlertDescription>{{ importRemoteErrorMessage }}</AlertDescription>
          </Alert>
          <Field>
            <FieldLabel for="import-remote-url">
              QuickProxy URL
            </FieldLabel>
            <Input id="import-remote-url" v-model="importRemoteUrl" />
            <FieldDescription>Example: https://quickproxy.example.com</FieldDescription>
          </Field>
          <Alert class="mt-4 mb-4 border-amber-500/40 text-amber-700 dark:text-amber-300">
            <TriangleAlert /><AlertDescription>
              This replaces the current server's local key/value store with the remote server's dataset.
            </AlertDescription>
          </Alert>
          <Field orientation="horizontal" class="items-center">
            <Checkbox id="import-remote-confirmed" v-model="importRemoteConfirmed" />
            <FieldLabel for="import-remote-confirmed">
              I understand this will replace all local Key/Values on this server.
            </FieldLabel>
          </Field>
        </div>
        <DialogFooter>
          <span class="ml-auto" />
          <Button variant="ghost" :disabled="importingRemoteConfigs" @click="importRemoteDialog = false">
            Cancel
          </Button>
          <Button
            :disabled="!importRemoteConfirmed"
            @click="submitImportRemote"
          >
            <Spinner v-if="importingRemoteConfigs" />
            Import
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="restoreDialog">
      <DialogContent size="4xl">
        <DialogHeader>
          <DialogTitle>Restore backup</DialogTitle>
          <DialogDescription>Replace the local Key/Values dataset with the contents of a backup file.</DialogDescription>
        </DialogHeader>
        <div class="dialog-body-content">
          <Alert v-if="restoreErrorMessage" class="mb-4" variant="destructive">
            <CircleAlert /><AlertDescription>{{ restoreErrorMessage }}</AlertDescription>
          </Alert>
          <Field><FieldLabel>Backup file</FieldLabel><Input type="file" accept=".json,application/json" @change="restoreFile = (($event.target as HTMLInputElement).files?.[0] ?? null)" /></Field>
          <Alert class="mt-4 mb-4 border-amber-500/40 text-amber-700 dark:text-amber-300">
            <TriangleAlert /><AlertDescription>
              Restoring replaces the current server's local key/value store with the contents of the selected backup.
            </AlertDescription>
          </Alert>
          <Field orientation="horizontal" class="items-center">
            <Checkbox id="restore-confirmed" v-model="restoreConfirmed" />
            <FieldLabel for="restore-confirmed">
              I understand this will replace all local Key/Values on this server.
            </FieldLabel>
          </Field>
        </div>
        <DialogFooter>
          <span class="ml-auto" />
          <Button variant="ghost" :disabled="restoringConfigs" @click="restoreDialog = false">
            Cancel
          </Button>
          <Button :disabled="!restoreConfirmed" @click="submitRestore">
            <Spinner v-if="restoringConfigs" />
            Restore
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="dropFilesDialog">
      <DialogContent size="4xl" scrollable>
        <DialogHeader>
          <DialogTitle>Import Dropped Files</DialogTitle>
          <DialogDescription class="sr-only">
            Choose how the dropped files should be stored as configuration entries.
          </DialogDescription>
        </DialogHeader>
        <CardContent class="dialog-body-content">
          <Alert v-if="dropFilesErrorMessage" class="mb-4" variant="destructive">
            <CircleAlert /><AlertDescription>{{ dropFilesErrorMessage }}</AlertDescription>
          </Alert>
          <div class="text-sm mb-4">
            Store {{ pendingDropFiles.length === 1 ? 'this file' : `${pendingDropFiles.length} files` }} as text or binary?
          </div>
          <RadioGroup v-model="dropFilesPayloadKind">
            <div class="flex items-center gap-2">
              <RadioGroupItem id="drop-files-text" value="text" />
              <FieldLabel for="drop-files-text">
                Text
              </FieldLabel>
            </div>
            <div class="flex items-center gap-2">
              <RadioGroupItem id="drop-files-binary" value="binary" />
              <FieldLabel for="drop-files-binary">
                Binary
              </FieldLabel>
            </div>
          </RadioGroup>
        </CardContent>
        <Separator />
        <DialogFooter>
          <span class="ml-auto" />
          <Button variant="ghost" :disabled="importingDroppedFiles" @click="closeDropFilesDialog">
            Cancel
          </Button>
          <Button @click="submitDroppedFiles" :disabled="importingDroppedFiles">
            <Spinner v-if="importingDroppedFiles" />
            Import
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <ConfigRevisionHistoryDialog
      v-model="revisionHistoryDialog"
      :entry-key="editorForm.key"
      :revisions="revisionSummaries"
      :selected-revision-id="selectedRevisionId"
      :details="selectedRevisionDetails"
      :error-message="revisionHistoryErrorMessage"
      :loading-revision-history="loadingRevisionHistory"
      :restoring-revision="restoringRevision"
      :revealing-revision="revealingRevision"
      :editor-language="revisionEditorLanguage"
      @update:selected-revision-id="onSelectedRevisionChanged"
      @restore-revision="restoreSelectedRevision"
      @reveal-revision="revealSelectedRevision"
      @download-binary="downloadSelectedRevisionBinary"
    />

    <Alert v-if="saveSnackbarVisible" class="fixed bottom-4 right-4 z-50 w-auto min-w-72 border-emerald-500/40 text-emerald-700 shadow-lg dark:text-emerald-300">
      <CircleCheck /><AlertDescription>{{ saveSnackbarMessage }}</AlertDescription>
    </Alert>
  </div>
</template>

<script setup lang="ts">
import type { ConfigBackupDocument, ConfigEntry, ConfigEntryDetails, ConfigEntryRevisionDetails, ConfigEntryRevisionSummary, ConfigEntryVariant, ConfigLabel, ConfigTreeNode } from '@/composables/useConfigsApi';
import { CircleAlert, CircleCheck, Info, TriangleAlert } from '@lucide/vue';
import { computed, onMounted, onUnmounted, ref } from 'vue';
import KeyValuesEntriesCard from '@/components/key-values/KeyValuesEntriesCard.vue';
import {

  useConfigsApi,
} from '@/composables/useConfigsApi';
import ConfigRevisionHistoryDialog from '@/dialogs/key-values/ConfigRevisionHistoryDialog.vue';
import CreateKeyValueDialog from '@/dialogs/key-values/CreateKeyValueDialog.vue';
import MoveSelectedKeysDialog from '@/dialogs/key-values/MoveSelectedKeysDialog.vue';
import RenameFolderDialog from '@/dialogs/key-values/RenameFolderDialog.vue';

type TreeViewNode = Omit<ConfigTreeNode, 'children'> & { children?: TreeViewNode[] };
type VisibleEntry = ConfigEntry & { kind: 'folder' | 'key'; selectable: boolean };
interface SelectedPathSegment { name: string; path: string; type: 'folder' | 'key' }
interface EntryForm {
  key: string;
  value: string;
  binaryBase64: string;
  mediaType: string;
  entryType: 'data' | 'secret';
  payloadKind: 'text' | 'binary';
  labels: ConfigLabel[];
  isRevealed: boolean;
}

const ROOT_NODE_VALUE = '__root__';
const api = useConfigsApi();

const entries = ref<ConfigEntry[]>([]);
const tree = ref<ConfigTreeNode[]>([]);
const errorMessage = ref('');
const editorErrorMessage = ref('');
const selectedPrefix = ref('');
const selectedNodePath = ref('');
const selectedNodeKind = ref<'folder' | 'key' | ''>('');
const loadingSelectedEntry = ref(false);
const loadingFolders = ref(false);
const loadingEntries = ref(false);
const openedFolders = ref<string[]>([]);
const activatedNodes = ref<string[]>([ROOT_NODE_VALUE]);
const editDialog = ref(false);
const editingExisting = ref(false);
const originalKey = ref('');
const savingEditor = ref(false);
const revealingSecret = ref(false);
const createDialog = ref(false);
const savingCreate = ref(false);
const createErrorMessage = ref('');
const isDragOverKeys = ref(false);
const renameFolderDialog = ref(false);
const renamingFolder = ref(false);
const renameFolderErrorMessage = ref('');
const selectedTableKeys = ref<string[]>([]);
const moveSelectedDialog = ref(false);
const movingSelected = ref(false);
const moveSelectedErrorMessage = ref('');
const selectedActionPaths = ref<string[]>([]);
const moveDialogMode = ref<'move' | 'copy'>('move');
const moveDialogPreserveSourceNames = ref(false);
const deleteConfirmDialog = ref(false);
const deletingConfirmed = ref(false);
const deleteConfirmErrorMessage = ref('');
const pendingDeletePaths = ref<string[]>([]);
const editorReadOnly = ref(false);
const editorSource = ref<'local' | 'remote' | ''>('');
const editorHasLocalOverride = ref(false);
const editorSelectedSource = ref<'local' | 'remote' | ''>('');
const editorDetails = ref<ConfigEntryDetails | null>(null);
const editorLocalVariant = ref<ConfigEntryVariant | null>(null);
const editorRemoteVariant = ref<ConfigEntryVariant | null>(null);
const editorInitialSnapshot = ref('');
const actionErrorMessage = ref('');
const actionSuccessMessage = ref('');
const saveSnackbarVisible = ref(false);
const saveSnackbarMessage = ref('');
const backingUpConfigs = ref(false);
const importRemoteDialog = ref(false);
const importRemoteUrl = ref('');
const importRemoteConfirmed = ref(false);
const importingRemoteConfigs = ref(false);
const importRemoteErrorMessage = ref('');
const restoreDialog = ref(false);
const restoreFile = ref<File | File[] | null>(null);
const restoreConfirmed = ref(false);
const restoringConfigs = ref(false);
const restoreErrorMessage = ref('');
const dropFilesDialog = ref(false);
const dropFilesPayloadKind = ref<'text' | 'binary'>('binary');
const pendingDropFiles = ref<File[]>([]);
const importingDroppedFiles = ref(false);
const dropFilesErrorMessage = ref('');
const revisionHistoryDialog = ref(false);
const loadingRevisionHistory = ref(false);
const restoringRevision = ref(false);
const revealingRevision = ref(false);
const revisionHistoryErrorMessage = ref('');
const revisionSummaries = ref<ConfigEntryRevisionSummary[]>([]);
const selectedRevisionId = ref('');
const selectedRevisionDetails = ref<ConfigEntryRevisionDetails | null>(null);
const draggedConfigPaths = ref<string[]>([]);

const editorForm = ref<EntryForm>(emptyEntry());
const createForm = ref<EntryForm>(emptyEntry());
const renameFolderForm = ref({ from: '', to: '' });
const moveSelectedForm = ref({ targetFolder: '' });

const editorLanguage = computed(() => detectLanguage(editorForm.value.key, editorForm.value.value));
const createEditorLanguage = computed(() => detectLanguage(createForm.value.key, createForm.value.value));
const editorAvailableSources = computed<Array<'local' | 'remote'>>(() => {
  const sources: Array<'local' | 'remote'> = [];

  if (editorDetails.value?.remote)
    sources.push('remote');

  if (editorDetails.value?.local)
    sources.push('local');

  return sources;
});
const showRevisionHistoryAction = computed(() => editingExisting.value && Boolean(editorDetails.value?.local));
const revisionEditorLanguage = computed(() => detectLanguage(
  selectedRevisionDetails.value?.key ?? editorForm.value.key,
  selectedRevisionDetails.value?.snapshot.value ?? '',
));
const currentFolderReadOnly = computed(() => {
  if (!selectedPrefix.value) {
    return !tree.value.some(node => !node.readOnly);
  }

  return findNodeByPath(tree.value, ensureFolderPath(selectedPrefix.value))?.readOnly ?? false;
});
const toolbarBusy = computed(() => backingUpConfigs.value || importingRemoteConfigs.value || restoringConfigs.value);
const moveDialogTitle = computed(() => moveDialogMode.value === 'copy' ? 'Copy Selected Keys' : 'Move Selected Keys');
const moveDialogConfirmLabel = computed(() => moveDialogMode.value === 'copy' ? 'Copy' : 'Move');
const editorHasChanges = computed(() =>
  !editorReadOnly.value
  && editorInitialSnapshot.value.length > 0
  && createEntrySnapshot(editorForm.value) !== editorInitialSnapshot.value);
const deleteConfirmSummary = computed(() => {
  if (pendingDeletePaths.value.length === 0) {
    return 'the selected entries';
  }

  if (pendingDeletePaths.value.length === 1) {
    const path = pendingDeletePaths.value[0];

    return `'${
      path?.endsWith('/')
        ? normalizeKey(path) || 'root'
        : normalizeKey(path ?? '')
    }'`;
  }

  return `${pendingDeletePaths.value.length} selected entries`;
});
const selectedFolderSegments = computed<SelectedPathSegment[]>(() => {
  const folderParts = normalizeKey(selectedPrefix.value).split('/').filter(Boolean);
  const segments: SelectedPathSegment[] = folderParts.map((name, index) => ({
    name,
    path: `${folderParts.slice(0, index + 1).join('/')}/`,
    type: 'folder',
  }));

  if (selectedNodeKind.value === 'key') {
    const keyParts = normalizeKey(selectedNodePath.value).split('/').filter(Boolean);
    const keyName = keyParts[keyParts.length - 1];

    if (keyName) {
      segments.push({ name: keyName, path: normalizeKey(selectedNodePath.value), type: 'key' });
    }
  }

  return segments;
});
const visibleEntries = computed<VisibleEntry[]>(() => {
  const folderNode = selectedPrefix.value ? findNodeByPath(tree.value, ensureFolderPath(selectedPrefix.value)) : null;
  const folderNodes = (selectedPrefix.value ? folderNode?.children ?? [] : tree.value)
    .filter(node => node.type === 'folder')
    .map(node => ({
      key: node.path,
      value: '',
      binaryBase64: null,
      mediaType: null,
      encryptedValue: null,
      encryptedBinaryBase64: null,
      encryptedLabels: null,
      entryType: node.entryType,
      payloadKind: node.payloadKind,
      labels: [],
      updatedAtUtc: new Date(0).toISOString(),
      updatedBy: null,
      source: node.source,
      readOnly: node.readOnly,
      hasLocalOverride: node.hasLocalOverride,
      kind: 'folder' as const,
      selectable: !node.readOnly,
    }));

  const keyEntries = entries.value
    .filter(entry => isDirectChild(entry.key, selectedPrefix.value))
    .filter(entry => !entryHasChildren(entry.key))
    .map(entry => ({
      ...entry,
      kind: 'key' as const,
      selectable: !entry.readOnly,
    }));

  return [...folderNodes, ...keyEntries].sort((a, b) => {
    if (a.kind !== b.kind) {
      return a.kind === 'folder' ? -1 : 1;
    }

    return a.key.localeCompare(b.key);
  });
});
const treeItems = computed<TreeViewNode[]>(() => [
  {
    name: 'All configuration',
    path: ROOT_NODE_VALUE,
    type: 'folder',
    source: 'local',
    readOnly: false,
    hasLocalOverride: false,
    entryType: 'data',
    payloadKind: 'text',
    children: normalizeTreeForView(tree.value),
  },
]);

onMounted(async () => {
  window.addEventListener('keydown', onEditorKeyDown);
  await refreshAll();
});

onUnmounted(() => {
  window.removeEventListener('keydown', onEditorKeyDown);
});

async function refreshAll() {
  clearActionMessages();
  await Promise.all([loadTree(), loadEntries()]);
}

async function loadTree() {
  loadingFolders.value = true;

  try {
    tree.value = await api.getTree();
    syncTreeStateForCurrentSelection();
  }
  catch (error) {
    errorMessage.value = toMessage(error);
  }
  finally {
    loadingFolders.value = false;
  }
}

async function loadEntries(prefix = selectedPrefix.value) {
  const loadingDelay = window.setTimeout(() => {
    loadingEntries.value = true;
  }, 180);

  try {
    entries.value = await api.listConfigs(prefix || undefined);
    return true;
  }
  catch (error) {
    errorMessage.value = toMessage(error);
    return false;
  }
  finally {
    window.clearTimeout(loadingDelay);
    loadingEntries.value = false;
  }
}

async function onActivatedNodesChanged(paths: string[]) {
  if (paths.length === 0) {
    return;
  }

  activatedNodes.value = paths;

  const path = paths[0] ?? ROOT_NODE_VALUE;

  if (path === ROOT_NODE_VALUE) {
    await openFolder('');
    return;
  }

  const node = findNodeByPath(tree.value, path);

  if (!node) {
    return;
  }

  if (node.type === 'folder') {
    await openFolder(node.path);
    return;
  }

  await openEntry(node.path);
}

async function onSelectedPathSegmentClick(path: string) {
  if (!path) {
    await openFolder('');
    return;
  }

  if (path.endsWith('/')) {
    await openFolder(path);
    return;
  }

  await openEntry(path);
}

async function openFolder(path: string) {
  const nextPrefix = ensureFolderPath(path);

  if (!await loadEntries(nextPrefix))
    return;

  selectedPrefix.value = nextPrefix;
  selectedNodePath.value = nextPrefix;
  selectedNodeKind.value = 'folder';
  syncTreeStateForPath(nextPrefix || ROOT_NODE_VALUE, 'folder');
  editDialog.value = false;
  selectedTableKeys.value = [];
}

async function openEntry(path: string) {
  loadingSelectedEntry.value = true;
  editorErrorMessage.value = '';

  try {
    const entry = await api.getConfig(path);

    editorDetails.value = entry;
    editorLocalVariant.value = entry.local ?? null;
    editorRemoteVariant.value = entry.remote ?? null;
    originalKey.value = entry.key;
    editingExisting.value = true;
    editDialog.value = true;
    selectedNodePath.value = path;
    selectedNodeKind.value = 'key';
    selectedPrefix.value = ensureFolderPath(path.substring(0, path.lastIndexOf('/')));
    syncTreeStateForPath(path, 'key');
    editorHasLocalOverride.value = entry.hasLocalOverride;
    editorSource.value = entry.source;

    const preferredSource = entry.source === 'local' && entry.local ? 'local' : entry.remote ? 'remote' : 'local';

    setEditorSelectedSource(preferredSource);
  }
  catch (error) {
    errorMessage.value = toMessage(error);
  }
  finally {
    loadingSelectedEntry.value = false;
  }
}

function setEditorSelectedSource(source: 'local' | 'remote' | '') {
  editorSelectedSource.value = source;

  const variant = source === 'remote' ? editorRemoteVariant.value : editorLocalVariant.value;

  if (!variant) {
    return;
  }

  editorSource.value = source;
  editorReadOnly.value = source === 'remote';
  applyVariantToForm(variant);
}

function syncTreeStateForPath(path: string, kind: 'folder' | 'key') {
  const normalizedPath = kind === 'folder'
    ? ensureFolderPath(path)
    : normalizeKey(path);

  activatedNodes.value = [normalizedPath || ROOT_NODE_VALUE];

  const folderPaths = [ROOT_NODE_VALUE, ...getAncestorFolderPaths(normalizedPath, kind)];

  openedFolders.value = Array.from(new Set([...openedFolders.value, ...folderPaths]));
}

function syncTreeStateForCurrentSelection() {
  if (selectedNodeKind.value === 'key' && selectedNodePath.value) {
    syncTreeStateForPath(selectedNodePath.value, 'key');
    return;
  }

  syncTreeStateForPath(selectedPrefix.value || ROOT_NODE_VALUE, 'folder');
}

function getAncestorFolderPaths(path: string, kind: 'folder' | 'key'): string[] {
  const normalized = normalizeKey(path);

  if (!normalized) {
    return [];
  }

  const parts = normalized.split('/').filter(Boolean);
  const folderCount = kind === 'folder' ? parts.length : Math.max(0, parts.length - 1);
  const folderPaths: string[] = [];

  for (let index = 1; index <= folderCount; index += 1) {
    folderPaths.push(`${parts.slice(0, index).join('/')}/`);
  }

  return folderPaths;
}

function applyVariantToForm(variant: ConfigEntryVariant, keyOverride?: string) {
  editorForm.value = {
    key: keyOverride ?? originalKey.value,
    value: variant.value ?? '',
    binaryBase64: variant.binaryBase64 ?? '',
    mediaType: variant.mediaType ?? defaultMediaType(variant.payloadKind),
    entryType: variant.entryType,
    payloadKind: variant.payloadKind,
    labels: cloneLabels(variant.labels),
    isRevealed: variant.isRevealed === true || variant.entryType !== 'secret',
  };
  editorInitialSnapshot.value = createEntrySnapshot(editorForm.value);
}

function openCreate() {
  createDialog.value = true;
  createErrorMessage.value = '';
  createForm.value = emptyEntry({
    key: buildSuggestedKey(),
  });
}

async function saveCreate() {
  createErrorMessage.value = '';
  savingCreate.value = true;

  try {
    const key = normalizeKey(createForm.value.key);

    if (!key) {
      createErrorMessage.value = 'Key is required.';
      return;
    }

    await api.upsertConfig(key, toUpsertPayload(createForm.value));
    createDialog.value = false;
    await refreshAll();
    await openEntry(key);
  }
  catch (error) {
    createErrorMessage.value = toMessage(error);
  }
  finally {
    savingCreate.value = false;
  }
}

async function saveEditor() {
  editorErrorMessage.value = '';

  if (!editorHasChanges.value) {
    return;
  }

  savingEditor.value = true;

  try {
    const key = normalizeKey(editorForm.value.key);

    if (!key) {
      editorErrorMessage.value = 'Key is required.';
      return;
    }

    if (editorForm.value.entryType === 'secret' && !editorForm.value.isRevealed) {
      editorErrorMessage.value = 'Reveal the secret before saving so the server receives plaintext.';
      return;
    }

    if (editingExisting.value && normalizeKey(originalKey.value) !== key) {
      await api.renameKey(originalKey.value, key);
    }

    await api.upsertConfig(key, toUpsertPayload(editorForm.value));
    await refreshAll();
    selectedNodePath.value = key;
    selectedNodeKind.value = 'key';
    selectedPrefix.value = ensureFolderPath(key.substring(0, key.lastIndexOf('/')));
    await openEntry(key);
    showSaveSnackbar('Entry saved.');
  }
  catch (error) {
    editorErrorMessage.value = toMessage(error);
  }
  finally {
    savingEditor.value = false;
  }
}

async function revealEditorSecret() {
  if (editorForm.value.entryType !== 'secret') {
    return;
  }

  revealingSecret.value = true;
  editorErrorMessage.value = '';

  try {
    const revealKey = editingExisting.value ? originalKey.value : editorForm.value.key;
    const currentEditedKey = normalizeKey(editorForm.value.key) || originalKey.value;

    if (!revealKey) {
      return;
    }

    const revealed = await api.revealConfig(revealKey, editorSelectedSource.value || undefined);

    if (editorSelectedSource.value === 'remote') {
      editorRemoteVariant.value = revealed;
    }
    else {
      editorLocalVariant.value = revealed;
    }

    applyVariantToForm(revealed, currentEditedKey);
  }
  catch (error) {
    editorErrorMessage.value = toMessage(error);
  }
  finally {
    revealingSecret.value = false;
  }
}

async function openRevisionHistory() {
  if (!editorDetails.value?.local) {
    return;
  }

  revisionHistoryDialog.value = true;
  revisionHistoryErrorMessage.value = '';
  loadingRevisionHistory.value = true;
  revisionSummaries.value = [];
  selectedRevisionId.value = '';
  selectedRevisionDetails.value = null;

  try {
    revisionSummaries.value = await api.listConfigRevisions(editorDetails.value.key);
    selectedRevisionId.value = revisionSummaries.value[0]?.revisionId ?? '';

    if (selectedRevisionId.value) {
      await loadSelectedRevisionDetails(false);
    }
  }
  catch (error) {
    revisionHistoryErrorMessage.value = toMessage(error);
  }
  finally {
    loadingRevisionHistory.value = false;
  }
}

async function onSelectedRevisionChanged(revisionId: string) {
  selectedRevisionId.value = revisionId;
  await loadSelectedRevisionDetails(false);
}

async function loadSelectedRevisionDetails(reveal: boolean) {
  if (!editorDetails.value?.key || !selectedRevisionId.value) {
    selectedRevisionDetails.value = null;
    return;
  }

  selectedRevisionDetails.value = await api.getConfigRevision(editorDetails.value.key, selectedRevisionId.value, reveal);
}

async function revealSelectedRevision() {
  if (!selectedRevisionId.value) {
    return;
  }

  revealingRevision.value = true;
  revisionHistoryErrorMessage.value = '';

  try {
    await loadSelectedRevisionDetails(true);
  }
  catch (error) {
    revisionHistoryErrorMessage.value = toMessage(error);
  }
  finally {
    revealingRevision.value = false;
  }
}

async function restoreSelectedRevision() {
  if (!editorDetails.value?.key || !selectedRevisionId.value) {
    return;
  }

  restoringRevision.value = true;
  revisionHistoryErrorMessage.value = '';

  try {
    await api.restoreConfigRevision(editorDetails.value.key, selectedRevisionId.value);
    revisionHistoryDialog.value = false;
    await refreshAll();
    await openEntry(editorDetails.value.key);
  }
  catch (error) {
    revisionHistoryErrorMessage.value = toMessage(error);
  }
  finally {
    restoringRevision.value = false;
  }
}

async function createLocalOverride() {
  if (!editorForm.value.key) {
    return;
  }

  try {
    await api.createLocalOverride(editorForm.value.key);
    await refreshAll();
    await openEntry(editorForm.value.key);
  }
  catch (error) {
    editorErrorMessage.value = toMessage(error);
  }
}

async function remove(key: string) {
  openDeleteConfirmation([key]);
}

async function confirmDelete() {
  deletingConfirmed.value = true;
  deleteConfirmErrorMessage.value = '';

  try {
    const keysToDelete = buildDeleteTargets(pendingDeletePaths.value);

    for (const key of keysToDelete) {
      await api.deleteConfig(key);
    }

    if (pendingDeletePaths.value.some(path => normalizeSelectionPath(path) === normalizeSelectionPath(selectedNodePath.value))) {
      editDialog.value = false;
    }

    deleteConfirmDialog.value = false;
    pendingDeletePaths.value = [];
    selectedTableKeys.value = [];
    await refreshAll();
  }
  catch (error) {
    const message = toMessage(error);

    deleteConfirmErrorMessage.value = message;
  }
  finally {
    deletingConfirmed.value = false;
  }
}

async function deleteSelected() {
  openDeleteConfirmation(selectedTableKeys.value);
}

async function deletePath(path: string) {
  openDeleteConfirmation([path]);
}

function openRenameFolder(path: string) {
  const normalized = ensureFolderPath(path);

  renameFolderForm.value = { from: normalized, to: normalized.replace(/\/$/, '') };
  renameFolderErrorMessage.value = '';
  renameFolderDialog.value = true;
}

async function saveRenameFolder() {
  renamingFolder.value = true;
  renameFolderErrorMessage.value = '';

  try {
    await api.renameFolder(renameFolderForm.value.from, renameFolderForm.value.to);
    renameFolderDialog.value = false;
    await refreshAll();
    await openFolder(renameFolderForm.value.to);
  }
  catch (error) {
    renameFolderErrorMessage.value = toMessage(error);
  }
  finally {
    renamingFolder.value = false;
  }
}

function openMoveSelectedDialog() {
  moveDialogMode.value = 'move';
  moveDialogPreserveSourceNames.value = true;
  selectedActionPaths.value = normalizeSelectedPaths(selectedTableKeys.value);
  moveSelectedForm.value.targetFolder = selectedPrefix.value.replace(/\/$/, '');
  moveSelectedErrorMessage.value = '';
  moveSelectedDialog.value = true;
}

function openCopySelectedDialog() {
  moveDialogMode.value = 'copy';
  moveDialogPreserveSourceNames.value = true;
  selectedActionPaths.value = normalizeSelectedPaths(selectedTableKeys.value);
  moveSelectedForm.value.targetFolder = selectedPrefix.value.replace(/\/$/, '');
  moveSelectedErrorMessage.value = '';
  moveSelectedDialog.value = true;
}

function openMovePathDialog(path: string) {
  moveDialogMode.value = 'move';
  moveDialogPreserveSourceNames.value = false;
  selectedActionPaths.value = normalizeSelectedPaths([path]);
  moveSelectedForm.value.targetFolder = normalizeSelectionPath(path).replace(/\/$/, '');
  moveSelectedErrorMessage.value = '';
  moveSelectedDialog.value = true;
}

function openCopyPathDialog(path: string) {
  moveDialogMode.value = 'copy';
  moveDialogPreserveSourceNames.value = false;
  selectedActionPaths.value = normalizeSelectedPaths([path]);
  moveSelectedForm.value.targetFolder = normalizeSelectionPath(path).replace(/\/$/, '');
  moveSelectedErrorMessage.value = '';
  moveSelectedDialog.value = true;
}

async function saveMoveSelected() {
  movingSelected.value = true;
  moveSelectedErrorMessage.value = '';

  try {
    const targetFolder = normalizeKey(moveSelectedForm.value.targetFolder) || undefined;

    if (moveDialogMode.value === 'copy') {
      await api.copyConfigs(selectedActionPaths.value, targetFolder, moveDialogPreserveSourceNames.value);
    }
    else {
      await api.moveConfigs(selectedActionPaths.value, targetFolder, moveDialogPreserveSourceNames.value);
    }

    moveSelectedDialog.value = false;
    selectedTableKeys.value = [];
    selectedActionPaths.value = [];
    await refreshAll();
  }
  catch (error) {
    moveSelectedErrorMessage.value = toMessage(error);
  }
  finally {
    movingSelected.value = false;
  }
}

async function handleVisibleEntryRowClick(item: VisibleEntry) {
  if (item.kind === 'folder') {
    await openFolder(item.key);
    activatedNodes.value = [item.key];
    return;
  }

  await openEntry(item.key);
  activatedNodes.value = [item.key];
}

async function backupLocalConfigs() {
  backingUpConfigs.value = true;
  clearActionMessages();

  try {
    const backupDocument = await api.exportLocalConfigs();
    const blob = new Blob([JSON.stringify(backupDocument, null, 2)], { type: 'application/json' });

    downloadBlob(blob, `quickproxy-key-values-backup-${formatDateForFile(backupDocument.exportedAtUtc)}.json`);
    actionSuccessMessage.value = 'Backup downloaded.';
  }
  catch (error) {
    actionErrorMessage.value = toMessage(error);
  }
  finally {
    backingUpConfigs.value = false;
  }
}

function openImportRemoteDialog() {
  clearActionMessages();
  importRemoteDialog.value = true;
  importRemoteConfirmed.value = false;
  importRemoteErrorMessage.value = '';
}

async function submitImportRemote() {
  importingRemoteConfigs.value = true;
  clearActionMessages();

  try {
    await api.importFromRemote(importRemoteUrl.value.trim());
    importRemoteDialog.value = false;
    await refreshAll();
    actionSuccessMessage.value = 'Remote import completed.';
  }
  catch (error) {
    importRemoteErrorMessage.value = toMessage(error);
    actionErrorMessage.value = toMessage(error);
  }
  finally {
    importingRemoteConfigs.value = false;
  }
}

function openRestoreDialog() {
  clearActionMessages();
  restoreDialog.value = true;
  restoreConfirmed.value = false;
  restoreErrorMessage.value = '';
}

async function submitRestore() {
  restoringConfigs.value = true;
  clearActionMessages();

  try {
    const file = getSingleFile(restoreFile.value);

    if (!file) {
      restoreErrorMessage.value = 'Select a backup file.';
      return;
    }

    const raw = await file.text();
    const document = JSON.parse(raw) as ConfigBackupDocument;

    await api.restoreLocalConfigs(document);
    restoreDialog.value = false;
    await refreshAll();
    actionSuccessMessage.value = 'Backup restored.';
  }
  catch (error) {
    restoreErrorMessage.value = toMessage(error);
    actionErrorMessage.value = toMessage(error);
  }
  finally {
    restoringConfigs.value = false;
  }
}

async function onKeysDrop(event: DragEvent) {
  isDragOverKeys.value = false;

  if (hasConfigDragData(event)) {
    await handleConfigDrop(selectedPrefix.value.replace(/\/$/, ''), event);
    return;
  }

  const files = event.dataTransfer?.files;

  if (!files || files.length === 0) {
    return;
  }

  pendingDropFiles.value = Array.from(files);
  dropFilesPayloadKind.value = inferDroppedFilesPayloadKind(pendingDropFiles.value);
  dropFilesErrorMessage.value = '';
  dropFilesDialog.value = true;
}

function onKeysDragLeave(event: DragEvent) {
  const target = event.currentTarget as Node | null;
  const related = event.relatedTarget as Node | null;

  if (target && related && target.contains(related)) {
    return;
  }

  isDragOverKeys.value = false;
}

function onConfigDragStart(event: DragEvent, path: string) {
  const selection = selectedTableKeys.value.includes(path)
    ? selectedTableKeys.value
    : [path];
  const paths = normalizeSelectedPaths(selection);

  draggedConfigPaths.value = paths;

  if (event.dataTransfer) {
    event.dataTransfer.setData('application/x-quickproxy-config-paths', JSON.stringify(paths));
    event.dataTransfer.setData('text/plain', paths.join('\n'));
  }
}

function onConfigDragEnd() {
  draggedConfigPaths.value = [];
}

async function onTreeDrop(payload: { event: DragEvent; targetPath: string }) {
  await handleConfigDrop(payload.targetPath, payload.event);
}

async function handleConfigDrop(targetPath: string, event: DragEvent) {
  const draggedPaths = getDraggedConfigPaths(event);

  if (draggedPaths.length === 0) {
    return;
  }

  const normalizedTarget = normalizeKey(targetPath);

  if (!canDropConfigPaths(draggedPaths, normalizedTarget)) {
    return;
  }

  clearActionMessages();

  try {
    const preserveSourceNames = true;

    if (event.ctrlKey) {
      await api.copyConfigs(draggedPaths, normalizedTarget || undefined, preserveSourceNames);
      actionSuccessMessage.value = `Copied ${draggedPaths.length === 1 ? 'entry' : 'entries'}.`;
    }
    else {
      await api.moveConfigs(draggedPaths, normalizedTarget || undefined, preserveSourceNames);
      actionSuccessMessage.value = `Moved ${draggedPaths.length === 1 ? 'entry' : 'entries'}.`;
    }

    draggedConfigPaths.value = [];
    selectedTableKeys.value = [];
    await refreshAll();
  }
  catch (error) {
    actionErrorMessage.value = toMessage(error);
  }
}

async function onCreateBinaryUpload(value: File | File[] | null) {
  const file = getSingleFile(value);

  if (!file) {
    return;
  }

  createForm.value.payloadKind = 'binary';
  createForm.value.binaryBase64 = arrayBufferToBase64(await file.arrayBuffer());
  createForm.value.value = '';
  createForm.value.mediaType = file.type || 'application/octet-stream';
  createForm.value.key = replaceKeyLeafName(createForm.value.key, file.name, selectedPrefix.value);
}

async function onEditorBinaryUpload(value: File | File[] | null) {
  const file = getSingleFile(value);

  if (!file) {
    return;
  }

  editorForm.value.payloadKind = 'binary';
  editorForm.value.binaryBase64 = arrayBufferToBase64(await file.arrayBuffer());
  editorForm.value.value = '';
  editorForm.value.mediaType = file.type || 'application/octet-stream';
  editorForm.value.key = replaceKeyLeafName(editorForm.value.key, file.name, selectedPrefix.value);

  if (editorForm.value.entryType === 'secret') {
    editorForm.value.isRevealed = true;
  }
}

function onCreatePayloadKindChange(value: 'text' | 'binary') {
  createForm.value = convertEntryFormPayloadKind(createForm.value, value);
}

function onEditorPayloadKindChange(value: 'text' | 'binary') {
  editorForm.value = convertEntryFormPayloadKind(editorForm.value, value);
}

function closeDropFilesDialog() {
  dropFilesDialog.value = false;
  pendingDropFiles.value = [];
  dropFilesErrorMessage.value = '';
}

async function submitDroppedFiles() {
  importingDroppedFiles.value = true;
  dropFilesErrorMessage.value = '';
  errorMessage.value = '';

  try {
    for (const file of pendingDropFiles.value) {
      const key = buildFileDropKey(file.name);
      const payload = dropFilesPayloadKind.value === 'text'
        ? {
            value: await file.text(),
            binaryBase64: null,
            mediaType: file.type || 'text/plain',
          }
        : {
            value: '',
            binaryBase64: arrayBufferToBase64(await file.arrayBuffer()),
            mediaType: file.type || 'application/octet-stream',
          };

      await api.upsertConfig(key, {
        entryType: 'data',
        payloadKind: dropFilesPayloadKind.value,
        value: payload.value,
        binaryBase64: payload.binaryBase64,
        mediaType: payload.mediaType,
        labels: [],
      });
    }

    closeDropFilesDialog();
    await refreshAll();
  }
  catch (error) {
    dropFilesErrorMessage.value = toMessage(error);
    errorMessage.value = toMessage(error);
  }
  finally {
    importingDroppedFiles.value = false;
  }
}

function downloadEditorBinary() {
  const payload = editorForm.value.binaryBase64;

  if (!payload) {
    return;
  }

  const bytes = Uint8Array.from(atob(payload), char => char.charCodeAt(0));

  downloadBlob(
    new Blob([bytes], { type: editorForm.value.mediaType || 'application/octet-stream' }),
    getBinaryDownloadName(editorForm.value.key),
  );
}

function downloadSelectedRevisionBinary() {
  const payload = selectedRevisionDetails.value?.snapshot.binaryBase64;

  if (!payload) {
    return;
  }

  const bytes = Uint8Array.from(atob(payload), char => char.charCodeAt(0));

  downloadBlob(
    new Blob([bytes], { type: selectedRevisionDetails.value?.snapshot.mediaType || 'application/octet-stream' }),
    getBinaryDownloadName(selectedRevisionDetails.value?.key ?? editorForm.value.key),
  );
}

function getDisplayKey(key: string) {
  const display = !selectedPrefix.value || !key.startsWith(selectedPrefix.value)
    ? key
    : key.slice(selectedPrefix.value.length) || key;

  return display.endsWith('/') ? display.slice(0, -1) : display;
}

function getRowProps({ item }: { item: VisibleEntry }) {
  return {
    class: normalizeKey(item.key) === normalizeKey(selectedNodePath.value) ? 'config-row-selected' : '',
  };
}

function normalizeTreeForView(nodes: ConfigTreeNode[]): TreeViewNode[] {
  return nodes
    .filter(node => node.type === 'folder')
    .map((node) => {
      const children = normalizeTreeForView(node.children);
      const { children: _ignoredChildren, ...folderNode } = node;

      return children.length > 0
        ? { ...folderNode, children }
        : { ...folderNode };
    });
}

function findNodeByPath(nodes: ConfigTreeNode[], path: string): ConfigTreeNode | null {
  for (const node of nodes) {
    if (node.path === path) {
      return node;
    }

    const found = findNodeByPath(node.children, path);

    if (found) {
      return found;
    }
  }

  return null;
}

function isDirectChild(key: string, prefix: string) {
  if (!prefix) {
    return !normalizeKey(key).includes('/');
  }

  if (!key.startsWith(prefix)) {
    return false;
  }

  return !key.slice(prefix.length).includes('/');
}

function entryHasChildren(key: string) {
  const normalizedKey = normalizeKey(key);

  if (!normalizedKey) {
    return false;
  }

  const childPrefix = `${normalizedKey}/`;

  return entries.value.some(entry => normalizeKey(entry.key).startsWith(childPrefix));
}

async function expandSelectedPaths(paths: string[]) {
  const normalizedSelections = normalizeSelectedPaths(paths);
  const expanded = new Set<string>();

  for (const path of normalizedSelections) {
    if (path.endsWith('/')) {
      const entriesInFolder = await api.listConfigs(path);

      for (const entry of entriesInFolder) {
        expanded.add(normalizeKey(entry.key));
      }

      continue;
    }

    expanded.add(normalizeKey(path));
  }

  return Array.from(expanded).filter(Boolean);
}

function normalizeSelectedPaths(paths: string[]) {
  const normalized = paths
    .map(path => normalizeSelectionPath(path))
    .filter(Boolean)
    .sort((a, b) => a.localeCompare(b));

  return normalized.filter((path, index) => {
    const parentFolder = normalized.find((candidate, candidateIndex) =>
      candidateIndex !== index
      && candidate.endsWith('/')
      && path.startsWith(candidate));

    return !parentFolder;
  });
}

function normalizeSelectionPath(path: string) {
  return path.endsWith('/') ? ensureFolderPath(path) : normalizeKey(path);
}

function getDraggedConfigPaths(event: DragEvent) {
  const payload = event.dataTransfer?.getData('application/x-quickproxy-config-paths');

  if (!payload) {
    return normalizeSelectedPaths(draggedConfigPaths.value);
  }

  try {
    const parsed = JSON.parse(payload) as string[];

    return normalizeSelectedPaths(Array.isArray(parsed) ? parsed : []);
  }
  catch {
    return normalizeSelectedPaths(draggedConfigPaths.value);
  }
}

function hasConfigDragData(event: DragEvent) {
  const types = Array.from(event.dataTransfer?.types ?? []);

  return types.includes('application/x-quickproxy-config-paths') || draggedConfigPaths.value.length > 0;
}

function canDropConfigPaths(paths: string[], targetFolder: string) {
  const normalizedTarget = normalizeKey(targetFolder);

  return paths.every((path) => {
    const normalizedPath = normalizeSelectionPath(path);

    if (!normalizedPath) {
      return false;
    }

    if (normalizedPath.endsWith('/')) {
      const folderPath = normalizeKey(normalizedPath);

      return normalizedTarget !== folderPath
        && !normalizedTarget.startsWith(`${folderPath}/`);
    }

    return normalizedTarget !== normalizeKey(normalizedPath);
  });
}

function buildDeleteTargets(paths: string[]) {
  return normalizeSelectedPaths(paths)
    .map(path => path.endsWith('/') ? normalizeKey(path) : path)
    .filter(Boolean);
}

function openDeleteConfirmation(paths: string[]) {
  pendingDeletePaths.value = normalizeSelectedPaths(paths);

  if (pendingDeletePaths.value.length === 0) {
    return;
  }

  deleteConfirmErrorMessage.value = '';
  deleteConfirmDialog.value = true;
}

function ensureFolderPath(path: string) {
  const normalized = normalizeKey(path);

  return normalized ? `${normalized}/` : '';
}

function normalizeKey(value: string) {
  return (value ?? '').trim().replace(/^\/+|\/+$/g, '').replace(/\/{2,}/g, '/');
}

function buildSuggestedKey() {
  const base = selectedPrefix.value || '';
  let candidate = `${base}new-entry`.replace(/^\/+/, '');
  let index = 2;
  const existing = new Set(entries.value.map(entry => normalizeKey(entry.key)));

  while (existing.has(normalizeKey(candidate))) {
    candidate = `${base}new-entry-${index}`;
    index += 1;
  }

  return candidate;
}

function buildFileDropKey(fileName: string) {
  const clean = fileName.trim().replace(/[/\\]/g, '-');

  return `${selectedPrefix.value}${clean}`.replace(/^\/+/, '');
}

function replaceKeyLeafName(currentKey: string, fileName: string, fallbackPrefix = '') {
  const cleanFileName = fileName.trim().replace(/[/\\]/g, '-');

  if (!cleanFileName) {
    return normalizeKey(currentKey);
  }

  const normalizedCurrentKey = normalizeKey(currentKey);
  const normalizedFallbackPrefix = ensureFolderPath(fallbackPrefix);

  if (!normalizedCurrentKey) {
    return `${normalizedFallbackPrefix}${cleanFileName}`.replace(/^\/+/, '');
  }

  const slashIndex = normalizedCurrentKey.lastIndexOf('/');

  if (slashIndex < 0) {
    return cleanFileName;
  }

  return `${normalizedCurrentKey.slice(0, slashIndex + 1)}${cleanFileName}`;
}

function getBinaryDownloadName(key: string) {
  const normalized = normalizeKey(key);

  if (!normalized) {
    return 'config-binary';
  }

  const parts = normalized.split('/');

  return parts[parts.length - 1] || 'config-binary';
}

function toUpsertPayload(form: EntryForm) {
  return {
    entryType: form.entryType,
    payloadKind: form.payloadKind,
    value: form.payloadKind === 'text' ? form.value : '',
    binaryBase64: form.payloadKind === 'binary' ? form.binaryBase64 : null,
    mediaType: form.mediaType || defaultMediaType(form.payloadKind),
    labels: normalizeLabels(form.labels),
  };
}

function normalizeLabels(labels: ConfigLabel[]) {
  return labels
    .map(label => ({ key: label.key.trim(), value: label.value ?? '' }))
    .filter(label => label.key.length > 0);
}

function cloneLabels(labels: ConfigLabel[]) {
  return (labels ?? []).map(label => ({ key: label.key, value: label.value }));
}

function emptyEntry(overrides: Partial<EntryForm> = {}): EntryForm {
  return {
    key: '',
    value: '',
    binaryBase64: '',
    mediaType: 'text/plain',
    entryType: 'data',
    payloadKind: 'text',
    labels: [],
    isRevealed: true,
    ...overrides,
  };
}

function defaultMediaType(kind: 'text' | 'binary') {
  return kind === 'binary' ? 'application/octet-stream' : 'text/plain';
}

function convertEntryFormPayloadKind(form: EntryForm, nextKind: 'text' | 'binary'): EntryForm {
  if (form.payloadKind === nextKind) {
    return { ...form };
  }

  if (nextKind === 'text') {
    return {
      ...form,
      payloadKind: 'text',
      value: form.binaryBase64 ? base64ToUtf8(form.binaryBase64) : form.value,
      binaryBase64: '',
      mediaType: defaultMediaType('text'),
    };
  }

  return {
    ...form,
    payloadKind: 'binary',
    value: '',
    binaryBase64: form.value ? utf8ToBase64(form.value) : form.binaryBase64,
    mediaType: defaultMediaType('binary'),
  };
}

function inferDroppedFilesPayloadKind(files: File[]) {
  if (files.length === 0) {
    return 'binary' as const;
  }

  return files.every(file => isProbablyTextFile(file))
    ? 'text'
    : 'binary';
}

function isProbablyTextFile(file: File) {
  const type = file.type.toLowerCase();

  if (!type) {
    return /\.(?:txt|json|ya?ml|xml|csv|log|ini|conf|config|md|html?|css|js|ts|env)$/i.test(file.name);
  }

  return type.startsWith('text/')
    || type.includes('json')
    || type.includes('xml')
    || type.includes('yaml');
}

function detectLanguage(key: string, value: string) {
  const lower = key.toLowerCase();

  if (lower.endsWith('.json') || looksLikeJson(value))
    return 'json';

  if (lower.endsWith('.yaml') || lower.endsWith('.yml') || looksLikeYaml(value))
    return 'yaml';

  if (lower.endsWith('.xml') || looksLikeXml(value))
    return 'xml';

  return 'plaintext';
}

function looksLikeJson(value: string) {
  const trimmed = value.trim();

  return trimmed.startsWith('{') || trimmed.startsWith('[');
}

function looksLikeYaml(value: string) {
  return value.split(/\r?\n/).some(line => /^[\w.-]+\s*:\s*(?:\S.*|[\t\v\f \xA0\u1680\u2000-\u200A\u202F\u205F\u3000\uFEFF])$/.test(line.trim()));
}

function looksLikeXml(value: string) {
  const trimmed = value.trim();

  if (!trimmed.startsWith('<')) {
    return false;
  }

  return /^<\?xml[\s\S]*$/.test(trimmed) || /^<\/?[A-Z_][\w:.-]*(?:\s|>|\/>)[\s\S]*$/i.test(trimmed);
}

function getSingleFile(value: File | File[] | null) {
  return Array.isArray(value) ? value[0] ?? null : value;
}

function arrayBufferToBase64(buffer: ArrayBuffer) {
  let binary = '';
  const bytes = new Uint8Array(buffer);

  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }

  return btoa(binary);
}

function utf8ToBase64(value: string) {
  return arrayBufferToBase64(new TextEncoder().encode(value).buffer);
}

function base64ToUtf8(value: string) {
  if (!value) {
    return '';
  }

  const bytes = Uint8Array.from(atob(value), char => char.charCodeAt(0));

  return new TextDecoder().decode(bytes);
}

function downloadBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');

  anchor.href = url;
  anchor.download = fileName;
  anchor.click();
  URL.revokeObjectURL(url);
}

function formatDateForFile(value: string) {
  const date = new Date(value);
  const year = date.getFullYear();
  const month = `${date.getMonth() + 1}`.padStart(2, '0');
  const day = `${date.getDate()}`.padStart(2, '0');
  const hours = `${date.getHours()}`.padStart(2, '0');
  const minutes = `${date.getMinutes()}`.padStart(2, '0');
  const seconds = `${date.getSeconds()}`.padStart(2, '0');

  return `${year}${month}${day}-${hours}${minutes}${seconds}`;
}

function clearActionMessages() {
  actionErrorMessage.value = '';
  actionSuccessMessage.value = '';
}

function showSaveSnackbar(message: string) {
  saveSnackbarMessage.value = message;
  saveSnackbarVisible.value = false;
  saveSnackbarVisible.value = true;
}

function onEditorKeyDown(event: KeyboardEvent) {
  if (!editDialog.value || editorReadOnly.value) {
    return;
  }

  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 's') {
    event.preventDefault();
    void saveEditor();
  }
}

function createEntrySnapshot(form: EntryForm) {
  return JSON.stringify({
    key: normalizeKey(form.key),
    value: form.payloadKind === 'text' ? form.value : '',
    binaryBase64: form.payloadKind === 'binary' ? form.binaryBase64 : '',
    mediaType: form.mediaType || defaultMediaType(form.payloadKind),
    entryType: form.entryType,
    payloadKind: form.payloadKind,
    labels: normalizeLabels(form.labels),
  });
}

function toMessage(error: unknown) {
  return error instanceof Error ? error.message : String(error);
}
</script>

<style scoped>
.config-row-selected {
  background: color-mix(in srgb, var(--primary) calc(0.08 * 100%), transparent);
}
</style>
