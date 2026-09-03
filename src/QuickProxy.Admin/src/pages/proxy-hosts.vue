<template>
  <div class="page-container">
    <Alert v-if="errorMessage" class="mb-4" variant="destructive">
      {{ errorMessage }}
    </Alert>

    <header class="mb-6 flex flex-wrap items-end justify-between gap-4">
      <div>
        <div class="eyebrow">
          Proxy
        </div><h1 class="page-title mt-1">
          Proxy hosts
        </h1><p class="page-lead">
          Manage automatic and manual reverse proxy host definitions.
        </p>
      </div><div class="flex flex-wrap items-center gap-2">
        <Badge variant="default">
          {{ hosts.length }} total
        </Badge>
        <div class="page-action-buttons">
          <Button variant="secondary" @click="loadHosts">
            <RefreshCw />
            Refresh
          </Button>
          <Button @click="createHost">
            <Plus />
            New host
          </Button>
        </div>
      </div>
    </header>

    <Tabs v-model="tab">
      <TabsList>
        <TabsTrigger value="auto">
          Automatic Hosts
        </TabsTrigger>
        <TabsTrigger value="manual">
          Manual Hosts
        </TabsTrigger>
        <TabsTrigger value="translations">
          Domain Translations
        </TabsTrigger>
      </TabsList>

      <TabsContent class="mt-4" value="auto">
        <ProxyHostsSectionCard
          v-for="section in automaticSections" :key="section.key" :section="section"
          :expanded="expandedBySection[section.key] || []" :link-settings="linkSettings"
          @update:expanded="expandedBySection[section.key] = $event" @edit-host="editHost"
          @toggle-enabled="onToggleEnabled"
        />
      </TabsContent>

      <TabsContent class="mt-4" value="manual">
        <ProxyHostsSectionCard
          :section="manualSection" :expanded="expandedBySection[manualSection.key] || []"
          :link-settings="linkSettings" @update:expanded="expandedBySection[manualSection.key] = $event"
          @edit-host="editHost" @toggle-enabled="onToggleEnabled"
        />
      </TabsContent>

      <TabsContent class="mt-4" value="translations">
        <DomainTranslationsSectionCard
          :items="domainTranslations" @create-rule="createDomainTranslation"
          @reload-rules="loadDomainTranslations" @edit-rule="editDomainTranslation"
          @toggle-enabled="onToggleDomainTranslationEnabled"
        />
      </TabsContent>
    </Tabs>

    <ProxyHostDialog
      v-model="showDialog" :host="form" :is-edit="isEdit" :save-error="dialogErrorMessage"
      @save="saveHost" @delete="deleteCurrentHost"
    />
    <DomainTranslationDialog
      v-model="showDomainTranslationDialog" :rule="domainTranslationForm"
      :is-edit="isEditDomainTranslation" :save-error="domainTranslationErrorMessage" @save="saveDomainTranslation"
      @delete="deleteCurrentDomainTranslation"
    />
  </div>
</template>

<script lang="ts" setup>
import type { DomainTranslationRule } from '@/composables/useDomainTranslationsApi';
import type { AdminProxyHostDto, ProxyHostConfig, ProxyHostLinkSettings } from '@/composables/useProxyHostsApi';
import { computed, onMounted, reactive, ref } from 'vue';
import DomainTranslationsSectionCard from '@/components/domain-translations/DomainTranslationsSectionCard.vue';
import ProxyHostsSectionCard from '@/components/proxy-hosts/ProxyHostsSectionCard.vue';
import { useDomainTranslationsApi } from '@/composables/useDomainTranslationsApi';
import { useProxyHostsApi } from '@/composables/useProxyHostsApi';
import DomainTranslationDialog from '@/dialogs/DomainTranslationDialog.vue';
import ProxyHostDialog from '@/dialogs/ProxyHostDialog.vue';

const tab = ref<'auto' | 'manual' | 'translations'>('auto');
const hosts = ref<AdminProxyHostDto[]>([]);
const domainTranslations = ref<DomainTranslationRule[]>([]);
const showDialog = ref(false);
const isEdit = ref(false);
const showDomainTranslationDialog = ref(false);
const isEditDomainTranslation = ref(false);
const errorMessage = ref('');
const dialogErrorMessage = ref('');
const domainTranslationErrorMessage = ref('');
const form = ref<ProxyHostConfig>(emptyHost());
const domainTranslationForm = ref<DomainTranslationRule>(emptyDomainTranslation());
const proxyHostsApi = useProxyHostsApi();
const domainTranslationsApi = useDomainTranslationsApi();
const linkSettings = ref<ProxyHostLinkSettings>({
  httpPort: 80,
  httpsPort: 443,
});
const expandedBySection = reactive<Record<string, string[]>>({
  manual: [],
  templates: [],
  generated: [],
});

const automaticSections = computed(() => [
  {
    key: 'templates',
    title: 'Templates',
    color: 'secondary',
    emptyLabel: 'templates',
    items: hosts.value.filter(x => x.mode === 'automaticContainer' && !x.runtime.isGenerated),
  },
  {
    key: 'generated',
    title: 'Generated',
    color: 'info',
    emptyLabel: 'generated hosts',
    items: hosts.value.filter(x => x.runtime.isGenerated),
  },
]);

const manualSection = computed(() => ({
  key: 'manual',
  title: 'Manual Hosts',
  color: 'default',
  emptyLabel: 'proxy hosts',
  items: hosts.value.filter(x => x.mode === 'manual' && !x.runtime.isGenerated),
}));

onMounted(async () => {
  await Promise.all([
    loadHosts(),
    loadDomainTranslations(),
    loadLinkSettings(),
  ]);
});

