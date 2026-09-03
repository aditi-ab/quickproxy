<template>
  <div class="page-container">
    <Alert v-if="errorMessage" class="mb-4" variant="destructive">
      {{ errorMessage }}
    </Alert>

    <div class="grid grid-cols-12 gap-4">
      <div class="col-span-12">
        <header class="mb-6 flex flex-wrap items-end justify-between gap-4">
          <div>
            <div class="eyebrow">
              Runtime
            </div><h1 class="page-title mt-1">
              Containers
            </h1><p class="page-lead">
              Manage containers, compose projects, images, and reusable defaults.
            </p>
          </div><div class="page-action-buttons">
            <Button v-if="activeTab === 'containers'" @click="createContainer">
              <Plus />
              New container
            </Button>
            <Button v-if="activeTab === 'containers'" @click="createComposeProject">
              <Plus />
              New project
            </Button>
            <Button v-if="activeTab === 'defaults'" @click="createDefaultsSet">
              <Plus />
              New default set
            </Button>
            <Button variant="secondary" @click="reloadCurrentTab">
              <RefreshCw />
              Refresh
            </Button>
          </div>
        </header>

        <Tabs v-model="activeTab">
          <TabsList>
            <TabsTrigger value="containers">
              Containers
            </TabsTrigger>
            <TabsTrigger value="images">
              Images
            </TabsTrigger>
            <TabsTrigger value="defaults">
              Defaults
            </TabsTrigger>
          </TabsList>

          <TabsContent class="mt-4" value="containers">
            <div class="flex flex-col gap-4">
              <ProjectsTab
                :projects="composeProjects" :busy-project-id="busyComposeProjectId"
                :busy-action="busyComposeProjectAction" @row-click="editComposeProject" @action="onComposeProjectAction"
                @logs="openComposeLogs"
              />

              <ContainersTab
                :status="status" :filtered-containers="filteredContainers"
                :selected-container-names="selectedContainerNames" :bulk-action-running="bulkActionRunning"
                :bulk-action-options="bulkActionOptions" :show-only-running-containers="showOnlyRunningContainers"
                :show-system-containers="showSystemContainers" :selected-project-filter="selectedProjectFilter"
                :project-filter-options="projectFilterOptions" :busy-container-name="busyContainerName"
                :busy-action="busyAction" :is-dragging-archive="isDraggingArchive"
                :drag-over-container-name="dragOverContainerName"
                @update:show-only-running-containers="showOnlyRunningContainers = $event"
                @update:show-system-containers="showSystemContainers = $event"
                @update:selected-project-filter="selectedProjectFilter = $event"
                @update:selected-container-names="selectedContainerNames = $event" @bulk-action="runBulkAction"
                @row-click="onContainerRowClickDirect" @run-action="onContainerRunAction" @open-shell="openShell"
                @open-logs="openLogs"
              />
            </div>
          </TabsContent>

          <TabsContent class="mt-4" value="images">
            <Card>
              <ImagesTab
                :images="images" :show-all-images="showAllImages" :pruning-images="pruningImages"
                @update:show-all-images="onShowAllImagesChanged" @prune="pruneImages"
              />
            </Card>
          </TabsContent>

          <TabsContent class="mt-4" value="defaults">
            <Card>
              <DefaultsTab :default-sets="defaultSets" @row-click="editDefaultsSet" />
            </Card>
          </TabsContent>
        </Tabs>
      </div>
    </div>

    <ContainerDialog
      v-model="showDialog" :container="dialogModel" :is-edit="dialogIsEdit"
      :initial-image-archive="pendingDialogArchive" :save-error="dialogError" :saving="dialogSaving" @save="saveContainer"
      @delete="deleteCurrentContainer"
    />
    <ContainerShellDialog v-model="showShellDialog" :container-name="shellContainerName" />
    <ContainerLogsDialog v-model="showLogsDialog" :container-name="logsContainerName" />
    <ComposeProjectLogsDialog
      v-model="showComposeLogsDialog" :project-id="composeLogsProjectId"
      :service="composeLogsService"
    />

    <ContainerDefaultsDialog
      v-model="showDefaultsDialog" :model="defaultsDialogModel" :is-edit="defaultsDialogIsEdit"
      :saving="defaultsDialogSaving" :error="defaultsDialogError" @save="saveDefaultsSet"
      @delete="deleteCurrentDefaultsSet"
    />

    <ComposeProjectDialog
      v-model="showComposeProjectDialog" :model="composeProjectDialogModel"
      :is-edit="composeProjectDialogIsEdit" :runtime="composeProjectDialogRuntime"
      :validation-result="composeProjectValidationResult" :saving="composeProjectDialogSaving"
      :validating="composeProjectDialogValidating" :error="composeProjectDialogError" @save="saveComposeProject"
      @deploy="deployComposeProject" @validate="validateComposeProject" @delete="deleteCurrentComposeProject"
      @logs="openComposeLogs"
    />

    <teleport to="body">
      <div v-if="dropOverlayStyle" class="container-drop-overlay" :style="dropOverlayStyle">
        <PackageOpen class="size-4" />
        <span class="text-sm font-medium">Drop archive to upgrade</span>
      </div>
    </teleport>
  </div>
