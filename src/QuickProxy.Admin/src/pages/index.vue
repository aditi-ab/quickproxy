<template>
  <div class="page-container">
    <Alert v-if="errorMessage" class="mb-4" variant="destructive">
      {{ errorMessage }}
    </Alert>
    <Alert v-if="updateMessage" class="mb-4">
      {{ updateMessage }}
    </Alert>

    <Dialog v-model:open="showUpdateDialog">
      <DialogContent size="4xl" scrollable>
        <DialogHeader>
          <DialogTitle>Updating QuickProxy</DialogTitle>
          <DialogDescription class="sr-only">
            Track the progress of the current QuickProxy update.
          </DialogDescription>
        </DialogHeader>
        <CardContent class="dialog-body-content">
          <div class="text-sm">
            {{ updateDialogMessage }}
          </div>
          <div class="text-xs text-muted-foreground mt-2">
            This page will automatically reload when QuickProxy is reachable again.
          </div>
          <Progress :model-value="50" class="mt-4 animate-pulse" />
        </CardContent>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="showUpdateConfirmDialog">
      <DialogContent size="4xl" scrollable>
        <DialogHeader>
          <DialogTitle>Update QuickProxy</DialogTitle>
          <DialogDescription class="sr-only">
            Choose the image used to update and restart this QuickProxy container.
          </DialogDescription>
        </DialogHeader>
        <CardContent class="dialog-body-content">
          <div class="text-sm">
            Pull selected image and restart this QuickProxy container now?
          </div>
          <Field>
            <FieldLabel>
              Repository + Tag<Input
                v-model="selfUpdateImageReference" class="mt-3"
                placeholder="example: aditiab/quickproxy:windows-latest"
              />
            </FieldLabel><FieldDescription>Enter a Docker repository and optional tag. QuickProxy keeps using this same repo+tag after self-update.</FieldDescription>
          </Field>
        </CardContent>
        <DialogFooter>
          <span class="ml-auto" />
          <Button variant="ghost" :disabled="selfUpdateBusy" @click="showUpdateConfirmDialog = false">
            Cancel
          </Button>
          <Button @click="confirmSelfUpdate" variant="warning" :disabled="selfUpdateBusy">
            <Spinner v-if="selfUpdateBusy" />
            Update
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <header class="mb-6 flex flex-wrap items-end justify-between gap-4">
      <div>
        <div class="eyebrow">
          Management console
        </div><h1 class="page-title mt-1">
          Overview
        </h1><p class="page-lead">
          Reverse proxy configuration, container runtime, certificates, and key/value configuration.
        </p>
      </div><div class="flex flex-wrap items-center gap-2">
        <Badge :variant="app.proxyEnabled ? 'success' : 'warning'">
          Proxy {{ app.proxyEnabled ? 'Enabled' : 'Disabled' }}
        </Badge>
        <Badge :variant="app.configEnabled ? 'success' : 'warning'">
          Config {{ app.configEnabled ? 'Enabled' : 'Disabled' }}
        </Badge>
        <Button variant="secondary" @click="refreshDashboard">
          <RefreshCw />
          Refresh
        </Button>
      </div>
    </header>

    <Card class="p-5 mb-5">
      <div class="flex flex-wrap gap-2">
        <Button v-if="app.proxyEnabled" variant="secondary" as-child>
          <RouterLink to="/proxy-hosts">
            <Network />Proxy Hosts
          </RouterLink>
        </Button>
        <Button v-if="app.proxyEnabled && app.containersEnabled" variant="secondary" as-child>
          <RouterLink to="/containers">
            <Container />Containers
          </RouterLink>
        </Button>
        <Button v-if="app.proxyEnabled" variant="secondary" as-child>
          <RouterLink to="/certificates">
            <BadgeCheck />Certificates
          </RouterLink>
        </Button>
        <Button v-if="app.configEnabled" variant="secondary" as-child>
          <RouterLink to="/key-values">
            <Settings2 />Key/Values
          </RouterLink>
        </Button>
        <Button
          v-if="app.proxyEnabled" variant="secondary"
          @click="triggerReprovision" :disabled="reprovisionBusy"
        >
          <Spinner v-if="reprovisionBusy" /><RefreshCw />
          Re-provision
        </Button>

        <Button
          v-if="false" @click="triggerSelfUpdate" variant="warning" :disabled="selfUpdateBusy"
        >
          <Spinner v-if="selfUpdateBusy" /><DownloadCloud />
          Update QuickProxy
        </Button>
      </div>
      <div v-if="false && selfUpdateImage" class="text-xs text-muted-foreground mt-2">
        Source image: {{ selfUpdateImage }}
      </div>
      <div
        v-else-if="false && selfUpdateStatus?.supported && selfUpdateStatus?.imageUpdateStatus === 'current'"
        class="text-xs text-muted-foreground mt-2"
      >
        QuickProxy is already running the latest available image digest.
      </div>
    </Card>

    <div class="mb-4 grid grid-cols-12 gap-4">
      <div class="col-span-12 md:col-span-6" v-if="app.proxyEnabled">
        <Card class="h-full">
          <CardHeader class="border-b py-4">
            <CardTitle class="flex items-center">
              <span>Proxy</span>
              <span class="ml-auto" />
              <Badge :variant="app.proxyEnabled ? 'success' : 'warning'">
                {{ app.proxyEnabled ? 'Enabled' : 'Disabled' }}
              </Badge>
            </CardTitle>
          </CardHeader>
          <CardContent>
            <template v-if="app.proxyEnabled">
              <div class="grid grid-cols-12 gap-4">
                <div class="col-span-6 sm:col-span-4">
                  <div class="stat-label">
                    Hosts
                  </div>
                  <div class="stat-value">
                    {{ hostCount }}
                  </div>
                </div>
                <div class="col-span-6 sm:col-span-4">
                  <div class="stat-label">
                    Manual
                  </div>
                  <div class="stat-value">
                    {{ manualHostCount }}
                  </div>
                </div>
                <div class="col-span-6 sm:col-span-4">
                  <div class="stat-label">
                    Templates
                  </div>
                  <div class="stat-value">
                    {{ templateCount }}
                  </div>
                </div>
                <div class="col-span-6 sm:col-span-4">
                  <div class="stat-label">
                    Generated
                  </div>
                  <div class="stat-value">
                    {{ generatedCount }}
                  </div>
                </div>
                <div class="col-span-6 sm:col-span-4">
                  <div class="stat-label">
                    Enabled
                  </div>
                  <div class="stat-value">
                    {{ enabledCount }}
                  </div>
                </div>
                <div class="col-span-6 sm:col-span-4">
                  <div class="stat-label">
                    Container Routes
                  </div>
                  <div class="stat-value">
                    {{ containerRouteCount }}
                  </div>
                </div>
              </div>

              <div class="text-xs text-muted-foreground mt-4 mb-1">
                Storage
              </div>
              <div class="text-sm font-medium">
                {{ storageInfo?.proxy.storage.label ?? '-' }}
              </div>
              <div class="text-sm text-muted-foreground">
                {{ storageInfo?.proxy.storage.details ?? 'Unavailable' }}
              </div>
            </template>
            <div v-else class="text-muted-foreground">
              Proxy module is disabled in configuration.
            </div>
          </CardContent>
        </Card>
      </div>

      <div class="col-span-12 md:col-span-6" v-if="app.proxyEnabled && app.containersEnabled">
        <Card class="h-full">
          <CardHeader class="border-b py-4">
            <CardTitle class="flex items-center">
              <span>Container Runtime</span>
              <span class="ml-auto" />
              <Badge :variant="containerEventColor">
                {{ containerEventLabel }}
              </Badge>
            </CardTitle>
          </CardHeader>
          <CardContent>
            <template v-if="app.proxyEnabled">
              <div class="grid grid-cols-12 gap-4">
                <div class="col-span-6 sm:col-span-3">
                  <div class="stat-label">
                    Containers
                  </div>
                  <div class="stat-value">
                    {{ containerCount }}
                  </div>
                </div>
                <div class="col-span-6 sm:col-span-3">
                  <div class="stat-label">
                    Running
                  </div>
                  <div class="stat-value">
                    {{ runningContainerCount }}
                  </div>
                </div>
                <div class="col-span-6 sm:col-span-3">
                  <div class="stat-label">
                    Stopped
                  </div>
                  <div class="stat-value">
                    {{ stoppedContainerCount }}
                  </div>
                </div>
                <div class="col-span-6 sm:col-span-3">
                  <div class="stat-label">
                    Published Ports
                  </div>
                  <div class="stat-value">
                    {{ publishedPortCount }}
                  </div>
                </div>
              </div>

              <div class="text-xs text-muted-foreground mt-4 mb-1">
                Runtime Status
              </div>
              <div class="flex flex-wrap gap-2">
                <Badge :variant="containerStatus?.enabled ? 'success' : 'warning'">
                  {{ containerStatus?.enabled ? 'Enabled' : 'Disabled' }}
                </Badge>
                <Badge variant="default">
                  Last Refresh: {{ formatUtc(containerStatus?.lastSuccessfulRefreshUtc) }}
                </Badge>
                <Badge v-if="containerStatus?.imageUpdatesEnabled" variant="info">
                  Image Check: {{ formatUtc(containerStatus?.lastSuccessfulImageUpdateUtc) }}
                </Badge>
              </div>
              <div v-if="containerStatus?.lastError" class="text-sm text-amber-600 dark:text-amber-300 mt-2">
                {{ containerStatus.lastError }}
              </div>
              <div v-if="containerStatus?.lastImageUpdateError" class="text-sm text-amber-600 dark:text-amber-300 mt-2">
                {{ containerStatus.lastImageUpdateError }}
              </div>
            </template>
            <div v-else class="text-muted-foreground">
              Container runtime data is unavailable while Proxy is disabled.
            </div>
          </CardContent>
        </Card>
      </div>
    </div>

    <div class="mb-4 grid grid-cols-12 gap-4">
      <div class="col-span-12 md:col-span-6" v-if="app.configEnabled">
        <Card class="h-full">
          <CardHeader class="border-b py-4">
            <CardTitle class="flex items-center">
              <span>Config</span>
              <span class="ml-auto" />
              <Badge :variant="app.configEnabled ? 'success' : 'warning'">
                {{ app.configEnabled ? 'Enabled' : 'Disabled' }}
              </Badge>
            </CardTitle>
          </CardHeader>
          <CardContent>
            <template v-if="app.configEnabled">
              <div class="grid grid-cols-12 gap-4">
                <div class="col-span-6 sm:col-span-4">
                  <div class="stat-label">
                    Keys
                  </div>
                  <div class="stat-value">
                    {{ configKeyCount }}
                  </div>
                </div>
                <div class="col-span-6 sm:col-span-4">
                  <div class="stat-label">
                    Folders
                  </div>
                  <div class="stat-value">
                    {{ configFolderCount }}
                  </div>
                </div>
                <div class="col-span-12 sm:col-span-4">
                  <div class="stat-label">
                    Last Updated
                  </div>
                  <div class="stat-value stat-value-sm">
                    {{ latestConfigUpdateLabel }}
                  </div>
                </div>
              </div>

              <div class="text-xs text-muted-foreground mt-4 mb-1">
                Storage
              </div>
              <div class="text-sm font-medium">
                {{ storageInfo?.config.storage.label ?? '-' }}
              </div>
              <div class="text-sm text-muted-foreground">
                {{ storageInfo?.config.storage.details ?? 'Unavailable' }}
              </div>

              <div class="text-xs text-muted-foreground mt-4 mb-1">
                Remote Master Store
              </div>
              <div class="text-sm font-medium">
                {{ storageInfo?.config.remote?.enabled ? 'Enabled' : 'Disabled' }}
              </div>
              <div class="text-sm text-muted-foreground break-all">
                {{ storageInfo?.config.remote?.url ?? 'No remote master store URL configured.' }}
              </div>
            </template>
            <div v-else class="text-muted-foreground">
              Config module is disabled in configuration.
            </div>
          </CardContent>
        </Card>
      </div>

      <div class="col-span-12 md:col-span-6" v-if="app.proxyEnabled">
        <Card class="h-full">
          <CardHeader class="border-b py-4">
            <CardTitle>Highlights</CardTitle>
          </CardHeader>
          <CardContent class="flex flex-col gap-3">
            <div class="flex justify-between items-center">
              <span class="text-muted-foreground">Automatic host templates</span>
              <Badge variant="secondary">
                {{ templateCount }}
              </Badge>
            </div>
            <div class="flex justify-between items-center">
              <span class="text-muted-foreground">Generated hosts from containers</span>
              <Badge variant="info">
                {{ generatedCount }}
              </Badge>
            </div>
            <div class="flex justify-between items-center">
              <span class="text-muted-foreground">WebSocket-enabled hosts</span>
              <Badge variant="success">
                {{ websocketHostCount }}
              </Badge>
            </div>
            <div class="flex justify-between items-center">
              <span class="text-muted-foreground">SSL-forced hosts</span>
              <Badge variant="success">
                {{ sslHostCount }}
              </Badge>
            </div>
            <div class="flex justify-between items-center">
              <span class="text-muted-foreground">Cached asset hosts</span>
              <Badge variant="success">
                {{ cachedHostCount }}
              </Badge>
            </div>
          </CardContent>
        </Card>
      </div>

      <div class="col-span-12">
        <Card>
          <CardHeader class="border-b py-4">
            <CardTitle>Attention</CardTitle>
          </CardHeader>
          <CardContent class="px-0">
            <div v-if="attentionItems.length === 0" class="text-muted-foreground p-4">
              No issues detected.
            </div>
            <div v-else class="grid divide-y">
              <div v-for="item in attentionItems" :key="item.key" class="flex items-start gap-3 p-3">
                <CircleAlert class="mt-0.5 size-4 text-amber-600" /><div>
                  <div class="font-medium">
                    {{ item.title }}
                  </div><div class="text-sm text-muted-foreground">
                    {{ item.description }}
                  </div>
                </div>
              </div>
            </div>
          </CardContent>
        </Card>
      </div>
    </div>
  </div>
