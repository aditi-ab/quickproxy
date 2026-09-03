<template>
  <Card class="mb-4">
    <CardHeader class="border-b py-4">
      <CardTitle class="flex items-center">
        <span>{{ section.title }}</span>
        <span class="ml-auto" />
        <Badge :variant="section.color">
          {{ section.items.length }}
        </Badge>
      </CardTitle>
    </CardHeader>

    <CardContent class="px-0">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Status</TableHead><TableHead>ID</TableHead><TableHead>Domains</TableHead><TableHead>Upstream</TableHead><TableHead>Runtime</TableHead><TableHead>TLS</TableHead><TableHead>Options</TableHead><TableHead class="text-right">
              Actions
            </TableHead>
          </TableRow>
        </TableHeader><TableBody>
          <template v-for="item in section.items" :key="item.id">
            <TableRow class="cursor-pointer" @click="emit('edit-host', item)">
              <TableCell>
                <Badge variant="secondary">
                  {{ statusLabel(item) }}
                </Badge>
              </TableCell><TableCell class="font-medium">
                {{ item.id }}
              </TableCell><TableCell>
                <div class="flex flex-wrap gap-1">
                  <Badge v-for="domain in item.domainNames" :key="domain" variant="secondary">
                    {{ domain }}
                  </Badge><span v-if="item.domainNames.length === 0">-</span>
                </div>
              </TableCell><TableCell>
                <span v-if="item.routes.length === 0">-</span><span v-else-if="item.mode === 'automaticContainer' && !item.runtime.isGenerated">{{ item.automaticContainer.labelSelectors.length }} label selectors</span><span v-else-if="item.routes[0]?.upstreamMode === 'container'">{{ item.routes[0].container.scheme }}://{{ item.routes[0].container.containerName || item.runtime.matchedContainerName || 'container' }}:{{ item.routes[0].container.port }}</span><span v-else>{{ item.routes[0]?.upstream.scheme }}://{{ item.routes[0]?.upstream.host }}:{{ item.routes[0]?.upstream.port }}</span>
              </TableCell><TableCell><span v-if="item.runtime.isGenerated">{{ item.runtime.matchedContainerName }}</span><span v-else-if="item.mode === 'automaticContainer'">{{ item.runtime.activeMatchCount }} active</span><span v-else>-</span></TableCell><TableCell>{{ item.certificateId || (item.tls?.mode ?? 'none') }}</TableCell><TableCell>
                <div class="flex gap-1">
                  <Badge v-if="item.forceSsl" variant="secondary">
                    SSL
                  </Badge><Badge v-if="item.cacheAssets" variant="secondary">
                    Cache
                  </Badge><Badge v-if="item.websockets" variant="secondary">
                    WS
                  </Badge>
                </div>
              </TableCell><TableCell>
                <div class="flex justify-end gap-1">
                  <Button v-if="!item.runtime.readOnly" size="sm" variant="outline" @click.stop="emit('toggle-enabled', { host: item, enabled: !item.enabled })">
                    {{ item.enabled ? 'Disable' : 'Enable' }}
                  </Button><Button size="sm" variant="ghost" @click.stop="toggleExpanded(item.id)">
                    {{ isExpanded(item.id) ? 'Collapse' : 'Details' }}
                  </Button>
                </div>
              </TableCell>
            </TableRow>
            <TableRow v-if="isExpanded(item.id)">
              <TableCell :colspan="8" class="bg-muted px-0 py-0">
                <div class="proxied-locations-panel">
                  <div v-if="proxiedLocations(item).length === 0" class="text-muted-foreground">
                    No clickable locations available.
                  </div>
                  <div v-else class="grid gap-1 p-0 bg-transparent proxied-locations-list">
                    <button
                      v-for="location in proxiedLocations(item)" :key="location.key"
                      type="button" class="proxied-location-item flex items-start gap-3 rounded-md p-2 text-left hover:bg-muted" :disabled="!location.href" @click.stop="openLocation(location.href)"
                    >
                      <span><span class="proxied-location-title block font-medium">{{ location.display }}</span><span class="proxied-location-subtitle block text-sm text-muted-foreground">{{ location.description }}</span></span>
                    </button>
                  </div>
                </div>
              </TableCell>
            </TableRow>
          </template>
          <TableEmpty v-if="section.items.length === 0" :colspan="8">
            No {{ section.emptyLabel }}.
          </TableEmpty>
        </TableBody>
      </Table>
    </CardContent>
  </Card>
</template>

<script setup lang="ts">
import type { AdminProxyHostDto, ProxyHostLinkSettings } from '@/composables/useProxyHostsApi';

interface SectionDto {
  key: string;
  title: string;
  color: string;
  emptyLabel: string;
  items: AdminProxyHostDto[];
}

const props = defineProps<{
  section: SectionDto;
  expanded: string[];
  linkSettings: ProxyHostLinkSettings;
}>();

