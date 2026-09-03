<template>
  <div class="monaco-field">
    <div v-if="label" class="monaco-field__label">
      {{ label }}
    </div>
    <div ref="container" class="monaco-editor-container" :style="{ height: `${height}px` }" />
  </div>
</template>

<script setup lang="ts">
import type { MonacoYaml, MonacoYamlOptions } from 'monaco-yaml';
import loader from '@monaco-editor/loader';
import { configureMonacoYaml } from 'monaco-yaml';
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { normalizeMonacoThemeColor } from '@/lib/monacoTheme';

interface MonacoTextModel {
  dispose: () => void;
}

interface MonacoEditor {
  getValue: () => string;
  setValue: (value: string) => void;
  getModel: () => MonacoTextModel | null;
  updateOptions: (options: Record<string, unknown>) => void;
  dispose: () => void;
  onDidChangeModelContent: (handler: () => void) => { dispose: () => void };
}

interface MonacoNamespace {
  Uri: {
    parse: (value: string) => unknown;
  };
  editor: {
    create: (element: HTMLElement, options: Record<string, unknown>) => MonacoEditor;
    createModel: (value: string, language?: string, uri?: unknown) => MonacoTextModel;
    setModelLanguage: (model: MonacoTextModel, language: string) => void;
    setTheme: (theme: string) => void;
    defineTheme: (name: string, themeData: Record<string, unknown>) => void;
  };
}

const props = withDefaults(
  defineProps<{
    modelValue: string;
    language?: string;
    label?: string;
    height?: number;
    fontSize?: number;
    readOnly?: boolean;
    modelUri?: string;
    yamlSchemaUri?: string;
  }>(),
  {
    language: 'plaintext',
    label: '',
    height: 280,
    fontSize: 14,
    readOnly: false,
    modelUri: '',
    yamlSchemaUri: '',
  },
);
const emit = defineEmits<{
  (event: 'update:modelValue', value: string): void;
}>();
const yamlSchemaEntries = new Map<string, string>();
let yamlConfiguration: MonacoYaml | null = null;
let monacoModelCounter = 0;

const container = ref<HTMLElement | null>(null);
const darkTheme = ref(isDarkThemeActive());
const themeObserver = new MutationObserver(() => {
  darkTheme.value = isDarkThemeActive();
});
const monacoTheme = computed(() => darkTheme.value ? 'quick-admin-dark' : 'quick-admin-light');

let monaco: MonacoNamespace | null = null;
let editor: MonacoEditor | null = null;
let modelChangeSubscription: { dispose: () => void } | null = null;
let updatingFromProps = false;
let createdModel: MonacoTextModel | null = null;
const resolvedModelUri = `inmemory://quickproxy/editor-${++monacoModelCounter}.${props.language || 'txt'}`;

onMounted(async () => {
  themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['class', 'data-theme'] });

  if (!container.value) {
    return;
  }

  monaco = (await loader.init()) as unknown as MonacoNamespace;
  applyAdminMonacoTheme(monaco);
  syncYamlSchemaConfiguration();
  createdModel = monaco.editor.createModel(
    props.modelValue ?? '',
    props.language,
    monaco.Uri.parse(activeModelUri()),
  );
  editor = monaco.editor.create(container.value, {
    model: createdModel,
    theme: monacoTheme.value,
    fontSize: props.fontSize,
    automaticLayout: true,
    minimap: { enabled: false },
    scrollBeyondLastLine: false,
    wordWrap: 'on',
    readOnly: props.readOnly,
  });

  modelChangeSubscription = editor.onDidChangeModelContent(() => {
    if (!editor || updatingFromProps) {
      return;
    }

    emit('update:modelValue', editor.getValue());
  });
});

watch(
  () => props.modelValue,
  (value) => {
    if (!editor) {
      return;
    }

    const currentValue = editor.getValue();

    if (currentValue === value) {
      return;
    }

    updatingFromProps = true;
    editor.setValue(value ?? '');
    updatingFromProps = false;
  },
);

watch(
  () => props.language,
  (value) => {
    if (!editor || !monaco) {
      return;
    }

    const model = editor.getModel();

    if (!model) {
      return;
    }

    monaco.editor.setModelLanguage(model, value || 'plaintext');
    syncYamlSchemaConfiguration();
  },
);

watch(
  () => [props.yamlSchemaUri, props.modelUri] as const,
  () => {
    syncYamlSchemaConfiguration();
  },
);

watch(monacoTheme, (value) => {
  if (!monaco) {
    return;
  }

  applyAdminMonacoTheme(monaco);
  monaco.editor.setTheme(value);
});

watch(
  () => props.readOnly,
  (value) => {
    if (!editor) {
      return;
    }

    editor.updateOptions({ readOnly: value });
  },
);

watch(
  () => props.fontSize,
  (value) => {
    if (!editor) {
      return;
    }

    editor.updateOptions({ fontSize: value || 14 });
  },
);