</template>

<script lang="ts" setup>
import type { ConfigTreeNode } from '@/composables/useConfigsApi';
import type { ContainerInventorySnapshot } from '@/composables/useContainersApi';
import type { AdminProxyHostDto } from '@/composables/useProxyHostsApi';
import type { SelfUpdateStatus } from '@/composables/useSystemApi';
import { CircleAlert } from '@lucide/vue';
import { computed, onBeforeUnmount, onMounted, ref } from 'vue';
import { RouterLink } from 'vue-router';
import { useConfigsApi } from '@/composables/useConfigsApi';
import { useContainersApi } from '@/composables/useContainersApi';
import { useProxyHostsApi } from '@/composables/useProxyHostsApi';
import { useSystemApi } from '@/composables/useSystemApi';
import { useAppStore } from '@/stores/app';

const errorMessage = ref('');
const updateMessage = ref('');
const selfUpdateBusy = ref(false);
const reprovisionBusy = ref(false);
const showUpdateDialog = ref(false);
const showUpdateConfirmDialog = ref(false);
const selfUpdateImageReference = ref('');
const updateDialogMessage = ref('Preparing update...');
const hosts = ref<AdminProxyHostDto[]>([]);
const containerSnapshot = ref<ContainerInventorySnapshot | null>(null);
const configTree = ref<ConfigTreeNode[]>([]);
const selfUpdateStatus = ref<SelfUpdateStatus | null>(null);
let updatePollTimer: number | null = null;
let updatePollRunning = false;
let updatePollSawFailure = false;
let updatePollStartedAtBaseline: string | null = null;

