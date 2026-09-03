<template>
  <div class="flex items-center flex-wrap gap-2 mb-4">
    <div>
      <div class="text-base font-semibold">
        Routes
      </div>
      <div class="text-sm text-muted-foreground">
        Match an incoming path and choose where QuickProxy sends the request.
      </div>
    </div>
    <span class="ml-auto" />
    <Button
      v-if="localForm.mode === 'manual'" size="sm" variant="secondary"
      @click="emit('reload-containers')"
    >
      Reload Containers
    </Button>
    <Button size="sm" @click="addRoute">
      <Plus />
      Add Route
    </Button>
  </div>

  <div v-if="localForm.routes.length === 0" class="text-center text-muted-foreground py-4">
    No routes configured.
  </div>

  <div class="grid grid-cols-12 gap-4" v-else>
    <div class="col-span-12" v-for="(route, index) in localForm.routes" :key="`route-${index}`">
      <Card class="proxy-route-card">
        <CardHeader class="border-b">
          <CardTitle>
            Route {{ index + 1 }}
          </CardTitle>
          <CardAction>
            <Button @click="removeRoute(index)" variant="destructive">
              <Trash2 />
              Delete route
            </Button>
          </CardAction>
        </CardHeader>
        <CardContent class="space-y-4">
          <div class="grid grid-cols-12 gap-4">
            <div class="col-span-12 md:col-span-4">
              <Field>
                <FieldLabel>Path</FieldLabel><Input
                  v-model="route.path"
                /><FieldDescription>Use / for default traffic or /nginx for a prefix route.</FieldDescription>
              </Field>
            </div>
            <div
              class="col-span-12"
              :class="route.rewriteMode === 'replacePrefix' ? 'md:col-span-4' : 'md:col-span-8'"
            >
              <Field>
                <FieldLabel>Path Rewrite</FieldLabel><Select v-model="route.rewriteMode">
                  <SelectTrigger><SelectValue placeholder="Path Rewrite" /></SelectTrigger><SelectContent>
                    <SelectItem v-for="option in rewriteOptions" :key="String((typeof option === 'string' ? option : (option as any).value))" :value="(typeof option === 'string' ? option : (option as any).value)">
                      {{ (typeof option === 'string' ? option : (option as any).title ?? (option as any).value) }}
                    </SelectItem>
                  </SelectContent>
                </Select>
              </Field>
            </div>
            <div class="col-span-12 md:col-span-4" v-if="route.rewriteMode === 'replacePrefix'">
              <Field>
                <FieldLabel>Target Path</FieldLabel><Input
                  v-model="route.rewriteTargetPath"
                /><FieldDescription>Example: /my/custom/path</FieldDescription>
              </Field>
            </div>
          </div>

          <template v-if="localForm.mode === 'manual'">
            <div class="grid grid-cols-12 gap-4">
              <div class="col-span-12 sm:col-span-4 lg:col-span-2">
                <Field>
                  <FieldLabel>Scheme</FieldLabel><Select :model-value="selectedScheme(route)" @update:model-value="updateSelectedScheme(route, $event)">
                    <SelectTrigger><SelectValue placeholder="Scheme" /></SelectTrigger><SelectContent>
                      <SelectItem v-for="option in ['http', 'https']" :key="String((typeof option === 'string' ? option : (option as any).value))" :value="(typeof option === 'string' ? option : (option as any).value)">
                        {{ (typeof option === 'string' ? option : (option as any).title ?? (option as any).value) }}
                      </SelectItem>
                    </SelectContent>
                  </Select>
                </Field>
              </div>
              <div class="col-span-12 sm:col-span-8 lg:col-span-6">
                <Field>
                  <FieldLabel>Target Host</FieldLabel><Input :model-value="targetHostInputValue(route)" list="target-host-options" @update:model-value="updateSelectedTargetHost(route, String($event))" /><datalist id="target-host-options">
                    <option v-for="option in targetHostOptions" :key="option.value" :value="option.value">
                      {{ option.label }}
                    </option>
                  </datalist><FieldDescription>Type a hostname/IP or select a discovered container.</FieldDescription>
                </Field>
              </div>
              <div class="col-span-12 sm:col-span-6 lg:col-span-4">
                <Field><FieldLabel>Target Port</FieldLabel><Input :model-value="portInputValue(route)" type="number" @update:model-value="updateSelectedPort(route, $event)" /><FieldDescription>Type a port manually or use a discovered port.</FieldDescription></Field>
              </div>
              <div
                class="col-span-12"
                v-if="route.upstreamMode === 'container' && route.container.portResolutionMode !== 'published'"
              >
                <Field>
                  <FieldLabel>Preferred Network</FieldLabel><Select v-model="route.container.networkName">
                    <SelectTrigger><SelectValue placeholder="Preferred Network" /></SelectTrigger><SelectContent>
                      <SelectItem v-for="option in networkOptions(route)" :key="String((typeof option === 'string' ? option : (option as any).value))" :value="(typeof option === 'string' ? option : (option as any).value)">
                        {{ (typeof option === 'string' ? option : (option as any).title ?? (option as any).value) }}
                      </SelectItem>
                    </SelectContent>
                  </Select>
                </Field>
              </div>
            </div>
          </template>

          <template v-else>
            <div class="grid grid-cols-12 gap-4">
              <div class="col-span-12 sm:col-span-6 lg:col-span-3">
                <Field>
                  <FieldLabel>Target Type</FieldLabel><Select v-model="route.upstreamMode">
                    <SelectTrigger><SelectValue placeholder="Target Type" /></SelectTrigger><SelectContent>
                      <SelectItem v-for="option in upstreamModeOptions" :key="String((typeof option === 'string' ? option : (option as any).value))" :value="(typeof option === 'string' ? option : (option as any).value)">
                        {{ (typeof option === 'string' ? option : (option as any).title ?? (option as any).value) }}
                      </SelectItem>
                    </SelectContent>
                  </Select>
                </Field>
              </div>
              <div class="col-span-12 sm:col-span-6 lg:col-span-3">
                <Field>
                  <FieldLabel>Scheme</FieldLabel><Select :model-value="selectedScheme(route)" @update:model-value="updateSelectedScheme(route, $event)">
                    <SelectTrigger><SelectValue placeholder="Scheme" /></SelectTrigger><SelectContent>
                      <SelectItem v-for="option in ['http', 'https']" :key="String((typeof option === 'string' ? option : (option as any).value))" :value="(typeof option === 'string' ? option : (option as any).value)">
                        {{ (typeof option === 'string' ? option : (option as any).title ?? (option as any).value) }}
                      </SelectItem>
                    </SelectContent>
                  </Select>
                </Field>
              </div>
              <template v-if="route.upstreamMode === 'manual'">
                <div class="col-span-12 sm:col-span-8 lg:col-span-4">
                  <Field>
                    <FieldLabel>Target Host</FieldLabel><Input
                      v-model="route.upstream.host"
                    /><FieldDescription>Static hostname or IP for this route.</FieldDescription>
                  </Field>
                </div>
                <div class="col-span-12 sm:col-span-4 lg:col-span-2">
                  <Field>
                    <FieldLabel>Target Port</FieldLabel><Input
                      :model-value="route.upstream.port" type="number"
                      @update:model-value="updateManualPort(route, $event)"
                    />
                  </Field>
                </div>
              </template>
              <template v-else>
                <div class="col-span-12 sm:col-span-6 lg:col-span-3">
                  <Field>
                    <FieldLabel>Port Resolution</FieldLabel><Select v-model="route.container.portResolutionMode">
                      <SelectTrigger><SelectValue placeholder="Port Resolution" /></SelectTrigger><SelectContent>
                        <SelectItem v-for="option in portResolutionOptions" :key="String((typeof option === 'string' ? option : (option as any).value))" :value="(typeof option === 'string' ? option : (option as any).value)">
                          {{ (typeof option === 'string' ? option : (option as any).title ?? (option as any).value) }}
                        </SelectItem>
                      </SelectContent>
                    </Select>
                  </Field>
                </div>
                <div class="col-span-12 sm:col-span-6 lg:col-span-3">
                  <Field>
                    <FieldLabel>Target Port</FieldLabel><Input
                      :model-value="route.container.port" type="number"
                      @update:model-value="updateAutomaticContainerPort(route, $event)"
                    /><FieldDescription>Used for every matched container.</FieldDescription>
                  </Field>
                </div>
                <div class="col-span-12 sm:col-span-6 lg:col-span-3" v-if="route.container.portResolutionMode !== 'published'">
                  <Field>
                    <FieldLabel>Preferred Network</FieldLabel><Input
                      v-model="route.container.networkName"
                    /><FieldDescription>Optional network name used for container IP routing.</FieldDescription>
                  </Field>
                </div>
                <div class="flex items-center col-span-12 sm:col-span-6 lg:col-span-3">
                  <span class="text-muted-foreground">Matched container is selected automatically from labels.</span>
                </div>
              </template>
            </div>
          </template>

          <div class="route-options mt-4">
            <Field orientation="horizontal" class="items-start">
              <Switch :id="`route-${index}-preserve-host`" v-model="route.preserveOriginalHostHeader" class="mt-0.5" />
              <div class="grid gap-1">
                <FieldLabel :for="`route-${index}-preserve-host`">
                  Preserve original Host header
                </FieldLabel><FieldDescription>Enabled by default. When off, QuickProxy sends the upstream target host in the Host header instead.</FieldDescription>
              </div>
            </Field>
            <Field orientation="horizontal" class="items-start">
              <Switch :id="`route-${index}-forwarded-headers`" v-model="route.sendForwardedHeaders" class="mt-0.5" />
              <div class="grid gap-1">
                <FieldLabel :for="`route-${index}-forwarded-headers`">
                  Send X-Forwarded headers
                </FieldLabel><FieldDescription>Enabled by default. When off, QuickProxy does not add X-Forwarded-* headers for this route.</FieldDescription>
              </div>
            </Field>
            <Field v-if="selectedScheme(route) === 'https'" orientation="horizontal" class="items-start">
              <Switch :id="`route-${index}-ignore-certificates`" v-model="route.ignoreBadCertificates" class="mt-0.5" />
              <div class="grid gap-1">
                <FieldLabel :for="`route-${index}-ignore-certificates`">
                  Ignore invalid upstream HTTPS certificates
                </FieldLabel><FieldDescription>Use only for internal/self-signed upstreams. This disables upstream certificate validation for this route.</FieldDescription>
              </div>
            </Field>
          </div>
        </CardContent>
      </Card>
    </div>
  </div>

  <Alert v-if="containerError && localForm.mode === 'manual'" class="mt-4">
    {{ containerError }}
  </Alert>