</template>

<script lang="ts" setup>
import type { ComposeProject, ComposeProjectListItem, ComposeProjectRuntimeSnapshot, ComposeProjectValidationResult, ContainerDefaultsSet, ContainerEditRequest, ContainerHostMappingRequest, ContainerImageInventoryItem, ContainerInventoryItem, ContainerInventoryStatus, ContainerKeyValuePair, ContainerMountBindingRequest, ContainerNetworkAliasRequest, ContainerSaveRequest } from '@/composables/useContainersApi';
import { PackageOpen } from '@lucide/vue';

import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import ContainersTab from '@/components/containers/ContainersTab.vue';
import DefaultsTab from '@/components/containers/DefaultsTab.vue';
import ImagesTab from '@/components/containers/ImagesTab.vue';
import ProjectsTab from '@/components/containers/ProjectsTab.vue';
import {

  useContainersApi,

} from '@/composables/useContainersApi';
import ComposeProjectDialog from '@/dialogs/ComposeProjectDialog.vue';
import ComposeProjectLogsDialog from '@/dialogs/ComposeProjectLogsDialog.vue';
import ContainerDefaultsDialog from '@/dialogs/ContainerDefaultsDialog.vue';
import ContainerDialog from '@/dialogs/ContainerDialog.vue';
import ContainerLogsDialog from '@/dialogs/ContainerLogsDialog.vue';
import ContainerShellDialog from '@/dialogs/ContainerShellDialog.vue';

type BulkContainerAction = 'restart' | 'start' | 'stop' | 'delete' | 'repull-restart';
type ComposeProjectAction = 'deploy' | 'start' | 'stop' | 'restart' | 'pull' | 'down';

const ALL_PROJECT_FILTER_VALUE = '__all_projects__';
const NO_PROJECT_FILTER_VALUE = '__no_project__';

const api = useContainersApi();
const activeTab = ref<'containers' | 'projects' | 'images' | 'defaults'>('containers');
const containers = ref<ContainerInventoryItem[]>([]);
const composeProjects = ref<ComposeProjectListItem[]>([]);
const images = ref<ContainerImageInventoryItem[]>([]);
const defaultSets = ref<ContainerDefaultsSet[]>([]);
const status = ref<ContainerInventoryStatus | null>(null);
const errorMessage = ref('');
const busyContainerName = ref('');
const busyAction = ref('');
const showDialog = ref(false);
const dialogIsEdit = ref(false);
const dialogError = ref('');
const dialogSaving = ref(false);
const dialogModel = ref<ContainerEditRequest>(emptyContainerRequest());
const editingContainerName = ref('');
const pendingDialogArchive = ref<File | null>(null);
const showLogsDialog = ref(false);
const logsContainerName = ref('');
const showShellDialog = ref(false);
const shellContainerName = ref('');
const dragOverContainerName = ref('');
const isDraggingArchive = ref(false);
const showOnlyRunningContainers = ref(true);
const showSystemContainers = ref(false);
const selectedProjectFilter = ref(ALL_PROJECT_FILTER_VALUE);
const showAllImages = ref(false);
const pruningImages = ref(false);
const selectedContainerNames = ref<string[]>([]);
const bulkActionRunning = ref(false);
const dropOverlayBounds = ref<{ top: number; left: number; width: number; height: number } | null>(null);
const showDefaultsDialog = ref(false);
const defaultsDialogIsEdit = ref(false);
const defaultsDialogSaving = ref(false);
const defaultsDialogError = ref('');
const editingDefaultsSetId = ref('');
const defaultsDialogModel = ref<ContainerDefaultsSet>(emptyDefaultsSet());
const showComposeProjectDialog = ref(false);
const composeProjectDialogIsEdit = ref(false);
const composeProjectDialogSaving = ref(false);
const composeProjectDialogValidating = ref(false);
const composeProjectDialogError = ref('');
const composeProjectValidationResult = ref<ComposeProjectValidationResult | null>(null);
const composeProjectDialogModel = ref<ComposeProject>(emptyComposeProject());
const composeProjectDialogRuntime = ref<ComposeProjectRuntimeSnapshot | null>(null);
const editingComposeProjectId = ref('');
const showComposeLogsDialog = ref(false);
const composeLogsProjectId = ref('');
const composeLogsService = ref('');
const busyComposeProjectId = ref('');
const busyComposeProjectAction = ref('');
let refreshTimer: number | null = null;

const dropOverlayStyle = computed(() => {
  if (!dropOverlayBounds.value) {
    return null;
  }

  const overlayWidth = Math.min(Math.max(220, dropOverlayBounds.value.width * 0.5), 320);
  const overlayHeight = 36;

  return {
    top: `${dropOverlayBounds.value.top + (dropOverlayBounds.value.height - overlayHeight) / 2}px`,
    left: `${dropOverlayBounds.value.left + (dropOverlayBounds.value.width - overlayWidth) / 2}px`,
    width: `${overlayWidth}px`,
    minHeight: `${overlayHeight}px`,
  };
});