const app = useAppStore();
const proxyHostsApi = useProxyHostsApi();
const containersApi = useContainersApi();
const configsApi = useConfigsApi();
const systemApi = useSystemApi();

const storageInfo = computed(() => app.systemInfo);
const canSelfUpdate = computed(() =>
  selfUpdateStatus.value?.supported === true && selfUpdateStatus.value?.updateAvailable === true);
const selfUpdateImage = computed(() => selfUpdateStatus.value?.image ?? null);

const hostCount = computed(() => hosts.value.length);
const manualHostCount = computed(() => hosts.value.filter(x => x.mode === 'manual' && !x.runtime.isGenerated).length);
const templateCount = computed(() => hosts.value.filter(x => x.mode === 'automaticContainer' && !x.runtime.isGenerated).length);
const generatedCount = computed(() => hosts.value.filter(x => x.runtime.isGenerated).length);
const enabledCount = computed(() => hosts.value.filter(x => x.enabled && !x.runtime.isGenerated).length);
const websocketHostCount = computed(() => hosts.value.filter(x => x.websockets && !x.runtime.isGenerated).length);
const sslHostCount = computed(() => hosts.value.filter(x => x.forceSsl && !x.runtime.isGenerated).length);
const cachedHostCount = computed(() => hosts.value.filter(x => x.cacheAssets && !x.runtime.isGenerated).length);
const containerRouteCount = computed(() => hosts.value.reduce((count, host) =>
  count + host.routes.filter(route => route.upstreamMode === 'container').length, 0));