function applyAdminMonacoTheme(monacoInstance: MonacoNamespace) {
  const styles = getComputedStyle(document.documentElement);
  const isDark = darkTheme.value;
  const background = normalizeMonacoThemeColor(styles.getPropertyValue('--card'), isDark ? '#111827' : '#ffffff');
  const foreground = normalizeMonacoThemeColor(styles.getPropertyValue('--card-foreground'), isDark ? '#f8fafc' : '#0f172a');
  const border = normalizeMonacoThemeColor(styles.getPropertyValue('--border'), isDark ? '#334155' : '#d8dee9');
  const mutedForeground = normalizeMonacoThemeColor(styles.getPropertyValue('--muted-foreground'), isDark ? '#94a3b8' : '#64748b');
  const primary = normalizeMonacoThemeColor(styles.getPropertyValue('--primary'), isDark ? '#818cf8' : '#4f46e5');

  monacoInstance.editor.defineTheme('quick-admin-dark', {
    base: 'vs-dark',
    inherit: true,
    rules: [
      { token: 'comment', foreground: '94A3B8' },
      { token: 'delimiter', foreground: 'CBD5E1' },
      { token: 'keyword', foreground: '7DD3FC' },
      { token: 'number', foreground: 'F0ABFC' },
      { token: 'string', foreground: 'A5B4FC' },
      { token: 'tag', foreground: 'FCD34D' },
      { token: 'type', foreground: '6EE7B7' },
    ],
    colors: {
      'editor.background': background,
      'editor.foreground': foreground,
      'editor.lineHighlightBackground': '#182235',
      'editor.lineHighlightBorder': '#00000000',
      'editor.selectionBackground': '#334155',
      'editor.inactiveSelectionBackground': '#263449',
      'editorCursor.foreground': primary,
      'editorGutter.background': background,
      'editorIndentGuide.background1': border,
      'editorIndentGuide.activeBackground1': mutedForeground,
      'editorLineNumber.foreground': mutedForeground,
      'editorLineNumber.activeForeground': foreground,
    },
  });

  monacoInstance.editor.defineTheme('quick-admin-light', {
    base: 'vs',
    inherit: true,
    rules: [],
    colors: {
      'editor.background': background,
      'editor.foreground': foreground,
      'editor.lineHighlightBackground': '#00000000',
      'editor.lineHighlightBorder': '#00000000',
      'editorLineNumber.foreground': '#6b7280',
      'editorLineNumber.activeForeground': foreground,
    },
  });
}

function isDarkThemeActive() {
  return document.documentElement.classList.contains('dark')
    || document.documentElement.dataset.theme === 'dark';
}

onBeforeUnmount(() => {
  themeObserver.disconnect();
  unregisterYamlSchema();
  modelChangeSubscription?.dispose();
  editor?.dispose();
  createdModel?.dispose();
});

function activeModelUri() {
  return props.modelUri || resolvedModelUri;
}

function syncYamlSchemaConfiguration() {
  if (!monaco) {
    return;
  }

  unregisterYamlSchema();

  if (props.language !== 'yaml' || !props.yamlSchemaUri) {
    applyYamlConfiguration(monaco);
    return;
  }

  yamlSchemaEntries.set(activeModelUri(), props.yamlSchemaUri);
  applyYamlConfiguration(monaco);
}

function unregisterYamlSchema() {
  yamlSchemaEntries.delete(activeModelUri());

  if (monaco) {
    applyYamlConfiguration(monaco);
  }
}

function applyYamlConfiguration(monacoInstance: MonacoNamespace) {
  const options: MonacoYamlOptions = {
    enableSchemaRequest: true,
    validate: true,
    hover: true,
    completion: true,
    format: {},
    schemas: Array.from(yamlSchemaEntries.entries()).map(([fileMatch, uri]) => ({
      fileMatch: [fileMatch],
      uri,
    })),
  };

  if (!yamlConfiguration) {
    yamlConfiguration = configureMonacoYaml(monacoInstance as unknown as never, options);
    return;
  }

  void yamlConfiguration.update(options);
}
</script>

<style scoped>
.monaco-field {
  display: grid;
  gap: 0.375rem;
  width: 100%;
}

.monaco-field__label {
  color: var(--foreground);
  font-size: 0.875rem;
  line-height: 1.25rem;
  font-weight: 500;
}

.monaco-editor-container {
  border: 1px solid var(--border);
  border-radius: var(--radius);
  overflow: hidden;
  transition:
    border-color 150ms ease-out,
    box-shadow 150ms ease-out;
}

.monaco-editor-container:hover {
  border-color: color-mix(in srgb, var(--muted-foreground) 70%, var(--border));
}

.monaco-editor-container:focus-within {
  border-color: var(--ring);
  box-shadow: 0 0 0 3px color-mix(in srgb, var(--ring) 35%, transparent);
}
</style>
