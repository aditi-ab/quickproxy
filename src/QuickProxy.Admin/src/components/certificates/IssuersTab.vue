<template>
  <Card>
    <CardHeader class="border-b py-4">
      <CardTitle>Issuers</CardTitle>
      <CardDescription>Manage CA issuers used to automatically issue and bind host certificates.</CardDescription>
      <CardAction>
        <Button size="sm" @click="emit('create')">
          <Plus />
          New Issuer
        </Button>
      </CardAction>
    </CardHeader>
    <CardContent class="px-0">
      <Table>
        <TableHeader><TableRow><TableHead>ID</TableHead><TableHead>Enabled</TableHead><TableHead>Match Domains</TableHead><TableHead>Type</TableHead></TableRow></TableHeader><TableBody>
          <TableRow v-for="item in items" :key="item.id" class="cursor-pointer" @click="emit('row-click', item)">
            <TableCell class="font-medium">
              {{ item.id }}
            </TableCell><TableCell>
              <Badge :variant="item.issuerEnabled ? 'success' : 'default'">
                {{ item.issuerEnabled ? 'Enabled' : 'Disabled' }}
              </Badge>
            </TableCell><TableCell>
              <div class="flex flex-wrap gap-1">
                <Badge v-for="domain in item.issuerMatchDomains" :key="domain" variant="secondary">
                  {{ domain }}
                </Badge>
                <span v-if="item.issuerMatchDomains.length === 0" class="text-muted-foreground">-</span>
              </div>
            </TableCell><TableCell>
              <Badge variant="secondary">
                {{ item.provider || 'Issuer' }}
              </Badge>
            </TableCell>
          </TableRow><TableEmpty v-if="items.length === 0" :colspan="4">
            No issuers configured.
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