</template>

<script lang="ts" setup>
import type { ContainerInventoryItem } from '@/composables/useContainersApi';

import type { ContainerUpstreamTarget, ProxyHostConfig, ProxyRouteConfig, UpstreamTarget } from '@/composables/useProxyHostsApi';
import { computed } from 'vue';

interface PortOption {
  label: string;
  value: number;
  resolutionMode: 'container' | 'published';
}

interface TargetHostOption {
  label: string;
  value: string;
}

const props = defineProps<{
  localForm: ProxyHostConfig;
  containerOptions: ContainerInventoryItem[];
  containerError: string;
  rewriteOptions: Array<{ title: string; value: string }>;
  upstreamModeOptions: Array<{ title: string; value: string }>;
  portResolutionOptions: Array<{ title: string; value: string }>;
}>();

const emit = defineEmits<{
  'reload-containers': [];
}>();

const targetHostOptions = computed<TargetHostOption[]>(() =>
  props.containerOptions.map(container => ({
    label: container.name,
    value: container.name,
  })),
);

function addRoute() {
  props.localForm.routes.push(emptyRoute('/'));
}

function removeRoute(index: number) {
  if (index < 0) {
    return;
  }

  props.localForm.routes.splice(index, 1);
}

function selectedContainer(route: ProxyRouteConfig) {
  return props.containerOptions.find(x => x.name === route.container.containerName) ?? null;
}