const visibleContainers = computed(() => showSystemContainers.value
  ? containers.value
  : containers.value.filter(item => !isSystemContainer(item)));
const projectFilterOptions = computed(() => {
  const projects = Array.from(new Set(
    visibleContainers.value
      .map(item => item.compose.project?.trim())
      .filter((value): value is string => Boolean(value)),
  )).sort((left, right) => left.localeCompare(right, undefined, { sensitivity: 'base' }));

  return [
    { title: 'All projects', value: ALL_PROJECT_FILTER_VALUE },
    { title: 'No project', value: NO_PROJECT_FILTER_VALUE },
    ...projects.map(project => ({ title: project, value: project })),
  ];
});
const projectScopedContainers = computed(() => {
  if (selectedProjectFilter.value === ALL_PROJECT_FILTER_VALUE) {
    return visibleContainers.value;
  }

  if (selectedProjectFilter.value === NO_PROJECT_FILTER_VALUE) {
    return visibleContainers.value.filter(item => !item.compose.project?.trim());
  }

  return visibleContainers.value.filter(item => item.compose.project === selectedProjectFilter.value);
});
const filteredContainers = computed(() => showOnlyRunningContainers.value
  ? projectScopedContainers.value.filter(item => item.state === 'running')
  : projectScopedContainers.value);
const bulkActionOptions = [
  { value: 'restart', title: 'Restart Selected', icon: 'mdi-restart' },
  { value: 'start', title: 'Start Selected', icon: 'mdi-play' },
  { value: 'stop', title: 'Stop Selected', icon: 'mdi-stop' },
  { value: 'delete', title: 'Delete Selected', icon: 'mdi-delete' },
  { value: 'repull-restart', title: 'Re-pull and Restart Selected', icon: 'mdi-refresh' },
] as const;

onMounted(async () => {
  await loadContainers();
  await loadComposeProjects(true);
  await loadImages(true);
  await loadDefaultSets(true);
  window.addEventListener('dragenter', onWindowDragEnter);
  window.addEventListener('dragover', onWindowDragOver);
  window.addEventListener('dragleave', onWindowDragLeave);
  window.addEventListener('drop', onWindowDrop);
  refreshTimer = window.setInterval(() => {
    void loadContainers(true);
    void loadComposeProjects(true);
  }, 1000);
});

onBeforeUnmount(() => {
  window.removeEventListener('dragenter', onWindowDragEnter);
  window.removeEventListener('dragover', onWindowDragOver);
  window.removeEventListener('dragleave', onWindowDragLeave);
  window.removeEventListener('drop', onWindowDrop);

  if (refreshTimer !== null) {
    window.clearInterval(refreshTimer);
    refreshTimer = null;
  }
});

watch(activeTab, async (value) => {
  if (value === 'projects' && composeProjects.value.length === 0) {
    await loadComposeProjects();
    return;
  }

  if (value === 'images' && images.value.length === 0) {
    await loadImages();
    return;
  }

  if (value === 'defaults' && defaultSets.value.length === 0) {
    await loadDefaultSets();
  }
});

watch(filteredContainers, (value) => {
  const visibleNames = new Set(value.map(item => item.name));

  selectedContainerNames.value = selectedContainerNames.value.filter(name => visibleNames.has(name));
}, { deep: true });

async function loadContainers(silent = false) {
  try {
    if (!silent) {
      errorMessage.value = '';
    }

    applySnapshot(await api.listContainers());
  }
  catch (error) {
    if (isCanceledError(error)) {
      return;
    }

    if (!silent || !status.value) {
      errorMessage.value = toErrorMessage(error);
    }
  }
}

async function loadComposeProjects(silent = false) {
  try {
    if (!silent) {
      errorMessage.value = '';
    }

    const result = await api.listComposeProjects();

    composeProjects.value = result.projects
      .slice()
      .sort((left, right) => left.project.id.localeCompare(right.project.id, undefined, { sensitivity: 'base' }));
  }
  catch (error) {
    if (isCanceledError(error)) {
      return;
    }

    if (!silent) {
      errorMessage.value = toErrorMessage(error);
    }
  }
}

async function loadImages(silent = false) {
  try {
    if (!silent) {
      errorMessage.value = '';
    }

    const result = await api.listImages(showAllImages.value);

    images.value = result.images;
  }
  catch (error) {
    if (isCanceledError(error)) {
      return;
    }

    if (!silent) {
      errorMessage.value = toErrorMessage(error);
    }
  }
}

async function reloadCurrentTab() {
  if (activeTab.value === 'projects') {
    await loadComposeProjects();
    return;
  }

  if (activeTab.value === 'images') {
    await loadImages();
    return;
  }

  if (activeTab.value === 'defaults') {
    await loadDefaultSets();
    return;
  }

  await Promise.all([
    loadContainers(),
    loadComposeProjects(true),
    loadImages(true),
  ]);
}

async function loadDefaultSets(silent = false) {
  try {
    if (!silent) {
      errorMessage.value = '';
    }

    const result = await api.listDefaultSets();

    defaultSets.value = result.sets
      .slice()
      .sort((left, right) => left.id.localeCompare(right.id, undefined, { sensitivity: 'base' }));
  }
  catch (error) {
    if (isCanceledError(error)) {
      return;
    }

    if (!silent) {
      errorMessage.value = toErrorMessage(error);
    }
  }
}

