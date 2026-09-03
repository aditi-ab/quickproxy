<template>
  <Dialog
    :open="modelValue"
    :retain-focus="false"
    @update:open="emit('update:modelValue', $event)"
  >
    <DialogContent size="4xl" scrollable class="logs-dialog-card sm:max-w-6xl">
      <DialogHeader>
        <DialogTitle class="flex items-center gap-2 pr-8">
          <span class="truncate">Logs: {{ containerName }}</span>
          <span class="ml-auto" />
          <Badge :variant="connected ? 'success' : 'warning'">
            {{ connected ? 'Live' : 'Connecting' }}
          </Badge>
        </DialogTitle>
        <DialogDescription class="sr-only">
          Live output from the selected container.
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
            <Switch id="container-logs-auto-scroll" v-model="autoScroll" />
            <FieldLabel for="container-logs-auto-scroll">
              Auto-scroll
            </FieldLabel>
          </Field>
        </div>
        <Alert v-if="errorMessage" class="mt-3" variant="destructive">
          <CircleAlert /><AlertDescription>{{ errorMessage }}</AlertDescription>
        </Alert>
        <div ref="terminalHost" class="logs-dialog-body" />
      </div>
    </DialogContent>
  </Dialog>
</template>

<script lang="ts" setup>
import type { ComponentPublicInstance } from 'vue';
import type { ContainerLogEntry } from '@/composables/useContainersApi';

import { CircleAlert } from '@lucide/vue';
import { FitAddon } from '@xterm/addon-fit';
import { Terminal } from '@xterm/xterm';
import { format } from 'date-fns';
import { nextTick, onBeforeUnmount, ref, watch } from 'vue';
import { useContainersApi } from '@/composables/useContainersApi';
import '@xterm/xterm/css/xterm.css';

const props = defineProps<{
  modelValue: boolean;
  containerName: string;
}>();

const emit = defineEmits<{
  'update:modelValue': [value: boolean];
}>();

const api = useContainersApi();
const terminalHost = ref<HTMLElement | ComponentPublicInstance | null>(null);
const errorMessage = ref('');
const connected = ref(false);
const autoScroll = ref(true);

let abortController: AbortController | null = null;
let terminal: Terminal | null = null;
let fitAddon: FitAddon | null = null;
let resizeObserver: ResizeObserver | null = null;
let sessionVersion = 0;
let pendingWrites: string[] = [];
let flushFrameId: number | null = null;

watch(
  () => [props.modelValue, props.containerName] as const,
  async ([isOpen, containerName], previousState) => {
    const [wasOpen, previousContainerName] = previousState ?? [];

    if (!isOpen) {
      teardownSession();
      return;
    }

    if (!containerName) {
      teardownSession();
      return;
    }

    if (isOpen === wasOpen && containerName === previousContainerName && terminal) {
      return;
    }

    await restartStream();
  },
  { immediate: true },
);

onBeforeUnmount(() => {
  teardownSession();
});

async function restartStream() {
  teardownSession(false);

  const currentSessionVersion = ++sessionVersion;

  errorMessage.value = '';
  connected.value = false;

  if (!props.containerName) {
    return;
  }

  await nextTick();

  const host = getTerminalHostElement();

  if (!host) {
    return;
  }

  host.replaceChildren();
  terminal = new Terminal({
    allowProposedApi: false,
    convertEol: true,
    disableStdin: true,
    fontFamily: 'Consolas, "Courier New", monospace',
    fontSize: 12,
    scrollback: 5000,
    theme: {
      background: '#0a0e14',
      foreground: '#e6ecf1',
    },
  });
  fitAddon = new FitAddon();
  terminal.loadAddon(fitAddon);
  terminal.open(host);
  fitTerminal();
  scheduleStabilizedFit();
  terminal.writeln(`Connecting to ${props.containerName}...`);

  resizeObserver = new ResizeObserver(() => {
    fitTerminal();
  });
  resizeObserver.observe(host);

  abortController = new AbortController();

  try {
    for await (const entry of api.streamContainerLogs(props.containerName, abortController.signal)) {
      if (currentSessionVersion !== sessionVersion) {
        return;
      }

      connected.value = true;
      enqueueLogEntry(entry);
    }
  }
  catch (error) {
    if (isAbortError(error) || abortController?.signal.aborted) {
      return;
    }

    errorMessage.value = (error as Error).message;
    connected.value = false;
    terminal?.writeln(`\x1B[31m${errorMessage.value}\x1B[0m`);
  }
}

function teardownSession(invalidateSession = true) {
  if (invalidateSession) {
    sessionVersion++;
  }

  abortController?.abort();
  abortController = null;
  connected.value = false;
  pendingWrites = [];

  if (flushFrameId !== null) {
    cancelAnimationFrame(flushFrameId);
    flushFrameId = null;
  }

  resizeObserver?.disconnect();
  resizeObserver = null;
  fitAddon = null;
  terminal?.dispose();
  terminal = null;

  const host = getTerminalHostElement();

  host?.replaceChildren();
}

function clearLogs() {
  pendingWrites = [];

  if (flushFrameId !== null) {
    cancelAnimationFrame(flushFrameId);
    flushFrameId = null;
  }

  terminal?.clear();
}

function enqueueLogEntry(entry: ContainerLogEntry) {
  pendingWrites.push(formatLogEntry(entry));
  scheduleFlush();
}

function scheduleFlush() {
  if (flushFrameId !== null) {
    return;
  }

  flushFrameId = requestAnimationFrame(() => {
    flushFrameId = null;
    flushPendingWrites();
  });
}

function flushPendingWrites() {
  if (!terminal || pendingWrites.length === 0) {
    return;
  }

  terminal.write(pendingWrites.join(''));
  pendingWrites = [];

  if (autoScroll.value) {
    terminal.scrollToBottom();
  }
}

function fitTerminal() {
  fitAddon?.fit();
}

function scheduleStabilizedFit() {
  void Promise.resolve().then(async () => {
    await nextTick();
    fitTerminal();

    requestAnimationFrame(() => {
      fitTerminal();
    });

    window.setTimeout(() => {
      fitTerminal();
    }, 150);

    if ('fonts' in document) {
      await document.fonts.ready;
      fitTerminal();
    }
  });
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

function formatLogEntry(entry: ContainerLogEntry) {
  const timestamp = formatTimestamp(entry.timestamp);
  const message = entry.message.replace(/\r?\n$/, '');
  const prefix = timestamp ? `\x1B[90m${timestamp}\x1B[0m ` : '';
  const colorStart = entry.stream === 'stderr' ? '\x1B[33m' : '';
  const colorEnd = entry.stream === 'stderr' ? '\x1B[0m' : '';

  return `${prefix}${colorStart}${message}${colorEnd}\r\n`;
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

function getTerminalHostElement() {
  const target = terminalHost.value;

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
  border-radius: var(--radius-md);
  padding: 8px;
  overflow: hidden;
  background: #0a0e14;
}

.logs-dialog-body :deep(.xterm),
.logs-dialog-body :deep(.xterm *) {
  /* font-family: Consolas, "Courier New", monospace; */
  letter-spacing: normal;
  /* line-height: normal; */
  font-kerning: none;
  font-variant-ligatures: none;
  font-size: 0.875rem;
}
</style>