function selectedScheme(route: ProxyRouteConfig) {
  return route.upstreamMode === 'container'
    ? route.container.scheme
    : route.upstream.scheme;
}

function updateSelectedScheme(route: ProxyRouteConfig, value: string | null) {
  if (value !== 'http' && value !== 'https') {
    return;
  }

  if (route.upstreamMode === 'container') {
    route.container.scheme = value;
    return;
  }

  route.upstream.scheme = value;
}

function selectedTargetHost(route: ProxyRouteConfig): string | TargetHostOption {
  if (route.upstreamMode === 'container') {
    return targetHostOptions.value.find(x => x.value === route.container.containerName)
      ?? route.container.containerName;
  }

  return route.upstream.host;
}

function targetHostInputValue(route: ProxyRouteConfig): string {
  const value = selectedTargetHost(route);

  return typeof value === 'string' ? value : value.value;
}

function updateSelectedTargetHost(route: ProxyRouteConfig, value: string | TargetHostOption | null) {
  if (!value) {
    route.upstreamMode = 'manual';
    route.upstream.host = '';
    return;
  }

  if (typeof value === 'object') {
    route.upstreamMode = 'container';
    route.container.containerName = value.value;

    const firstPort = portOptions(route)[0];

    if (firstPort) {
      updateSelectedPort(route, firstPort);
    }

    return;
  }

  route.upstreamMode = 'manual';
  route.upstream.host = value;
}

