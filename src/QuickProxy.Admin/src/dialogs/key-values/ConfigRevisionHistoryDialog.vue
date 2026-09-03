<template>
  <Dialog
    :open="modelValue" @update:open="emit('update:modelValue', $event)"
  >
    <DialogContent size="4xl" scrollable>
      <DialogHeader>
        <DialogTitle class="flex items-center gap-2">
          <span>Revision History</span>
          <Badge variant="secondary">
            {{ entryKey }}
          </Badge>
        </DialogTitle>
        <DialogDescription>Inspect previous versions of this entry and restore a selected revision.</DialogDescription>
      </DialogHeader>
      <div data-slot="dialog-body" class="dialog-body-content -mx-4 overflow-x-hidden px-4">
        <Alert v-if="errorMessage" class="mb-4" variant="destructive">
          {{ errorMessage }}
        </Alert>

        <div class="grid grid-cols-12 gap-4">
          <div class="col-span-12 md:col-span-4">
            <div class="grid divide-y rounded-lg border">
              <button
                v-for="revision in revisions" :key="revision.revisionId"
                type="button" class="p-3 text-left hover:bg-muted" :class="{ 'bg-muted': revision.revisionId === selectedRevisionId }" @click="emit('update:selectedRevisionId', revision.revisionId)"
              >
                <div class="font-medium">
                  {{ formatDate(revision.capturedAtUtc) }}
                </div><div class="text-sm text-muted-foreground">
                  {{ revision.action }} · {{ revision.capturedBy || revision.updatedBy || 'unknown' }}
                </div>
              </button>
              <div v-if="revisions.length === 0" class="p-4 text-muted-foreground">
                No local revisions found.
              </div>
            </div>
          </div>

          <div class="col-span-12 md:col-span-8">
            <template v-if="details">
              <MonacoEditorField
                v-if="details.snapshot.payloadKind === 'text'" :key="details.revisionId"
                :model-value="details.snapshot.value" :language="editorLanguage" :model-uri="revisionModelUri"
                :height="420" :font-size="13" :read-only="true"
              />
              <div v-else class="flex items-center gap-2">
                <Button variant="secondary" @click="emit('download-binary')">
                  <Download />
                  Download Binary
                </Button>
                <span class="text-muted-foreground">{{ details.snapshot.mediaType || 'application/octet-stream' }}</span>
              </div>

              <div class="flex flex-wrap gap-2 items-center mt-4">
                <Badge :variant="details.snapshot.entryType === 'secret' ? 'destructive' : 'info'">
                  {{ details.snapshot.entryType === 'secret' ? 'Secret' : 'Data' }}
                </Badge>
                <Badge variant="secondary">
                  {{ details.snapshot.payloadKind === 'binary' ? 'Binary' : 'Text' }}
                </Badge>
                <Badge variant="secondary">
                  {{ formatDate(details.capturedAtUtc) }}
                </Badge>
              </div>

              <div class="mt-4">
                <div class="text-xs text-muted-foreground mb-2">
                  Labels
                </div>
                <div v-if="details.snapshot.labels.length > 0" class="flex flex-wrap gap-1">
                  <Badge
                    v-for="label in details.snapshot.labels" :key="`${label.key}:${label.value}`" variant="secondary"
                  >
                    {{ label.key }}={{ label.value }}
                  </Badge>
                </div>
                <div v-else class="text-muted-foreground">
                  No labels.
                </div>
              </div>
            </template>

            <div v-else class="text-muted-foreground py-10 text-center">
              Select a revision to inspect it.
            </div>
          </div>
        </div>
      </div>
      <DialogFooter>
        <span class="ml-auto" />
        <Button
          v-if="details?.snapshot.entryType === 'secret' && !details.snapshot.isRevealed" @click="emit('reveal-revision')" variant="warning" :disabled="revealingRevision"
        >
          <Spinner v-if="revealingRevision" />
          Reveal
        </Button>
        <Button variant="ghost" @click="emit('update:modelValue', false)">
          Close
        </Button>
        <Button
          :disabled="!selectedRevisionId"
          @click="emit('restore-revision')"
        >
          <Spinner v-if="restoringRevision" />
          Restore
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>

<script setup lang="ts">
import type { ConfigEntryRevisionDetails, ConfigEntryRevisionSummary } from '@/composables/useConfigsApi';
import { format } from 'date-fns';
import { computed } from 'vue';
import MonacoEditorField from '@/components/MonacoEditorField.vue';

const props = defineProps<{
  modelValue: boolean;
  entryKey: string;
  revisions: ConfigEntryRevisionSummary[];
  selectedRevisionId: string;
  details: ConfigEntryRevisionDetails | null;
  errorMessage: string;
  loadingRevisionHistory: boolean;
  restoringRevision: boolean;
  revealingRevision: boolean;
  editorLanguage: string;
}>();

const emit = defineEmits<{
  'update:modelValue': [value: boolean];
  'update:selectedRevisionId': [value: string];
  'restore-revision': [];
  'reveal-revision': [];
  'download-binary': [];
}>();

const details = computed(() => props.details);
const revisionModelUri = computed(() => {
  if (!props.details) {
    return 'inmemory://quickproxy/revisions/empty.txt';
  }

  const extension = props.editorLanguage === 'yaml'
    ? 'yaml'
    : props.editorLanguage === 'json'
      ? 'json'
      : props.editorLanguage === 'xml'
        ? 'xml'
        : 'txt';

  return `inmemory://quickproxy/revisions/${encodeURIComponent(props.details.key)}/${props.details.revisionId}.${extension}`;
});

function formatDate(value: string) {
  return format(new Date(value), 'yyyy-MM-dd HH:mm:ss');
}
</script>