const containers = computed(() => containerSnapshot.value?.containers ?? []);
const containerStatus = computed(() => containerSnapshot.value?.status ?? null);
const containerCount = computed(() => containers.value.length);
const runningContainerCount = computed(() => containers.value.filter(x => x.state === 'running').length);
const stoppedContainerCount = computed(() => containers.value.filter(x => x.state !== 'running').length);
const publishedPortCount = computed(() => containers.value.reduce((count, container) =>
  count + container.ports.reduce((portCount, port) => portCount + port.publishedPorts.length, 0), 0));
const containerEventLabel = computed(() =>
  containerStatus.value?.eventStreamConnected ? 'Events Connected' : 'Events Disconnected');
const containerEventColor = computed(() =>
  containerStatus.value?.eventStreamConnected ? 'success' : 'warning');

const configKeyCount = computed(() => countConfigKeys(configTree.value));
const configFolderCount = computed(() => countConfigFolders(configTree.value));
const latestConfigUpdate = computed(() => findLatestConfigUpdate(configTree.value));
const latestConfigUpdateLabel = computed(() => formatUtc(latestConfigUpdate.value));
const attentionItems = computed(() => {
  const items: Array<{
    key: string;
    color: string;
    icon: string;
    title: string;
    description: string;
  }> = [];

  if (!app.proxyEnabled) {
    items.push({
      key: 'proxy-disabled',
      color: 'warning',
      icon: 'mdi-server-off',
      title: 'Proxy module disabled',
      description: 'Reverse proxy, containers, and certificates are unavailable until Proxy.Enabled is true.',
    });
  }

  if (!app.configEnabled) {
    items.push({
      key: 'config-disabled',
      color: 'warning',
      icon: 'mdi-cog-off',
      title: 'Config module disabled',
      description: 'Key/Values APIs and UI are unavailable until Config.Enabled is true.',
    });
  }

  if (app.proxyEnabled && containerStatus.value && !containerStatus.value.eventStreamConnected) {
    items.push({
      key: 'container-events',
      color: 'warning',
      icon: 'mdi-lan-disconnect',
      title: 'Container events disconnected',
      description: 'Container inventory still refreshes, but live Docker event tracking is not connected.',
    });
  }

  if (app.proxyEnabled && containerStatus.value?.lastError) {
    items.push({
      key: 'container-error',
      color: 'error',
      icon: 'mdi-alert-circle-outline',
      title: 'Container runtime reported an error',
      description: containerStatus.value.lastError,
    });
  }

  if (app.proxyEnabled && templateCount.value > 0) {
    const unmatchedTemplates = hosts.value.filter(x =>
      x.mode === 'automaticContainer'
      && !x.runtime.isGenerated
      && x.runtime.activeMatchCount === 0);

    if (unmatchedTemplates.length > 0) {
      items.push({
        key: 'template-matches',
        color: 'warning',
        icon: 'mdi-shape-outline',
        title: 'Automatic templates without active matches',
        description: `${unmatchedTemplates.length} template${unmatchedTemplates.length === 1 ? '' : 's'} currently match no running containers.`,
      });
    }
  }

  return items;
});

