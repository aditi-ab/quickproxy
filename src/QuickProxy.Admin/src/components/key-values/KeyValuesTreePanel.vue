<template>
  <Card class="h-full">
    <CardHeader class="border-b py-4">
      <CardTitle class="flex items-center">
        <div>
          <div>Configuration tree</div>
          <div class="text-xs text-muted-foreground font-normal">
            Select a folder to browse its entries.
          </div>
        </div>
        <span class="ml-auto" />
        <Button size="sm" variant="ghost" @click="emit('refresh')" :disabled="loadingFolders">
          <Spinner v-if="loadingFolders" /><RefreshCw />
          Refresh
        </Button>
      </CardTitle>
    </CardHeader>

    <CardContent class="px-0">
      <div class="compact-treeview grid gap-0.5 p-1">
        <div
          v-for="entry in flattenedTree" :key="entry.path"
          class="flex items-center w-full tree-item-title h-full"
          :class="{ 'tree-drop-target': dragOverPath !== null && getDropTargetPath(entry.item) === dragOverPath, 'bg-muted': activatedNodes.includes(entry.path) }"
          :style="{ paddingLeft: `${entry.depth * 16 + 6}px` }" :draggable="isDraggableItem(entry.item)"
          role="treeitem" :aria-expanded="entry.hasChildren ? openedFolders.includes(entry.path) : undefined" tabindex="0"
          @click="onActivatedChanged([entry.path])" @dblclick.stop="onTreeItemDoubleClick(entry.item)"
          @keydown.enter.prevent="onActivatedChanged([entry.path])"
          @dragstart="onTreeItemDragStart($event, entry.item)"
          @dragend="emit('tree-drag-end')"
          @dragenter.prevent="onTreeItemDragEnter(entry.item)" @dragover.prevent="onTreeItemDragOver($event, entry.item)"
          @dragleave.prevent="onTreeItemDragLeave($event, entry.item)" @drop.prevent="onTreeItemDrop($event, entry.item)"
        >
          <FolderOpen v-if="entry.hasChildren && openedFolders.includes(entry.path)" class="mr-2 size-4" /><Folder v-else class="mr-2 size-4" />
          <span>{{ entry.name }}</span>
          <span class="ml-auto" />
          <DropdownMenu v-if="entry.path !== rootNodeValue && !getTreeItemReadOnly(entry.item)">
            <DropdownMenuTrigger as-child>
              <Button size="icon-sm" variant="ghost" aria-label="Folder actions" @click.stop="setContextMenuPath(entry.item)">
                <EllipsisVertical />
              </Button>
            </DropdownMenuTrigger><DropdownMenuContent align="end">
              <DropdownMenuItem @select="emitContextAction('copy-path')">
                Copy
              </DropdownMenuItem><DropdownMenuItem @select="emitContextAction('move-path')">
                Move
              </DropdownMenuItem><DropdownMenuItem @select="emitContextAction('rename-folder')">
                Rename
              </DropdownMenuItem><DropdownMenuItem class="text-destructive" @select="emitContextAction('delete-path')">
                Delete
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        </div>
      </div>
    </CardContent>
  </Card>
</template>

<script setup lang="ts">
import { EllipsisVertical, Folder, FolderOpen } from '@lucide/vue';
import { computed, ref } from 'vue';

const props = defineProps<{
  treeItems: unknown[];
  loadingFolders: boolean;
  openedFolders: string[];
  activatedNodes: string[];
  rootNodeValue: string;
}>();

const emit = defineEmits<{
  'refresh': [];
  'update:openedFolders': [value: string[]];
  'update:activatedNodes': [value: string[]];
  'rename-folder': [path: string];
  'copy-path': [path: string];
  'move-path': [path: string];
  'delete-path': [path: string];
  'tree-drag-start': [payload: { event: DragEvent; path: string }];
  'tree-drag-end': [];
  'tree-drop': [payload: { event: DragEvent; targetPath: string }];
}>();

let contextMenuPath = '';
const dragOverPath = ref<string | null>(null);
const flattenedTree = computed(() => {
  const result: Array<{ item: unknown; path: string; name: string; depth: number; hasChildren: boolean }> = [];
  const visit = (items: unknown[], depth: number) => {
    for (const item of items) {
      const path = getTreeItemPath(item);
      const children = getTreeItemChildren(item);

      result.push({ item, path, name: String((getTreeItemValue(item, 'name') ?? path) || 'Configuration'), depth, hasChildren: children.length > 0 });

      if (children.length > 0 && props.openedFolders.includes(path))
        visit(children, depth + 1);
    }
  };

  visit(props.treeItems, 0);
  return result;
});

function onOpenedChanged(paths: unknown) {
  emit('update:openedFolders', normalizeStringArray(paths));
}

function onActivatedChanged(paths: unknown) {
  emit('update:activatedNodes', normalizeStringArray(paths));
}

function setContextMenuPath(item: unknown) {
  contextMenuPath = getTreeItemPath(item);
}