async function pruneImages() {
  try {
    errorMessage.value = '';
    pruningImages.value = true;
    await api.pruneUnusedImages();
    await loadImages(true);
  }
  catch (error) {
    errorMessage.value = (error as Error).message;
  }
  finally {
    pruningImages.value = false;
  }
}

function onShowAllImagesChanged(value: boolean) {
  showAllImages.value = value;
  void loadImages();
}

function applySnapshot(snapshot: Awaited<ReturnType<typeof api.listContainers>>) {
  containers.value = snapshot.containers;
  status.value = snapshot.status;
  errorMessage.value = '';

  const availableNames = new Set(snapshot.containers.map(item => item.name));

  selectedContainerNames.value = selectedContainerNames.value.filter(name => availableNames.has(name));
}

function onContainerRunAction(value: { name: string; action: 'start' | 'stop' | 'repull-restart' }) {
  void runAction(value.name, value.action);
}

async function runAction(name: string, action: 'start' | 'stop' | 'repull-restart') {
  try {
    errorMessage.value = '';
    await performContainerAction(name, action);
  }
  catch (error) {
    errorMessage.value = (error as Error).message;
  }
  finally {
    busyContainerName.value = '';
    busyAction.value = '';
  }
}

async function waitForContainerState(name: string, action: 'start' | 'stop' | 'repull-restart' | 'delete') {
  const deadline = Date.now() + 5000;
  const desiredState = action === 'start' || action === 'repull-restart'
    ? 'running'
    : action === 'stop'
      ? 'exited'
      : '';

  while (Date.now() < deadline) {
    await loadContainers(true);

    const current = containers.value.find(x => x.name === name);

    if (action === 'delete') {
      if (!current) {
        return;
      }
    }
    else if (action === 'stop') {
      if (!current || current.state !== 'running') {
        return;
      }
    }
    else if (current?.state === desiredState) {
      return;
    }

    await delay(250);
  }

  await loadContainers(true);
}

async function performContainerAction(name: string, action: 'start' | 'stop' | 'repull-restart' | 'delete') {
  busyContainerName.value = name;
  busyAction.value = action;

  if (action === 'start') {
    await api.startContainer(name);
  }
  else if (action === 'stop') {
    await api.stopContainer(name);
  }
  else if (action === 'delete') {
    await api.deleteContainer(name);
  }
  else {
    await api.repullAndRestartContainer(name);
  }

  await waitForContainerState(name, action);
}

async function runBulkAction(action: BulkContainerAction) {
  const names = [...selectedContainerNames.value];

  if (names.length === 0) {
    return;
  }

  try {
    errorMessage.value = '';
    bulkActionRunning.value = true;

    const failures: string[] = [];

    for (const name of names) {
      try {
        if (action === 'restart') {
          await performContainerAction(name, 'stop');
          await performContainerAction(name, 'start');
        }
        else if (action === 'delete') {
          await performContainerAction(name, 'delete');
        }
        else {
          await performContainerAction(name, action);
        }
      }
      catch (error) {
        failures.push(`${name}: ${(error as Error).message}`);
      }
      finally {
        busyContainerName.value = '';
        busyAction.value = '';
      }
    }

    await Promise.all([
      loadContainers(true),
      loadImages(true),
    ]);

    if (action === 'delete') {
      selectedContainerNames.value = [];
    }

    if (failures.length > 0) {
      errorMessage.value = failures.join('; ');
      return;
    }

    selectedContainerNames.value = [];
  }
  finally {
    bulkActionRunning.value = false;
    busyContainerName.value = '';
    busyAction.value = '';
  }
}

function delay(ms: number) {
  return new Promise(resolve => window.setTimeout(resolve, ms));
}

function createContainer() {
  dialogError.value = '';
  dialogSaving.value = false;
  dialogIsEdit.value = false;
  editingContainerName.value = '';
  pendingDialogArchive.value = null;
  dialogModel.value = emptyContainerRequest();
  showDialog.value = true;
}

async function editContainer(name: string, initialImageArchive: File | null = null) {
  try {
    dialogError.value = '';
    dialogSaving.value = false;
    dialogIsEdit.value = true;
    editingContainerName.value = name;
    pendingDialogArchive.value = initialImageArchive;
    dialogModel.value = await api.getEditableContainer(name);
    showDialog.value = true;
  }
  catch (error) {
    errorMessage.value = (error as Error).message;
  }
}

function onContainerRowClickDirect(item: ContainerInventoryItem) {
  void editContainer(item.name);
}

function openLogs(name: string) {
  logsContainerName.value = name;
  showLogsDialog.value = true;
}

function openShell(name: string) {
  shellContainerName.value = name;
  showShellDialog.value = true;
}