onMounted(async () => {
  await refreshDashboard();
});

onBeforeUnmount(() => {
  stopUpdatePolling();
});

async function refreshDashboard() {
  try {
    errorMessage.value = '';
    updateMessage.value = '';
    await app.loadSystemInfo();

    const tasks: Array<Promise<void>> = [];

    tasks.push(loadSelfUpdateStatus());

    if (app.proxyEnabled) {
      tasks.push(loadHosts());

      if (app.containersEnabled) {
        tasks.push(loadContainers());
      }
    }
    else {
      hosts.value = [];
      containerSnapshot.value = null;
    }

    if (!app.containersEnabled) {
      containerSnapshot.value = null;
    }

    if (app.configEnabled) {
      tasks.push(loadConfigTree());
    }
    else {
      configTree.value = [];
    }

    await Promise.all(tasks);
  }
  catch (error) {
    errorMessage.value = (error as Error).message;
  }
}

async function triggerSelfUpdate() {
  if (selfUpdateBusy.value || !canSelfUpdate.value || showUpdateDialog.value) {
    return;
  }

  selfUpdateImageReference.value = selfUpdateImage.value ?? '';
  showUpdateConfirmDialog.value = true;
}

async function triggerReprovision() {
  if (reprovisionBusy.value) {
    return;
  }

  try {
    reprovisionBusy.value = true;
    errorMessage.value = '';

    const result = await systemApi.triggerReprovision();

    updateMessage.value = result.message;
    await refreshDashboard();
  }
  catch (error) {
    errorMessage.value = (error as Error).message;
  }
  finally {
    reprovisionBusy.value = false;
  }
}