function portOptions(route: ProxyRouteConfig): PortOption[] {
  if (route.upstreamMode !== 'container') {
    return [];
  }

  const container = selectedContainer(route);

  if (!container) {
    return [];
  }

  const results: PortOption[] = [];
  const seenPublishedPorts = new Set<number>();

  for (const port of container.ports.filter(x => x.protocol === 'tcp')) {
    results.push({
      label: `${port.containerPort} - Container port`,
      value: port.containerPort,
      resolutionMode: 'container',
    });

    for (const binding of port.publishedBindings) {
      if (seenPublishedPorts.has(binding.hostPort)) {
        continue;
      }

      seenPublishedPorts.add(binding.hostPort);
      results.push({
        label: `${binding.hostPort} - Host port -> ${port.containerPort}/tcp${binding.hostIp ? ` (${formatHostIp(binding.hostIp)})` : ''}`,
        value: binding.hostPort,
        resolutionMode: 'published',
      });
    }
  }

  return results;
}

function selectedPortValue(route: ProxyRouteConfig): number | string | PortOption | null {
  if (route.upstreamMode === 'container') {
    return portOptions(route).find(x =>
      x.value === route.container.port
      && x.resolutionMode === route.container.portResolutionMode,
    ) ?? route.container.port;
  }

  return route.upstream.port;
}

function portInputValue(route: ProxyRouteConfig): number | string {
  const value = selectedPortValue(route);

  return value && typeof value === 'object' ? value.value : value ?? '';
}

function updateSelectedPort(route: ProxyRouteConfig, value: number | string | PortOption | null) {
  if (!value) {
    return;
  }

  if (typeof value === 'object') {
    route.container.port = value.value;
    route.container.portResolutionMode = value.resolutionMode;

    if (value.resolutionMode === 'published') {
      route.container.networkName = null;
    }

    return;
  }

  const parsed = typeof value === 'number' ? value : Number.parseInt(value, 10);

  if (!Number.isFinite(parsed) || parsed <= 0) {
    return;
  }

  if (route.upstreamMode === 'container') {
    route.container.port = parsed;
    route.container.portResolutionMode = 'container';
    return;
  }

  route.upstream.port = parsed;
}

function updateManualPort(route: ProxyRouteConfig, value: string | number | null) {
  const parsed = typeof value === 'number' ? value : Number.parseInt(value ?? '', 10);

  if (!Number.isFinite(parsed) || parsed <= 0) {
    return;
  }

  route.upstream.port = parsed;
}

function updateAutomaticContainerPort(route: ProxyRouteConfig, value: string | number | null) {
  const parsed = typeof value === 'number' ? value : Number.parseInt(value ?? '', 10);

  if (!Number.isFinite(parsed) || parsed <= 0) {
    return;
  }

  route.container.port = parsed;
}

function networkOptions(route: ProxyRouteConfig) {
  return selectedContainer(route)?.networks.map(x => x.name) ?? [];
}

function formatHostIp(value: string) {
  if (!value || value === '0.0.0.0' || value === '::') {
    return 'localhost';
  }

  return value;
}

function emptyRoute(path: string): ProxyRouteConfig {
  return {
    path,
    rewriteMode: 'preserve',
    rewriteTargetPath: null,
    preserveOriginalHostHeader: true,
    sendForwardedHeaders: true,
    ignoreBadCertificates: false,
    upstreamMode: 'manual',
    upstream: emptyManualUpstream(),
    container: emptyContainerTarget(),
  };
}

function emptyManualUpstream(): UpstreamTarget {
  return {
    scheme: 'http',
    host: '',
    port: 80,
  };
}

function emptyContainerTarget(): ContainerUpstreamTarget {
  return {
    containerName: '',
    scheme: 'http',
    port: 80,
    portResolutionMode: 'container',
    networkName: null,
  };
}
</script>

<style scoped>
.route-options {
  display: grid;
  gap: 1rem;
  border: 1px solid var(--border);
  border-radius: var(--radius);
  padding: 1rem;
}
</style>