async function saveContainer(payload: ContainerSaveRequest) {
  try {
    dialogError.value = '';
    dialogSaving.value = true;

    const normalizedPayload = normalizeContainerSaveRequest(payload);

    if (dialogIsEdit.value) {
      await api.updateContainer(editingContainerName.value, normalizedPayload);
    }
    else {
      await api.createContainer(normalizedPayload);
    }

    showDialog.value = false;
    pendingDialogArchive.value = null;
    await Promise.all([
      loadContainers(),
      loadImages(true),
    ]);
  }
  catch (error) {
    dialogError.value = (error as Error).message;
  }
  finally {
    dialogSaving.value = false;
  }
}

async function deleteCurrentContainer() {
  if (!dialogIsEdit.value || !editingContainerName.value) {
    return;
  }

  try {
    dialogError.value = '';
    dialogSaving.value = true;
    await api.deleteContainer(editingContainerName.value);
    showDialog.value = false;
    pendingDialogArchive.value = null;
    await Promise.all([
      loadContainers(),
      loadImages(true),
    ]);
  }
  catch (error) {
    dialogError.value = (error as Error).message;
  }
  finally {
    dialogSaving.value = false;
  }
}

function createDefaultsSet() {
  defaultsDialogError.value = '';
  defaultsDialogSaving.value = false;
  defaultsDialogIsEdit.value = false;
  editingDefaultsSetId.value = '';
  defaultsDialogModel.value = emptyDefaultsSet();
  showDefaultsDialog.value = true;
}

async function editDefaultsSet(id: string) {
  try {
    defaultsDialogError.value = '';
    defaultsDialogSaving.value = false;
    defaultsDialogIsEdit.value = true;
    editingDefaultsSetId.value = id;
    defaultsDialogModel.value = cloneDefaultsSet(await api.getDefaultSet(id));
    showDefaultsDialog.value = true;
  }
  catch (error) {
    errorMessage.value = (error as Error).message;
  }
}

async function saveDefaultsSet(payload: ContainerDefaultsSet) {
  try {
    defaultsDialogError.value = '';
    defaultsDialogModel.value = payload;

    const normalizedId = normalizeDefaultsId(defaultsDialogIsEdit.value
      ? editingDefaultsSetId.value
      : payload.id);

    if (!normalizedId) {
      defaultsDialogError.value = 'Set id is required.';
      return;
    }

    defaultsDialogSaving.value = true;
    await api.upsertDefaultSet(normalizedId, {
      labels: normalizeKeyValuePairs(payload.labels),
      envVars: normalizeKeyValuePairs(payload.envVars),
      mountBindings: normalizeMountBindings(payload.mountBindings),
      hostMappings: normalizeHostMappings(payload.hostMappings),
      networkAliases: normalizeNetworkAliases(payload.networkAliases),
    });

    showDefaultsDialog.value = false;
    await loadDefaultSets();
  }
  catch (error) {
    defaultsDialogError.value = (error as Error).message;
  }
  finally {
    defaultsDialogSaving.value = false;
  }
}

async function deleteCurrentDefaultsSet() {
  if (!defaultsDialogIsEdit.value || !editingDefaultsSetId.value) {
    return;
  }

  try {
    defaultsDialogError.value = '';
    defaultsDialogSaving.value = true;
    await api.deleteDefaultSet(editingDefaultsSetId.value);
    showDefaultsDialog.value = false;
    await loadDefaultSets();
  }
  catch (error) {
    defaultsDialogError.value = (error as Error).message;
  }
  finally {
    defaultsDialogSaving.value = false;
  }
}

function createComposeProject() {
  composeProjectDialogError.value = '';
  composeProjectDialogSaving.value = false;
  composeProjectDialogValidating.value = false;
  composeProjectDialogIsEdit.value = false;
  editingComposeProjectId.value = '';
  composeProjectValidationResult.value = null;
  composeProjectDialogRuntime.value = null;
  composeProjectDialogModel.value = emptyComposeProject();
  showComposeProjectDialog.value = true;
}

async function editComposeProject(id: string) {
  try {
    composeProjectDialogError.value = '';
    composeProjectDialogSaving.value = false;
    composeProjectDialogValidating.value = false;
    composeProjectDialogIsEdit.value = true;
    editingComposeProjectId.value = id;
    composeProjectValidationResult.value = null;

    const result = await api.getComposeProject(id);

    composeProjectDialogModel.value = cloneComposeProject(result.project);
    composeProjectDialogRuntime.value = result.runtime;
    showComposeProjectDialog.value = true;
  }
  catch (error) {
    errorMessage.value = (error as Error).message;
  }
}

async function saveComposeProject(payload: ComposeProject) {
  try {
    composeProjectDialogError.value = '';
    composeProjectDialogSaving.value = true;
    composeProjectDialogModel.value = payload;

    const normalized = normalizeComposeProject(payload, composeProjectDialogIsEdit.value
      ? editingComposeProjectId.value
      : payload.id);

    const stored = await api.upsertComposeProject(normalized.id, {
      displayName: normalized.id,
      slug: normalized.id,
      status: normalized.status,
      composeYaml: normalized.composeYaml,
      managedFiles: normalized.managedFiles,
    });

    editingComposeProjectId.value = stored.id;
    composeProjectDialogIsEdit.value = true;
    composeProjectDialogModel.value = cloneComposeProject(stored);
    showComposeProjectDialog.value = false;
    await loadComposeProjects();
  }
  catch (error) {
    composeProjectDialogError.value = (error as Error).message;
  }
  finally {
    composeProjectDialogSaving.value = false;
  }
}

