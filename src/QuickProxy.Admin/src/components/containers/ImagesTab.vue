<template>
  <CardContent class="py-4">
    <div class="flex flex-wrap items-center justify-between gap-3">
      <div class="flex flex-wrap items-center gap-3">
        <Badge variant="default">
          Images: {{ images.length }}
        </Badge>
        <Field class="w-auto" orientation="horizontal">
          <Switch id="show-all-images" v-model="showAllModel" /><FieldLabel for="show-all-images">
            Show all images
          </FieldLabel>
        </Field>
      </div>
      <Button
        size="sm"
        @click="emit('prune')" variant="warning" :disabled="pruningImages"
      >
        <Spinner v-if="pruningImages" /><BrushCleaning />
        Prune Unused Images
      </Button>
    </div>
  </CardContent>

  <Separator />

  <CardContent class="px-0">
    <Table>
      <TableHeader><TableRow><TableHead>Tags</TableHead><TableHead>Image ID</TableHead><TableHead>Created</TableHead><TableHead>Size</TableHead><TableHead>Containers</TableHead><TableHead>Labels</TableHead></TableRow></TableHeader><TableBody>
        <TableRow v-for="item in images" :key="item.id">
          <TableCell>
            <div class="flex flex-wrap gap-1 py-1">
              <Badge v-for="tag in displayRepoTags(item.repoTags)" :key="`${item.id}-${tag}`" variant="secondary">
                {{ tag }}
              </Badge>
              <span v-if="displayRepoTags(item.repoTags).length === 0" class="text-muted-foreground">untagged</span>
            </div>
          </TableCell><TableCell><span class="font-mono text-xs">{{ shortImageId(item.id) }}</span></TableCell><TableCell>{{ format(new Date(item.createdUtc), 'yyyy-MM-dd HH:mm:ss') }}</TableCell><TableCell>{{ formatBytes(item.sizeBytes) }}</TableCell><TableCell>{{ item.containers }}</TableCell><TableCell>
            <div class="flex flex-wrap gap-1 py-1">
              <Badge
                v-for="label in imageLabelEntries(item.labels)" :key="`${item.id}-${label.key}`" variant="secondary"
              >
                {{ label.key }}={{ label.value }}
              </Badge>
              <span v-if="imageLabelEntries(item.labels).length === 0" class="text-muted-foreground">-</span>
            </div>
          </TableCell>
        </TableRow><TableEmpty v-if="images.length === 0" :colspan="6">
          No Docker images found.
        </TableEmpty>
      </TableBody>
    </Table>
  </CardContent>
</template>

<script lang="ts" setup>
import type { ContainerImageInventoryItem } from '@/composables/useContainersApi';

import { format } from 'date-fns';
import { computed } from 'vue';

const props = defineProps<{
  images: ContainerImageInventoryItem[];
  showAllImages: boolean;
  pruningImages: boolean;
}>();

const emit = defineEmits<{
  'update:showAllImages': [value: boolean];
  'prune': [];
}>();

const showAllModel = computed({
  get: () => props.showAllImages,
  set: value => emit('update:showAllImages', value),
});

function imageLabelEntries(labels: Record<string, string>) {
  return Object.entries(labels)
    .filter(([key]) => !key.startsWith('quickproxy.internal.'))
    .sort(([left], [right]) => left.localeCompare(right))
    .slice(0, 8)
    .map(([key, value]) => ({ key, value }));
}

function displayRepoTags(repoTags: string[]) {
  return repoTags.slice(0, 8);
}

function shortImageId(value: string) {
  return value.startsWith('sha256:')
    ? value.slice(7, 19)
    : value.slice(0, 12);
}

function formatBytes(value: number) {
  if (value <= 0) {
    return '0 B';
  }

  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let size = value;
  let unitIndex = 0;

  while (size >= 1024 && unitIndex < units.length - 1) {
    size /= 1024;
    unitIndex += 1;
  }

  return `${size >= 10 || unitIndex === 0 ? size.toFixed(0) : size.toFixed(1)} ${units[unitIndex]}`;
}
</script>
