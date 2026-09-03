<template>
  <Dialog
    :open="modelValue"
    @update:open="emit('update:modelValue', $event)"
  >
    <DialogContent size="4xl" scrollable class="logs-dialog-card sm:max-w-6xl">
      <DialogHeader>
        <DialogTitle class="flex items-center gap-2 pr-8">
          <span class="truncate">Project Logs: {{ projectId }}<span v-if="service"> / {{ service }}</span></span>
          <span class="ml-auto" />
          <Badge :variant="connected ? 'success' : 'warning'">
            {{ connected ? 'Live' : 'Connecting' }}
          </Badge>
        </DialogTitle>
        <DialogDescription class="sr-only">
          Live output from the selected Compose project.
        </DialogDescription>
      </DialogHeader>
      <div data-slot="dialog-body" class="logs-dialog-content !overflow-hidden">
        <div class="logs-dialog-toolbar flex flex-wrap items-center gap-2">
          <Button size="sm" variant="secondary" @click="restartStream">
            <RefreshCw />
            Reconnect
          </Button>
          <Button size="sm" variant="ghost" @click="clearLogs">
            <Eraser />
            Clear
          </Button>
          <Field orientation="horizontal" class="ml-auto w-auto items-center">
            <Switch id="compose-logs-auto-scroll" v-model="autoScroll" />
            <FieldLabel for="compose-logs-auto-scroll">
              Auto-scroll
            </FieldLabel>
          </Field>
        </div>
        <Alert v-if="errorMessage" class="mt-3" variant="destructive">
          <CircleAlert /><AlertDescription>{{ errorMessage }}</AlertDescription>
        </Alert>
        <div ref="logContainer" class="logs-dialog-body">
          <div class="logs-output">
            <div v-for="(entry, index) in lines" :key="`${index}-${entry.timestamp}-${entry.service}`" class="log-row">
              <span class="log-timestamp">{{ formatTimestamp(entry.timestamp) }}</span>
              <span class="log-service">{{ entry.service || '-' }}</span>
              <span class="log-message">{{ entry.message }}</span>
            </div>
          </div>
        </div>
      </div>
    </DialogContent>
  </Dialog>
</template>

<script lang="ts" setup>
import type { ComponentPublicInstance } from 'vue';
import type { ComposeProjectLogEntry } from '@/composables/useContainersApi';

import { CircleAlert } from '@lucide/vue';
import { format } from 'date-fns';
import { nextTick, onBeforeUnmount, ref, watch } from 'vue';
import { useContainersApi } from '@/composables/useContainersApi';

const props = defineProps<{
  modelValue: boolean;
  projectId: string;
  service?: string;
}>();

const emit = defineEmits<{
  'update:modelValue': [value: boolean];
}>();

const api = useContainersApi();
const logContainer = ref<HTMLElement | ComponentPublicInstance | null>(null);
const lines = ref<ComposeProjectLogEntry[]>([]);
const errorMessage = ref('');
const connected = ref(false);
const autoScroll = ref(true);
let abortController: AbortController | null = null;

watch(() => props.modelValue, async (value) => {
  if (value) {
    await restartStream();
    return;
  }

  stopStream();
}, { immediate: true });

watch(() => [props.projectId, props.service] as const, async ([value, service], previous) => {
  if (!props.modelValue || !value) {
    return;
  }

  if (previous && previous[0] === value && previous[1] === service) {
    return;
  }

  lines.value = [];
  await restartStream();
});

onBeforeUnmount(() => {
  stopStream();
});

async function restartStream() {
  stopStream();
  lines.value = [];
  errorMessage.value = '';
  connected.value = false;

  if (!props.projectId) {
    return;
  }

  abortController = new AbortController();

  try {
    for await (const entry of api.streamComposeProjectLogs(props.projectId, props.service, abortController.signal)) {
      connected.value = true;
      lines.value.push(entry);

      if (lines.value.length > 1000) {
        lines.value.splice(0, lines.value.length - 1000);
      }

      if (autoScroll.value) {
        await scrollToBottom();
      }
    }
  }
  catch (error) {
    if (isAbortError(error) || abortController?.signal.aborted) {
      return;
    }

    errorMessage.value = (error as Error).message;
    connected.value = false;
  }
}

function stopStream() {
  abortController?.abort();
  abortController = null;
  connected.value = false;
}

function clearLogs() {
  lines.value = [];
}

function isAbortError(error: unknown) {
  if (error instanceof DOMException) {
    return error.name === 'AbortError';
  }

  if (error instanceof Error) {
    return error.name === 'AbortError'
      || error.message.toLowerCase().includes('aborted')
      || error.message.toLowerCase().includes('abort');
  }

  return false;
}

function formatTimestamp(value: string) {
  if (!value) {
    return '';
  }

  const parsed = new Date(value);

  return Number.isNaN(parsed.getTime())
    ? value
    : format(parsed, 'yyyy-MM-dd HH:mm:ss');
}

async function scrollToBottom() {
  await nextTick();

  const element = getLogContainerElement();

  if (!element) {
    return;
  }

  element.scrollTop = element.scrollHeight;
}

function getLogContainerElement() {
  const target = logContainer.value;

  if (!target) {
    return null;
  }

  if (target instanceof HTMLElement) {
    return target;
  }

  const element = target.$el;

  return element instanceof HTMLElement ? element : null;
}
</script>

<style scoped>
.logs-dialog-card {
  height: 52rem;
}

.logs-dialog-content {
  display: flex;
  min-height: 0;
  flex-direction: column;
  gap: 0.75rem;
}

.logs-dialog-toolbar {
  flex: 0 0 auto;
}

.logs-dialog-body {
  min-height: 0;
  flex: 1 1 auto;
  overflow: auto;
  background: rgb(10, 14, 20);
  color: rgb(230, 236, 241);
  font-family: Consolas, 'Courier New', monospace;
  font-size: 12px;
  line-height: 1.45;
  padding: 4px;
  border-radius: var(--radius-md);
}

.logs-output {
  display: flex;
  flex-direction: column;
}

.log-row {
  display: grid;
  grid-template-columns: max-content max-content minmax(0, 1fr);
  gap: 6px;
}

.log-timestamp,
.log-service {
  color: rgb(148, 163, 184);
  white-space: nowrap;
}

.log-message {
  white-space: pre-wrap;
  word-break: break-word;
}
</style>