async function loadHosts() {
  try {
    errorMessage.value = '';
    hosts.value = await proxyHostsApi.listHosts();
  }
  catch (error) {
    errorMessage.value = (error as Error).message;
  }
}

async function loadLinkSettings() {
  try {
    linkSettings.value = await proxyHostsApi.getLinkSettings();
  }
  catch {
  }
}

async function loadDomainTranslations() {
  try {
    errorMessage.value = '';
    domainTranslations.value = await domainTranslationsApi.listRules();
  }
  catch (error) {
    errorMessage.value = (error as Error).message;
  }
}

function createHost() {
  dialogErrorMessage.value = '';
  isEdit.value = false;
  form.value = emptyHost();
  showDialog.value = true;
}

function editHost(host: AdminProxyHostDto) {
  if (host.runtime.readOnly) {
    return;
  }

  dialogErrorMessage.value = '';
  isEdit.value = true;
  form.value = toEditableHost(host);
  showDialog.value = true;
}

function createDomainTranslation() {
  domainTranslationErrorMessage.value = '';
  isEditDomainTranslation.value = false;
  domainTranslationForm.value = emptyDomainTranslation();
  showDomainTranslationDialog.value = true;
}

function editDomainTranslation(rule: DomainTranslationRule) {
  domainTranslationErrorMessage.value = '';
  isEditDomainTranslation.value = true;
  domainTranslationForm.value = JSON.parse(JSON.stringify(rule)) as DomainTranslationRule;
  showDomainTranslationDialog.value = true;
}

async function saveHost(updatedHost: ProxyHostConfig) {
  try {
    dialogErrorMessage.value = '';

    if (isEdit.value) {
      await proxyHostsApi.updateHost(updatedHost);
    }
    else {
      await proxyHostsApi.createHost(updatedHost);
    }

    showDialog.value = false;
    await loadHosts();
  }
  catch (error) {
    dialogErrorMessage.value = (error as Error).message;
  }
}

async function saveDomainTranslation(rule: DomainTranslationRule) {
  try {
    domainTranslationErrorMessage.value = '';

    if (isEditDomainTranslation.value) {
      await domainTranslationsApi.updateRule(rule);
    }
    else {
      await domainTranslationsApi.createRule(rule);
    }

    showDomainTranslationDialog.value = false;
    await loadDomainTranslations();
  }
  catch (error) {
    domainTranslationErrorMessage.value = (error as Error).message;
  }
}

async function setHostEnabled(host: ProxyHostConfig, enabled: boolean) {
  try {
    errorMessage.value = '';
    await proxyHostsApi.setHostEnabled(host, enabled);
    await loadHosts();
  }
  catch (error) {
    errorMessage.value = (error as Error).message;
  }
}

async function setDomainTranslationEnabled(rule: DomainTranslationRule, enabled: boolean) {
  try {
    errorMessage.value = '';
    await domainTranslationsApi.setRuleEnabled(rule, enabled);
    await loadDomainTranslations();
  }
  catch (error) {
    errorMessage.value = (error as Error).message;
  }
}

async function deleteCurrentHost() {
  if (!isEdit.value || !form.value.id) {
    return;
  }

  try {
    dialogErrorMessage.value = '';
    await proxyHostsApi.deleteHost(form.value.id);
    showDialog.value = false;
    await loadHosts();
  }
  catch (error) {
    dialogErrorMessage.value = (error as Error).message;
  }
}

async function deleteCurrentDomainTranslation() {
  if (!isEditDomainTranslation.value || !domainTranslationForm.value.id) {
    return;
  }

  try {
    domainTranslationErrorMessage.value = '';
    await domainTranslationsApi.deleteRule(domainTranslationForm.value.id);
    showDomainTranslationDialog.value = false;
    await loadDomainTranslations();
  }
  catch (error) {
    domainTranslationErrorMessage.value = (error as Error).message;
  }
}

function toEditableHost(host: AdminProxyHostDto): ProxyHostConfig {
  const { runtime: _runtime, ...config } = JSON.parse(JSON.stringify(host)) as AdminProxyHostDto & { runtime: unknown };

  return config;
}

function emptyHost(): ProxyHostConfig {
  return {
    id: '',
    mode: 'manual',
    enabled: true,
    domainNames: [],
    automaticContainer: {
      labelSelectors: [
        {
          key: '',
          valuePattern: null,
          valuePatterns: [],
        },
      ],
      domainTemplates: [],
    },
    forceSsl: false,
    cacheAssets: false,
    websockets: true,
    routes: [{
      path: '/',
      rewriteMode: 'preserve',
      preserveOriginalHostHeader: true,
      sendForwardedHeaders: true,
      ignoreBadCertificates: false,
      upstreamMode: 'manual',
      upstream: {
        scheme: 'http',
        host: '',
        port: 80,
      },
      container: {
        containerName: '',
        scheme: 'http',
        port: 80,
        portResolutionMode: 'container',
        networkName: null,
      },
    }],
    tls: {
      mode: 'none',
      storeName: 'My',
      storeLocation: 'LocalMachine',
    },
    certificateId: null,
  };
}

function onToggleEnabled(value: { host: AdminProxyHostDto; enabled: boolean }) {
  void setHostEnabled(value.host, value.enabled);
}

function onToggleDomainTranslationEnabled(value: { rule: DomainTranslationRule; enabled: boolean }) {
  void setDomainTranslationEnabled(value.rule, value.enabled);
}

function emptyDomainTranslation(): DomainTranslationRule {
  return {
    id: '',
    enabled: true,
    sourceDomain: '',
    targetDomain: '',
    certificateId: null,
    rewriteHostHeader: true,
  };
}
</script>

<style scoped></style>