async function validateComposeProject(payload: ComposeProject) {
  try {
    composeProjectDialogError.value = '';
    composeProjectDialogValidating.value = true;

    const normalized = normalizeComposeProject(payload, composeProjectDialogIsEdit.value
      ? editingComposeProjectId.value
      : payload.id);

    const stored = await api.upsertComposeProject(normalized.id, {
      displayName: normalized.id,
      slug: normalized.id,
      status: normalized.status,
      composeYaml: normalized.composeYaml,
      managedFiles: normalized.managedFiles,
    });

    editingComposeProjectId.value = stored.id;
    composeProjectDialogIsEdit.value = true;
    composeProjectDialogModel.value = cloneComposeProject(stored);
    composeProjectValidationResult.value = await api.validateComposeProject(stored.id);

    const details = await api.getComposeProject(stored.id);

    composeProjectDialogRuntime.value = details.runtime;
  }
  catch (error) {
    composeProjectDialogError.value = (error as Error).message;
  }
  finally {
    composeProjectDialogValidating.value = false;
  }
}

async function deployComposeProject(payload: ComposeProject) {
  try {
    composeProjectDialogError.value = '';
    composeProjectDialogSaving.value = true;

    const normalized = normalizeComposeProject(payload, composeProjectDialogIsEdit.value
      ? editingComposeProjectId.value
      : payload.id);

    const stored = await api.upsertComposeProject(normalized.id, {
      displayName: normalized.id,
      slug: normalized.id,
      status: normalized.status,
      composeYaml: normalized.composeYaml,
      managedFiles: normalized.managedFiles,
    });

    editingComposeProjectId.value = stored.id;
    composeProjectDialogIsEdit.value = true;
    composeProjectDialogModel.value = cloneComposeProject(stored);

    const result = await api.runComposeProjectAction(stored.id, 'deploy');

    composeProjectDialogRuntime.value = result.runtime;
    await waitForComposeProjectState(stored.id, 'deploy');
    showComposeProjectDialog.value = false;
    await Promise.all([
      loadComposeProjects(),
      loadContainers(true),
    ]);
  }
  catch (error) {
    composeProjectDialogError.value = (error as Error).message;
  }
  finally {
    composeProjectDialogSaving.value = false;
  }
}

async function deleteCurrentComposeProject() {
  if (!composeProjectDialogIsEdit.value || !editingComposeProjectId.value) {
    return;
  }

  try {
    composeProjectDialogError.value = '';
    composeProjectDialogSaving.value = true;
    await api.deleteComposeProject(editingComposeProjectId.value);
    showComposeProjectDialog.value = false;
    await Promise.all([
      loadComposeProjects(),
      loadContainers(true),
    ]);
  }
  catch (error) {
    composeProjectDialogError.value = (error as Error).message;
  }
  finally {
    composeProjectDialogSaving.value = false;
  }
}

function onComposeProjectAction(value: { id: string; action: ComposeProjectAction }) {
  void runComposeProjectAction(value.id, value.action);
}

async function runComposeProjectAction(id: string, action: ComposeProjectAction) {
  try {
    errorMessage.value = '';
    busyComposeProjectId.value = id;
    busyComposeProjectAction.value = action;
    await api.runComposeProjectAction(id, action);
    await waitForComposeProjectState(id, action);
    await Promise.all([
      loadComposeProjects(true),
      loadContainers(true),
    ]);
  }
  catch (error) {
    errorMessage.value = (error as Error).message;
  }
  finally {
    busyComposeProjectId.value = '';
    busyComposeProjectAction.value = '';
  }
}

async function waitForComposeProjectState(id: string, action: ComposeProjectAction) {
  if (action === 'pull') {
    return;
  }

  const deadline = Date.now() + 30000;

  while (Date.now() < deadline) {
    await loadComposeProjects(true);

    const current = composeProjects.value.find(x => stringEquals(x.project.id, id));

    if (isComposeProjectActionComplete(current, action)) {
      return;
    }

    await delay(500);
  }

  await loadComposeProjects(true);
}

function isComposeProjectActionComplete(project: ComposeProjectListItem | undefined, action: ComposeProjectAction) {
  if (action === 'down') {
    if (!project) {
      return true;
    }

    return project.runtime.containerCount === 0 && project.runtime.status === 'stopped';
  }

  if (!project) {
    return false;
  }

  switch (action) {
    case 'deploy':
    case 'start':
    case 'restart':
      return project.runtime.containerCount > 0 && (project.runtime.status === 'running' || project.runtime.status === 'partial');
    case 'stop':
      return project.runtime.containerCount === 0 || project.runtime.status === 'stopped';
    case 'pull':
      return true;
    default:
      return false;
  }
}

function openComposeLogs(value: { id: string; service?: string }) {
  composeLogsProjectId.value = value.id;
  composeLogsService.value = value.service ?? '';
  showComposeLogsDialog.value = true;
}

