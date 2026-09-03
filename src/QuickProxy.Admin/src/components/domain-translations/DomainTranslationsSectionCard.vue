<template>
  <Card class="mb-4">
    <CardHeader class="border-b py-4">
      <CardTitle>Domain Translation</CardTitle>
      <CardAction class="flex flex-wrap items-center justify-end gap-2">
        <Button size="sm" @click="emit('create-rule')" variant="warning">
          <Plus />
          New Rule
        </Button>
        <Button variant="secondary" size="sm" @click="emit('reload-rules')">
          <RefreshCw />
          Reload
        </Button>
        <Badge variant="warning">
          {{ items.length }} total
        </Badge>
      </CardAction>
    </CardHeader>

    <CardContent class="px-0">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Status</TableHead><TableHead>ID</TableHead><TableHead>Source</TableHead><TableHead>Target</TableHead><TableHead>TLS</TableHead><TableHead>Options</TableHead><TableHead class="text-right">
              Actions
            </TableHead>
          </TableRow>
        </TableHeader><TableBody>
          <TableRow v-for="item in items" :key="item.id" class="cursor-pointer" @click="emit('edit-rule', item)">
            <TableCell>
              <Badge variant="secondary">
                {{ item.enabled ? 'Enabled' : 'Disabled' }}
              </Badge>
            </TableCell><TableCell class="font-medium">
              {{ item.id }}
            </TableCell><TableCell>{{ item.sourceDomain }}</TableCell><TableCell>{{ item.targetDomain }}</TableCell><TableCell>{{ item.certificateId || '-' }}</TableCell><TableCell>
              <Badge variant="info">
                {{ item.rewriteHostHeader ? 'Rewrite Host/SNI' : 'Preserve Host' }}
              </Badge>
            </TableCell><TableCell class="text-right">
              <Tooltip>
                <TooltipTrigger as-child>
                  <Button size="icon-sm" variant="ghost" :aria-label="item.enabled ? 'Disable rule' : 'Enable rule'" @click.stop="emit('toggle-enabled', { rule: item, enabled: !item.enabled })">
                    <Pause v-if="item.enabled" /><Play v-else />
                  </Button>
                </TooltipTrigger><TooltipContent>{{ item.enabled ? 'Disable rule' : 'Enable rule' }}</TooltipContent>
              </Tooltip>
            </TableCell>
          </TableRow>
          <TableEmpty v-if="items.length === 0" :colspan="7">
            No domain translation rules.
          </TableEmpty>
        </TableBody>
      </Table>
    </CardContent>
  </Card>
</template>

<script setup lang="ts">
import type { DomainTranslationRule } from '@/composables/useDomainTranslationsApi';
import { Pause, Play } from '@lucide/vue';

defineProps<{
  items: DomainTranslationRule[];
}>();

const emit = defineEmits<{
  'create-rule': [];
  'reload-rules': [];
  'edit-rule': [rule: DomainTranslationRule];
  'toggle-enabled': [value: { rule: DomainTranslationRule; enabled: boolean }];
}>();
</script>

<style scoped>
</style>
