<template>
  <Card>
    <CardHeader class="border-b py-4">
      <CardTitle>Issued certificates</CardTitle>
      <CardDescription>
        Manage TLS/SSL certificates used for proxy host TLS configurations. Certificates can be stored in the file
        system, Windows certificate store, or provided by an issuer.
      </CardDescription>
      <CardAction>
        <Button size="sm" @click="emit('create')">
          <Plus />
          New Certificate
        </Button>
      </CardAction>
    </CardHeader>
    <CardContent class="px-0">
      <Table>
        <TableHeader><TableRow><TableHead>ID</TableHead><TableHead>Domains</TableHead><TableHead>Type/Provider</TableHead><TableHead>Expires</TableHead><TableHead>In Use</TableHead></TableRow></TableHeader><TableBody>
          <TableRow v-for="item in items" :key="item.id" class="cursor-pointer" @click="emit('row-click', item)">
            <TableCell class="font-medium">
              {{ item.id }}
            </TableCell><TableCell>
              <div class="flex flex-wrap gap-1">
                <Badge v-for="domain in item.domainNames" :key="domain" variant="secondary">
                  {{ domain }}
                </Badge>
                <span v-if="item.domainNames.length === 0" class="text-muted-foreground">-</span>
              </div>
            </TableCell><TableCell>
              <Badge variant="secondary">
                {{ item.provider || item.mode }}
              </Badge>
            </TableCell><TableCell>
              {{ item.expiresAtUtc ? new Date(item.expiresAtUtc).toLocaleDateString() : '-' }}
            </TableCell><TableCell>
              <Badge :variant="item.inUse ? 'success' : 'default'">
                {{ item.inUse ? `Yes (${item.inUseCount})` : 'No' }}
              </Badge>
            </TableCell>
          </TableRow><TableEmpty v-if="items.length === 0" :colspan="5">
            No certificates configured.
          </TableEmpty>
        </TableBody>
      </Table>
    </CardContent>
  </Card>
</template>

<script lang="ts" setup>
import type { StoredCertificateConfig } from '@/composables/useCertificatesApi';

defineProps<{
  items: StoredCertificateConfig[];
}>();

const emit = defineEmits<{
  'create': [];
  'row-click': [item: StoredCertificateConfig];
}>();
</script>