async function confirmSelfUpdate() {
  if (selfUpdateBusy.value || !canSelfUpdate.value) {
    return;
  }

  try {
    showUpdateConfirmDialog.value = false;
    selfUpdateBusy.value = true;
    errorMessage.value = '';

    const result = await systemApi.triggerSelfUpdate(selfUpdateImageReference.value);

    updateMessage.value = result.message;
    beginUpdatePolling(app.systemInfo?.startedAtUtc ?? null);
  }
  catch (error) {
    errorMessage.value = (error as Error).message;
  }
  finally {
    selfUpdateBusy.value = false;
  }
}

async function loadHosts() {
  hosts.value = await proxyHostsApi.listHosts();
}

async function loadContainers() {
  if (!app.containersEnabled) {
    containerSnapshot.value = null;
    return;
  }

  containerSnapshot.value = await containersApi.listContainers();
}

async function loadConfigTree() {
  configTree.value = await configsApi.getTree();
}

async function loadSelfUpdateStatus() {
  selfUpdateStatus.value = await systemApi.getSelfUpdateStatus();
}

function beginUpdatePolling(startedAtBaseline: string | null) {
  stopUpdatePolling();
  showUpdateDialog.value = true;
  updateDialogMessage.value = 'Update started. Waiting for QuickProxy to restart...';
  updatePollSawFailure = false;
  updatePollRunning = false;
  updatePollStartedAtBaseline = startedAtBaseline;
  scheduleUpdatePoll(1500);
}

function stopUpdatePolling() {
  if (updatePollTimer !== null) {
    window.clearTimeout(updatePollTimer);
    updatePollTimer = null;
  }

  updatePollRunning = false;
}

function scheduleUpdatePoll(delayMs: number) {
  updatePollTimer = window.setTimeout(() => {
    void pollUpdateStatus();
  }, delayMs);
}

async function pollUpdateStatus() {
  if (!showUpdateDialog.value) {
    stopUpdatePolling();
    return;
  }

  if (updatePollRunning) {
    scheduleUpdatePoll(1000);
    return;
  }

  updatePollRunning = true;

  let shouldContinuePolling = true;

  try {
    const info = await systemApi.getSystemInfo();
    const hasRestarted = !!updatePollStartedAtBaseline
      && !!info.startedAtUtc
      && info.startedAtUtc !== updatePollStartedAtBaseline;

    if (hasRestarted || (updatePollSawFailure && !updatePollStartedAtBaseline)) {
      shouldContinuePolling = false;
      stopUpdatePolling();
      updateDialogMessage.value = 'QuickProxy is back online. Reloading...';
      window.location.reload();
      return;
    }

    updateDialogMessage.value = updatePollSawFailure
      ? 'QuickProxy is starting back up...'
      : 'Waiting for QuickProxy to restart...';
  }
  catch {
    updatePollSawFailure = true;
    updateDialogMessage.value = 'QuickProxy is restarting. Reconnecting...';
  }
  finally {
    updatePollRunning = false;

    if (shouldContinuePolling) {
      scheduleUpdatePoll(2000);
    }
  }
}

function countConfigKeys(nodes: ConfigTreeNode[]): number {
  return nodes.reduce((count, node) => count + (node.type === 'key' ? 1 : 0) + countConfigKeys(node.children), 0);
}

function countConfigFolders(nodes: ConfigTreeNode[]): number {
  return nodes.reduce((count, node) => count + (node.type === 'folder' ? 1 : 0) + countConfigFolders(node.children), 0);
}

function findLatestConfigUpdate(nodes: ConfigTreeNode[]): string | null {
  let latest: string | null = null;

  for (const node of nodes) {
    if (node.type === 'key' && node.key?.updatedAtUtc) {
      if (!latest || node.key.updatedAtUtc > latest) {
        latest = node.key.updatedAtUtc;
      }
    }

    const childLatest = findLatestConfigUpdate(node.children);

    if (childLatest && (!latest || childLatest > latest)) {
      latest = childLatest;
    }
  }

  return latest;
}

function formatUtc(value?: string | null) {
  if (!value) {
    return '-';
  }

  const date = new Date(value);

  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return date.toLocaleString();
}
</script>

<style scoped>
.stat-label {
  color: var(--muted-foreground);
  font-size: 0.78rem;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.stat-value {
  font-size: 1.55rem;
  font-weight: 600;
  line-height: 1.25;
}

.stat-value-sm {
  font-size: 1rem;
}
</style>