function emitContextAction(action: 'rename-folder' | 'copy-path' | 'move-path' | 'delete-path') {
  if (!contextMenuPath) {
    return;
  }

  switch (action) {
    case 'rename-folder':
      emit('rename-folder', contextMenuPath);
      break;
    case 'copy-path':
      emit('copy-path', contextMenuPath);
      break;
    case 'move-path':
      emit('move-path', contextMenuPath);
      break;
    case 'delete-path':
      emit('delete-path', contextMenuPath);
      break;
  }
}

function isDraggableItem(item: unknown) {
  return getTreeItemPath(item) !== props.rootNodeValue;
}

function getDropTargetPath(item: unknown) {
  const path = getTreeItemPath(item);

  return path === props.rootNodeValue ? '' : path;
}

function onTreeItemDragStart(event: DragEvent, item: unknown) {
  const path = getTreeItemPath(item);

  if (!path || path === props.rootNodeValue) {
    event.preventDefault();
    return;
  }

  if (event.dataTransfer) {
    event.dataTransfer.effectAllowed = 'copyMove';
  }

  emit('tree-drag-start', { event, path });
}

function onTreeItemDragEnter(item: unknown) {
  dragOverPath.value = getDropTargetPath(item);
}

function onTreeItemDragOver(event: DragEvent, item: unknown) {
  dragOverPath.value = getDropTargetPath(item);

  if (event.dataTransfer) {
    event.dataTransfer.dropEffect = event.ctrlKey ? 'copy' : 'move';
  }
}

function onTreeItemDragLeave(event: DragEvent, item: unknown) {
  const currentTarget = event.currentTarget as Node | null;
  const relatedTarget = event.relatedTarget as Node | null;

  if (currentTarget && relatedTarget && currentTarget.contains(relatedTarget)) {
    return;
  }

  const currentPath = getDropTargetPath(item);

  if (dragOverPath.value === currentPath) {
    dragOverPath.value = null;
  }
}

function onTreeItemDrop(event: DragEvent, item: unknown) {
  const targetPath = getDropTargetPath(item);

  dragOverPath.value = null;
  emit('tree-drop', { event, targetPath });
}

function normalizeStringArray(value: unknown): string[] {
  if (!Array.isArray(value)) {
    return [];
  }

  return value.filter((x): x is string => typeof x === 'string');
}

function getTreeItemPath(item: unknown) {
  if (!item || typeof item !== 'object') {
    return '';
  }

  const record = item as Record<string, unknown>;

  if (typeof record.path === 'string') {
    return record.path;
  }

  const raw = record.raw;

  if (raw && typeof raw === 'object') {
    const rawPath = (raw as Record<string, unknown>).path;

    if (typeof rawPath === 'string') {
      return rawPath;
    }
  }

  return '';
}

function treeItemHasChildren(item: unknown) {
  if (!item || typeof item !== 'object') {
    return false;
  }

  const record = item as Record<string, unknown>;
  const directChildren = record.children;

  if (Array.isArray(directChildren)) {
    return directChildren.length > 0;
  }

  const raw = record.raw;

  if (raw && typeof raw === 'object') {
    const rawChildren = (raw as Record<string, unknown>).children;

    return Array.isArray(rawChildren) && rawChildren.length > 0;
  }

  return false;
}

function getTreeItemChildren(item: unknown): unknown[] {
  const value = getTreeItemValue(item, 'children');

  return Array.isArray(value) ? value : [];
}

function getTreeItemReadOnly(item: unknown) {
  const value = getTreeItemValue(item, 'readOnly');

  return value === true;
}

function getTreeItemValue(item: unknown, key: string) {
  if (!item || typeof item !== 'object') {
    return null;
  }

  const record = item as Record<string, unknown>;

  if (key in record) {
    return record[key];
  }

  const raw = record.raw;

  if (raw && typeof raw === 'object') {
    return (raw as Record<string, unknown>)[key];
  }

  return null;
}

function onTreeItemDoubleClick(item: unknown) {
  const path = getTreeItemPath(item);

  if (!path || !treeItemHasChildren(item)) {
    return;
  }

  const next = [...(props.openedFolders ?? [])];
  const index = next.indexOf(path);

  if (index >= 0) {
    next.splice(index, 1);
  }
  else {
    next.push(path);
  }

  emit('update:openedFolders', next);
}
</script>

<style scoped>
.compact-treeview {
  user-select: none;
}

.tree-item-title {
  display: flex;
  width: 100%;
  min-height: 28px;
  border-radius: 6px;
  padding-inline: 6px 4px;
  margin-inline-end: 4px;
  transition:
    background-color 120ms ease,
    outline-color 120ms ease;
}

.tree-drop-target {
  background: color-mix(in srgb, var(--primary) calc(0.16 * 100%), transparent);
  outline: 1px dashed color-mix(in srgb, var(--primary) calc(0.8 * 100%), transparent);
  outline-offset: -1px;
}
</style>