const emit = defineEmits<{
  'update:expanded': [value: string[]];
  'edit-host': [host: AdminProxyHostDto];
  'toggle-enabled': [value: { host: AdminProxyHostDto; enabled: boolean }];
}>();

const hostHeaders = [
  { title: 'Status', key: 'status', sortable: false, width: 120 },
  { title: 'ID', key: 'id' },
  { title: 'Domains', key: 'domainNames' },
  { title: 'Upstream', key: 'upstream' },
  { title: 'Runtime', key: 'runtimeInfo', sortable: false },
  { title: 'TLS', key: 'tls' },
  { title: 'Flags', key: 'flags', sortable: false },
  { title: '', key: 'actions', sortable: false, align: 'end' as const, width: 72 },
];

function onRowClick(_event: Event, row: { item: AdminProxyHostDto }) {
  if (row.item.runtime.isGenerated) {
    toggleExpanded(row.item.id);
    return;
  }

  emit('edit-host', row.item);
}

function statusColor(host: AdminProxyHostDto) {
  if (!host.enabled) {
    return 'destructive';
  }

  if (host.mode === 'automaticContainer' && host.runtime.activeMatchCount === 0) {
    return 'warning';
  }

  return 'success';
}

function statusLabel(host: AdminProxyHostDto) {
  if (!host.enabled) {
    return 'Disabled';
  }

  if (host.mode === 'automaticContainer' && host.runtime.activeMatchCount === 0) {
    return 'No matches';
  }

  return 'Active';
}

function proxiedLocations(host: AdminProxyHostDto) {
  const baseDomains = host.domainNames.length > 0
    ? host.domainNames
    : host.mode === 'automaticContainer'
      ? host.automaticContainer.domainTemplates
      : [];

  const locations = host.routes.flatMap(route => baseDomains.map((domain) => {
    const normalizedPath = route.path === '/' ? '/' : route.path;
    const scheme = preferredScheme(host);
    const href = /\{[^}]+\}/.test(domain)
      ? null
      : `${scheme}://${domain}${portSuffix(scheme)}${normalizedPath}`;

    return {
      key: `${domain}:${route.path}:${scheme}`,
      display: `${domain}${portDisplaySuffix(scheme)}${normalizedPath}`,
      href,
      description: describeRoute(route, host),
    };
  }));

  return Array.from(new Map(locations.map(location => [`${location.display}|${location.href ?? ''}`, location])).values());
}

function preferredScheme(host: AdminProxyHostDto) {
  if ((host.forceSsl || host.certificateId || host.tls?.mode !== 'none') && props.linkSettings.httpsPort > 0) {
    return 'https';
  }

  return 'http';
}

function portSuffix(scheme: 'http' | 'https') {
  const port = scheme === 'https' ? props.linkSettings.httpsPort : props.linkSettings.httpPort;

  if (port <= 0 || (scheme === 'http' && port === 80) || (scheme === 'https' && port === 443)) {
    return '';
  }

  return `:${port}`;
}

function portDisplaySuffix(scheme: 'http' | 'https') {
  return portSuffix(scheme);
}

function describeRoute(route: AdminProxyHostDto['routes'][number], host: AdminProxyHostDto) {
  const target = route.upstreamMode === 'container'
    ? `${route.container.scheme}://${route.container.containerName || host.runtime.matchedContainerName || 'container'}:${route.container.port}`
    : `${route.upstream.scheme}://${route.upstream.host}:${route.upstream.port}`;

  if (route.rewriteMode === 'replacePrefix' && route.rewriteTargetPath) {
    return `${route.path} -> ${route.rewriteTargetPath} to ${target}`;
  }

  if (route.rewriteMode === 'stripPrefix') {
    return `${route.path} stripped before proxying to ${target}`;
  }

  return `${route.path} proxied to ${target}`;
}

function openLocation(href: string | null) {
  if (!href) {
    return;
  }

  window.open(href, '_blank', 'noopener,noreferrer');
}

function isExpanded(hostId: string) {
  return props.expanded?.includes(hostId) ?? false;
}

function toggleExpanded(hostId: string) {
  const current = props.expanded ?? [];

  if (current.includes(hostId)) {
    emit('update:expanded', current.filter(x => x !== hostId));
    return;
  }

  emit('update:expanded', [...current, hostId]);
}
</script>

<style scoped>
.cursor-pointer {
  cursor: pointer;
}

.proxied-locations-list {
  padding-block: 2px;
}

.proxied-location-item {
  min-height: 30px;
}

.proxied-location-title {
  font-size: 0.82rem;
  line-height: 1.1rem;
}

.proxied-location-subtitle {
  font-size: 0.72rem;
  line-height: 0.95rem;
}

.proxied-locations-panel {
  padding: 0;
}
</style>