function isSystemContainer(item: ContainerInventoryItem) {
  return stringEquals(item.containerLabels['quickproxy.role'], 'system');
}

function toErrorMessage(error: unknown) {
  if (error instanceof Error && error.message.trim().length > 0) {
    return error.message;
  }

  return 'An unexpected error occurred.';
}

function isCanceledError(error: unknown) {
  if (!(error instanceof Error)) {
    return false;
  }

  const name = error.name.toLowerCase();
  const message = error.message.toLowerCase();

  return name === 'aborterror'
    || name === 'cancelederror'
    || name === 'operationcanceledexception'
    || message.includes('the operation was canceled')
    || message.includes('the operation was cancelled')
    || message.includes('operation canceled')
    || message.includes('operation cancelled')
    || message.includes('signal is aborted')
    || message.includes('request was aborted');
}

function stringEquals(left?: string | null, right?: string | null) {
  return (left ?? '').localeCompare(right ?? '', undefined, { sensitivity: 'accent' }) === 0;
}

function onWindowDragEnter(event: DragEvent) {
  if (activeTab.value !== 'containers') {
    return;
  }

  if (!hasArchiveFile(event.dataTransfer)) {
    return;
  }

  event.preventDefault();
  isDraggingArchive.value = true;
  updateDropTarget(event);
}

function onWindowDragOver(event: DragEvent) {
  if (activeTab.value !== 'containers') {
    return;
  }

  if (!hasArchiveFile(event.dataTransfer)) {
    return;
  }

  event.preventDefault();
  isDraggingArchive.value = true;
  updateDropTarget(event);
}

function onWindowDragLeave(event: DragEvent) {
  if (activeTab.value !== 'containers') {
    return;
  }

  const relatedTarget = event.relatedTarget as Node | null;

  if (relatedTarget) {
    return;
  }

  clearDropTarget();
}

function onWindowDrop(event: DragEvent) {
  if (activeTab.value !== 'containers') {
    return;
  }

  event.preventDefault();

  const targetName = dragOverContainerName.value;

  clearDropTarget();

  const file = event.dataTransfer?.files?.[0] ?? null;

  if (!file) {
    return;
  }

  if (!isSupportedArchiveFile(file)) {
    errorMessage.value = 'Only `.tar`, `.tar.gz`, and `.tgz` image archives can be dropped onto a container row.';
    return;
  }

  if (!targetName) {
    errorMessage.value = 'Drop the archive on a specific container row.';
    return;
  }

  void editContainer(targetName, file);
}

function updateDropTarget(event: DragEvent) {
  const row = findContainerRowFromEvent(event) ?? findContainerRowAtPoint(event.clientX, event.clientY);

  if (!row) {
    clearDropTarget();
    return;
  }

  const name = row.dataset.containerName ?? '';

  if (!name) {
    clearDropTarget();
    return;
  }

  dragOverContainerName.value = name;

  const rowRect = row.getBoundingClientRect();

  dropOverlayBounds.value = {
    top: rowRect.top,
    left: rowRect.left,
    width: rowRect.width,
    height: rowRect.height,
  };
}

function findContainerRowFromEvent(event: DragEvent) {
  const target = event.target as HTMLElement | null;

  if (!target) {
    return null;
  }

  return target.closest('tr[data-container-name]') as HTMLElement | null;
}

function findContainerRowAtPoint(clientX: number, clientY: number) {
  const element = document.elementFromPoint(clientX, clientY) as HTMLElement | null;

  if (!element) {
    return null;
  }

  return element.closest('tr[data-container-name]') as HTMLElement | null;
}

function clearDropTarget() {
  isDraggingArchive.value = false;
  dragOverContainerName.value = '';
  dropOverlayBounds.value = null;
}

function hasArchiveFile(dataTransfer?: DataTransfer | null) {
  if (!dataTransfer) {
    return false;
  }

  const types = Array.from(dataTransfer.types ?? []);

  if (types.includes('Files')) {
    return true;
  }

  const file = dataTransfer.files?.[0] ?? null;

  return !!file && isSupportedArchiveFile(file);
}

function isSupportedArchiveFile(file: File) {
  const fileName = file.name.toLowerCase();

  return fileName.endsWith('.tar') || fileName.endsWith('.tar.gz') || fileName.endsWith('.tgz');
}

function normalizeDefaultsId(value: string) {
  return value.trim();
}

function normalizeKeyValuePairs(values: ContainerKeyValuePair[]) {
  const seen = new Set<string>();
  const result: ContainerKeyValuePair[] = [];

  for (const value of values) {
    const key = value.key.trim();

    if (!key) {
      continue;
    }

    const dedupeKey = key.toLowerCase();

    if (seen.has(dedupeKey)) {
      continue;
    }

    seen.add(dedupeKey);
    result.push({
      key,
      value: value.value ?? '',
    });
  }

  return result;
}

