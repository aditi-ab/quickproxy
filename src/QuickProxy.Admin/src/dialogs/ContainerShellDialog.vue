<template>
  <Dialog
    :open="modelValue" :retain-focus="false"
    @update:open="emit('update:modelValue', $event)"
  >
    <DialogContent size="4xl" scrollable class="shell-dialog-card">
      <DialogHeader>
        <DialogTitle class="flex items-center gap-2 pr-2">
          <span>Shell: {{ containerName }}</span>
          <span class="ml-auto" />
          <Badge :variant="connected ? 'success' : 'warning'">
            {{ connected ? 'Connected' : 'Connecting' }}
          </Badge>
          <Button variant="ghost" @click="emit('update:modelValue', false)">
            Close
          </Button>
        </DialogTitle>
        <DialogDescription class="sr-only">
          Run commands in an interactive shell for this container.
        </DialogDescription>
      </DialogHeader>
      <CardContent class="shell-dialog-toolbar p-3">
        <div class="flex items-center gap-2 flex-wrap">
          <Button variant="secondary" @click="restartSession">
            <RefreshCw />
            Reconnect
          </Button>
          <Button variant="ghost" @click="clearTerminal">
            <Eraser />
            Clear
          </Button>
        </div>
        <Alert v-if="errorMessage" class="mt-3" variant="destructive">
          {{ errorMessage }}
        </Alert>
      </CardContent>
      <Separator />
      <CardContent ref="terminalHost" class="shell-dialog-body" @click="focusTerminal" />
    </DialogContent>
  </Dialog>
</template>

<script lang="ts" setup>
import type { ComponentPublicInstance } from 'vue';
import { FitAddon } from '@xterm/addon-fit';

import { Terminal } from '@xterm/xterm';
import { nextTick, onBeforeUnmount, ref, watch } from 'vue';
import { INTERNAL_ADMIN_API_ROOT } from '@/composables/apiRoots';
import '@xterm/xterm/css/xterm.css';

const props = defineProps<{
  modelValue: boolean;
  containerName: string;
}>();

const emit = defineEmits<{
  'update:modelValue': [value: boolean];
}>();

interface ContainerShellServerMessage {
  type: 'output' | 'exit' | 'error';
  data?: string | null;
  message?: string | null;
}

const terminalHost = ref<HTMLElement | ComponentPublicInstance | null>(null);
const connected = ref(false);
const errorMessage = ref('');

let terminal: Terminal | null = null;
let fitAddon: FitAddon | null = null;
let socket: WebSocket | null = null;
let resizeObserver: ResizeObserver | null = null;
let sessionVersion = 0;

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

    await restartSession();
  },
  { immediate: true },
);

onBeforeUnmount(() => {
  teardownSession();
});

async function restartSession() {
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
    cursorBlink: true,
    fontFamily: 'Consolas, "Courier New", monospace',
    fontSize: 13,
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
  terminal.focus();
  terminal.writeln(`Connecting to ${props.containerName}...`);

  terminal.onData((data) => {
    sendMessage({
      type: 'input',
      data,
    });
  });

  resizeObserver = new ResizeObserver(() => {
    fitTerminal();
    sendResize();
  });
  resizeObserver.observe(host);

  socket = new WebSocket(buildShellUrl(props.containerName));
  socket.addEventListener('open', () => {
    if (currentSessionVersion !== sessionVersion) {
      return;
    }

    connected.value = true;
    terminal?.clear();
    fitTerminal();
    focusTerminal();
    sendResize();
  });
  socket.addEventListener('message', (event) => {
    if (currentSessionVersion !== sessionVersion) {
      return;
    }

    const payload = parseShellMessage(event.data);

    if (!payload) {
      return;
    }

    if (payload.type === 'output') {
      if (payload.data) {
        terminal?.write(payload.data);
      }

      return;
    }

    if (payload.type === 'error') {
      errorMessage.value = payload.message ?? 'Shell connection failed.';
      terminal?.writeln(`\r\n${errorMessage.value}`);
      return;
    }

    if (payload.message) {
      terminal?.writeln(`\r\n${payload.message}`);
    }
  });
  socket.addEventListener('close', () => {
    if (currentSessionVersion !== sessionVersion) {
      return;
    }

    connected.value = false;
  });
  socket.addEventListener('error', () => {
    if (currentSessionVersion !== sessionVersion) {
      return;
    }

    connected.value = false;
    errorMessage.value = errorMessage.value || 'WebSocket connection failed.';
  });
}

function teardownSession(invalidateSession = true) {
  if (invalidateSession) {
    sessionVersion++;
  }

  resizeObserver?.disconnect();
  resizeObserver = null;

  if (socket && (socket.readyState === WebSocket.OPEN || socket.readyState === WebSocket.CONNECTING)) {
    socket.close();
  }

  socket = null;
  connected.value = false;
  fitAddon = null;
  terminal?.dispose();
  terminal = null;

  const host = getTerminalHostElement();

  host?.replaceChildren();
}

function clearTerminal() {
  terminal?.clear();
  focusTerminal();
}

function sendResize() {
  if (!terminal) {
    return;
  }

  sendMessage({
    type: 'resize',
    cols: terminal.cols,
    rows: terminal.rows,
  });
}

function sendMessage(payload: { type: 'input' | 'resize'; data?: string; cols?: number; rows?: number }) {
  if (!socket || socket.readyState !== WebSocket.OPEN) {
    return;
  }

  socket.send(JSON.stringify(payload));
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

function focusTerminal() {
  terminal?.focus();
}

function buildShellUrl(containerName: string) {
  const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';

  return `${protocol}//${window.location.host}${INTERNAL_ADMIN_API_ROOT}/containers/${encodeURIComponent(containerName)}/shell/stream`;
}

function parseShellMessage(value: unknown): ContainerShellServerMessage | null {
  if (typeof value !== 'string') {
    return null;
  }

  try {
    return JSON.parse(value) as ContainerShellServerMessage;
  }
  catch {
    return null;
  }
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
.shell-dialog-card {
  /* max-height: min(92vh, 1100px); */
  display: flex;
  flex-direction: column;
}

.shell-dialog-toolbar {
  flex: 0 0 auto;
}

.shell-dialog-body {
  min-height: 520px;
  padding: 4px;
  overflow: hidden;
  background: #0a0e14;
}

.shell-dialog-body :deep(.xterm),
.shell-dialog-body :deep(.xterm *) {
  /* font-family: Consolas, "Courier New", monospace; */
  letter-spacing: normal;
  /* line-height: normal; */
  font-kerning: none;
  font-variant-ligatures: none;
  font-size: 0.875rem;
}
</style>
