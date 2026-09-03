<template>
  <CardContent class="py-4">
    <div class="flex flex-wrap items-center gap-2">
      <Badge variant="default">
        Sets: {{ defaultSets.length }}
      </Badge>
    </div>
  </CardContent>

  <Separator />

  <CardContent class="px-0">
    <Table>
      <TableHeader><TableRow><TableHead>Id</TableHead><TableHead>Labels</TableHead><TableHead>Env Vars</TableHead><TableHead>Mounts</TableHead><TableHead>Hosts</TableHead><TableHead>Updated</TableHead></TableRow></TableHeader><TableBody>
        <TableRow v-for="item in defaultSets" :key="item.id" class="cursor-pointer" @click="emit('row-click', item.id)">
          <TableCell class="font-medium">
            {{ item.id }}
          </TableCell><TableCell>{{ item.labels.length }}</TableCell><TableCell>{{ item.envVars.length }}</TableCell><TableCell>{{ item.mountBindings?.length ?? 0 }}</TableCell><TableCell>{{ item.hostMappings?.length ?? 0 }}</TableCell><TableCell>{{ format(new Date(item.updatedAtUtc), 'yyyy-MM-dd HH:mm:ss') }}</TableCell>
        </TableRow><TableEmpty v-if="defaultSets.length === 0" :colspan="6">
          No default sets configured.
        </TableEmpty>
      </TableBody>
    </Table>
  </CardContent>
</template>

<script lang="ts" setup>
import type { ContainerDefaultsSet } from '@/composables/useContainersApi';
import { format } from 'date-fns';

const props = defineProps<{
  defaultSets: ContainerDefaultsSet[];
}>();

const emit = defineEmits<{
  'row-click': [id: string];
}>();
</script>
