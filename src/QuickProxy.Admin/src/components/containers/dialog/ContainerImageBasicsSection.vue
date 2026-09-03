<template>
  <div class="grid grid-cols-12 gap-4">
    <div class="col-span-12">
      <input
        ref="imageArchiveInput" type="file" accept=".tar,.tar.gz,.tgz,.gz" class="hidden"
        @change="onImageArchiveSelected"
      >
      <Card
        rounded border class="image-upload-area" :class="{ 'image-upload-area--dragover': isDragOver }"
        @click="openImageArchivePicker" @dragenter.prevent="isDragOver = true" @dragover.prevent="isDragOver = true"
        @dragleave.prevent="onDragLeave" @drop.prevent="onImageArchiveDropped"
      >
        <div v-if="imageArchive" class="flex justify-end px-2 pt-2">
          <Button size="sm" @click.stop="clearImageArchive" variant="warning">
            <X />
            Remove file
          </Button>
        </div>
        <div class="flex flex-col items-center text-center gap-2 py-6 px-4">
          <PackageOpen class="size-9 text-primary" />
          <div class="text-base font-semibold font-medium">
            {{ isEdit ? 'Replacement Image Archive' : 'Image Archive' }}
          </div>
          <div class="text-sm text-muted-foreground">
            {{ isEdit ? 'Drop a replacement Docker image archive here, or click to select a file'
              : 'Drop a Docker image archive here, or click to select a file' }}
          </div>
          <div class="text-xs text-muted-foreground">
            Supports `.tar`, `.tar.gz`, and `.tgz`
          </div>
          <Badge v-if="imageArchive" variant="default">
            {{ imageArchive.name }}
          </Badge>
        </div>
      </Card>
    </div>
    <div class="col-span-12" v-if="archiveInspecting">
      <Progress :model-value="50" class="mt-1 animate-pulse" />
    </div>
    <div class="col-span-12" v-else-if="archiveInspectError">
      <Alert class="mt-1">
        {{ archiveInspectError }}
      </Alert>
    </div>
    <div class="col-span-12" v-else-if="archiveInfo">
      <Alert class="mt-1">
        <div class="font-medium mb-1">
          Archive Preview
        </div>
        <div v-if="archiveInfo.repoTags.length > 0" class="text-sm">
          Repo tags: {{ archiveInfo.repoTags.join(', ') }}
        </div>
        <div v-else class="text-sm">
          No repo tags detected in archive.
        </div>
        <div v-if="archiveInfo.suggestedImage" class="text-sm mt-1">
          Suggested image: {{ archiveInfo.suggestedImage }}
        </div>
      </Alert>
    </div>
    <div class="col-span-12" v-if="archiveRepositoryMismatchMessage">
      <Alert class="mt-1">
        {{ archiveRepositoryMismatchMessage }}
      </Alert>
    </div>
    <div class="col-span-12 md:col-span-6">
      <Field>
        <FieldLabel for="container-name">
          Name
        </FieldLabel><Input id="container-name" v-model="form.name" :disabled="isEdit" />
      </Field>
    </div>
    <div class="col-span-12 md:col-span-6">
      <Field>
        <FieldLabel for="container-image">
          Image
        </FieldLabel><Input id="container-image" v-model="form.image" />
      </Field>
    </div>
    <div class="col-span-12 md:col-span-6">
      <Field>
        <FieldLabel for="container-restart-policy">
          Restart Policy
        </FieldLabel><Select v-model="form.restartPolicy">
          <SelectTrigger id="container-restart-policy">
            <SelectValue placeholder="Restart Policy" />
          </SelectTrigger><SelectContent>
            <SelectItem v-for="option in restartPolicies" :key="String((typeof option === 'string' ? option : (option as any).value))" :value="(typeof option === 'string' ? option : (option as any).value)">
              {{ (typeof option === 'string' ? option : (option as any).title ?? (option as any).value) }}
            </SelectItem>
          </SelectContent>
        </Select>
      </Field>
    </div>
    <div class="col-span-12" v-if="triggerDefaultsSetId">
      <Alert>
        Startup defaults set: <strong>{{ triggerDefaultsSetId }}</strong>
      </Alert>
    </div>
  </div>
</template>

<script setup lang="ts">
import type { ContainerEditRequest, ContainerImageArchiveInfo } from '@/composables/useContainersApi';
import { PackageOpen } from '@lucide/vue';

import { ref } from 'vue';

const props = defineProps<{
  isEdit: boolean;
  form: ContainerEditRequest;
  imageArchive: File | null;
  archiveInspecting: boolean;
  archiveInspectError: string;
  archiveInfo: ContainerImageArchiveInfo | null;
  archiveRepositoryMismatchMessage: string;
  triggerDefaultsSetId: string;
  restartPolicies: Array<ContainerEditRequest['restartPolicy']>;
}>();

const emit = defineEmits<{
  'update:imageArchive': [value: File | null];
}>();

const isDragOver = ref(false);
const imageArchiveInput = ref<HTMLInputElement | null>(null);

function openImageArchivePicker() {
  imageArchiveInput.value?.click();
}

function onImageArchiveSelected(event: Event) {
  const input = event.target as HTMLInputElement | null;
  const file = input?.files?.[0] ?? null;

  emit('update:imageArchive', file);

  if (input) {
    input.value = '';
  }
}

function onImageArchiveDropped(event: DragEvent) {
  isDragOver.value = false;

  const file = event.dataTransfer?.files?.[0] ?? null;

  emit('update:imageArchive', file);

  if (imageArchiveInput.value) {
    imageArchiveInput.value.value = '';
  }
}

function onDragLeave(event: DragEvent) {
  const currentTarget = event.currentTarget as Node | null;
  const relatedTarget = event.relatedTarget as Node | null;

  if (currentTarget && relatedTarget && currentTarget.contains(relatedTarget)) {
    return;
  }

  isDragOver.value = false;
}

function clearImageArchive() {
  emit('update:imageArchive', null);

  if (imageArchiveInput.value) {
    imageArchiveInput.value.value = '';
  }
}
</script>

<style scoped>
.image-upload-area {
  border-style: dashed !important;
  cursor: pointer;
  transition:
    border-color 0.2s ease,
    background-color 0.2s ease;
}

.image-upload-area--dragover {
  border-color: var(--primary) !important;
  background-color: color-mix(in srgb, var(--primary) calc(0.08 * 100%), transparent);
}
</style>