function normalizeMountBindings(values: ContainerMountBindingRequest[]) {
  const seen = new Set<string>();
  const result: ContainerMountBindingRequest[] = [];

  for (const value of values) {
    const hostPath = value.hostPath.trim();
    const containerPath = value.containerPath.trim();

    if (!hostPath || !containerPath) {
      continue;
    }

    const dedupeKey = containerPath.toLowerCase();

    if (seen.has(dedupeKey)) {
      continue;
    }

    seen.add(dedupeKey);
    result.push({
      hostPath,
      containerPath,
      readOnly: !!value.readOnly,
    });
  }

  return result;
}

function normalizeNetworkAliases(values: ContainerNetworkAliasRequest[]) {
  const seen = new Set<string>();
  const result: ContainerNetworkAliasRequest[] = [];

  for (const value of values) {
    const network = value.network.trim();
    const alias = value.alias.trim();

    if (!network || !alias) {
      continue;
    }

    const dedupeKey = `${network.toLowerCase()}\u001F${alias.toLowerCase()}`;

    if (seen.has(dedupeKey)) {
      continue;
    }

    seen.add(dedupeKey);
    result.push({
      network,
      alias,
    });
  }

  return result;
}

function normalizeHostMappings(values: ContainerHostMappingRequest[]) {
  const seen = new Set<string>();
  const result: ContainerHostMappingRequest[] = [];

  for (const value of values) {
    const hostname = value.hostname.trim();
    const address = value.address.trim();

    if (!hostname || !address) {
      continue;
    }

    const dedupeKey = hostname.toLowerCase();

    if (seen.has(dedupeKey)) {
      continue;
    }

    seen.add(dedupeKey);
    result.push({
      hostname,
      address,
    });
  }

  return result;
}

function cloneDefaultsSet(value: ContainerDefaultsSet): ContainerDefaultsSet {
  return {
    id: value.id,
    labels: (value.labels ?? []).map(x => ({ key: x.key, value: x.value })),
    envVars: (value.envVars ?? []).map(x => ({ key: x.key, value: x.value })),
    mountBindings: (value.mountBindings ?? []).map(x => ({
      hostPath: x.hostPath,
      containerPath: x.containerPath,
      readOnly: x.readOnly,
    })),
    hostMappings: (value.hostMappings ?? []).map(x => ({
      hostname: x.hostname,
      address: x.address,
    })),
    networkAliases: (value.networkAliases ?? []).map(x => ({
      network: x.network,
      alias: x.alias,
    })),
    updatedAtUtc: value.updatedAtUtc,
  };
}

function cloneComposeProject(value: ComposeProject): ComposeProject {
  return {
    ...value,
    managedFiles: (value.managedFiles ?? []).map(file => ({
      path: file.path,
      content: file.content,
    })),
  };
}

function emptyDefaultsSet(): ContainerDefaultsSet {
  return {
    id: '',
    labels: [],
    envVars: [],
    mountBindings: [],
    hostMappings: [],
    networkAliases: [],
    updatedAtUtc: new Date().toISOString(),
  };
}

function emptyComposeProject(): ComposeProject {
  return {
    id: '',
    displayName: '',
    slug: '',
    status: 'draft',
    composeYaml: `services:\n  app:\n    image: nginx:alpine\n    ports:\n      - "8080:80"\n`,
    workspacePath: '',
    managedFiles: [],
    createdAtUtc: new Date().toISOString(),
    updatedAtUtc: new Date().toISOString(),
    lastDeployAtUtc: null,
    lastError: null,
  };
}

function emptyContainerRequest(): ContainerEditRequest {
  return {
    name: '',
    image: '',
    labels: [],
    envVars: [],
    mountBindings: [],
    hostMappings: [],
    networkAliases: [],
    restartPolicy: 'no',
    publishedPorts: [],
  };
}

function normalizeContainerSaveRequest(payload: ContainerSaveRequest): ContainerSaveRequest {
  return {
    imageArchive: payload.imageArchive,
    request: {
      ...payload.request,
      labels: normalizeKeyValuePairs(payload.request.labels),
      envVars: normalizeKeyValuePairs(payload.request.envVars),
      mountBindings: normalizeMountBindings(payload.request.mountBindings),
      hostMappings: normalizeHostMappings(payload.request.hostMappings),
      networkAliases: normalizeNetworkAliases(payload.request.networkAliases),
    },
  };
}

function normalizeComposeProject(payload: ComposeProject, idOverride: string) {
  const id = toKebabCase((idOverride || payload.id).trim());

  return {
    id,
    displayName: id,
    slug: id,
    status: payload.status || 'draft',
    composeYaml: payload.composeYaml.replace(/\r\n/g, '\n').trim(),
    managedFiles: (payload.managedFiles ?? [])
      .map(file => ({
        path: file.path.trim().replace(/\\/g, '/'),
        content: file.content ?? '',
      }))
      .filter(file => !!file.path),
  };
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

<style scoped>
.container-drop-overlay {
  position: fixed;
  z-index: 9999;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  padding: 6px 12px;
  border: 1px dashed var(--primary);
  border-radius: 8px;
  background-color: rgba(15, 23, 42, 0.92);
  box-shadow: 0 8px 18px rgba(0, 0, 0, 0.22);
  color: var(--primary);
  backdrop-filter: blur(6px);
  pointer-events: none;
}
</style>
