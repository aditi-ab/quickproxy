<template>
  <div class="grid grid-cols-12 gap-4">
    <div class="col-span-12 md:col-span-6">
      <Field>
        <FieldLabel>Host ID</FieldLabel><Input
          v-model="localForm.id" :disabled="isEdit"
        /><FieldDescription>lowercase kebab-case (example: docs-host)</FieldDescription>
      </Field>
    </div>
    <div class="col-span-12 md:col-span-6">
      <Field>
        <FieldLabel>Host Mode</FieldLabel><Select v-model="localForm.mode">
          <SelectTrigger><SelectValue placeholder="Host Mode" /></SelectTrigger><SelectContent>
            <SelectItem v-for="option in hostModeOptions" :key="String((typeof option === 'string' ? option : (option as any).value))" :value="(typeof option === 'string' ? option : (option as any).value)">
              {{ (typeof option === 'string' ? option : (option as any).title ?? (option as any).value) }}
            </SelectItem>
          </SelectContent>
        </Select>
      </Field>
    </div>

    <template v-if="localForm.mode === 'manual'">
      <div class="col-span-12">
        <Field>
          <FieldLabel>Domain Names</FieldLabel><TagsInput v-model="localForm.domainNames">
            <TagsInputItem v-for="value in localForm.domainNames" :key="value" :value="value">
              <TagsInputItemText /><TagsInputItemDelete />
            </TagsInputItem><TagsInputInput placeholder="Add an exact hostname" />
          </TagsInput><FieldDescription>Exact hostnames only, for example example.com or example.com:443.</FieldDescription>
        </Field>
      </div>
    </template>

    <template v-else>
      <div class="col-span-12">
        <Field>
          <FieldLabel>Domain Templates</FieldLabel><TagsInput v-model="localForm.automaticContainer.domainTemplates">
            <TagsInputItem v-for="value in localForm.automaticContainer.domainTemplates" :key="value" :value="value">
              <TagsInputItemText /><TagsInputItemDelete />
            </TagsInputItem><TagsInputInput placeholder="Add a domain template" />
          </TagsInput><FieldDescription>Use container, server, environment, or label placeholders.</FieldDescription>
        </Field>
      </div>
      <div class="col-span-12">
        <div class="flex items-center mb-2">
          <span class="text-base font-semibold">Label Selectors</span>
          <span class="ml-auto" />
          <Button size="sm" variant="secondary" @click="addLabelSelector">
            <Plus />
            Add Selector
          </Button>
        </div>
        <div class="grid grid-cols-12 gap-4" v-if="localForm.automaticContainer.labelSelectors.length > 0">
          <div
            class="col-span-12"
            v-for="(selector, index) in localForm.automaticContainer.labelSelectors" :key="`selector-${index}`"
          >
            <div class="items-center grid grid-cols-12 gap-4">
              <div class="col-span-12 md:col-span-4">
                <Field>
                  <FieldLabel>Label Key</FieldLabel><Select v-model="selector.key">
                    <SelectTrigger>
                      <SelectValue placeholder="Select a label key" />
                    </SelectTrigger><SelectContent>
                      <SelectItem v-for="option in availableLabelKeys(selector.key)" :key="option" :value="option">
                        {{ option }}
                      </SelectItem>
                    </SelectContent>
                  </Select><FieldDescription>The container must contain this label.</FieldDescription>
                </Field>
              </div>
              <div class="col-span-12 md:col-span-6">
                <Field>
                  <FieldLabel>Value Regexes</FieldLabel><TagsInput v-model="selector.valuePatterns">
                    <TagsInputItem v-for="value in selector.valuePatterns" :key="value" :value="value">
                      <TagsInputItemText /><TagsInputItemDelete />
                    </TagsInputItem><TagsInputInput placeholder="Add a regular expression" />
                  </TagsInput><FieldDescription>Optional. Any one matching expression is enough.</FieldDescription>
                </Field>
              </div>
              <div class="flex items-end justify-end col-span-12 md:col-span-2">
                <Button @click="removeLabelSelector(index)" variant="destructive">
                  Delete
                </Button>
              </div>
            </div>
          </div>
        </div>
      </div>
    </template>

    <div class="col-span-12">
      <div class="grid divide-y rounded-lg border">
        <Field orientation="horizontal" class="p-3">
          <FieldLabel for="host-enabled">
            Enabled
          </FieldLabel><Switch id="host-enabled" v-model="localForm.enabled" />
        </Field>
        <Field orientation="horizontal" class="p-3">
          <FieldLabel for="host-force-ssl">
            Force SSL
          </FieldLabel><Switch id="host-force-ssl" v-model="localForm.forceSsl" />
        </Field>
        <Field orientation="horizontal" class="p-3">
          <FieldLabel for="host-cache-assets">
            Cache assets
          </FieldLabel><Switch id="host-cache-assets" v-model="localForm.cacheAssets" />
        </Field>
        <Field orientation="horizontal" class="p-3">
          <FieldLabel for="host-websockets">
            WebSockets support
          </FieldLabel><Switch id="host-websockets" v-model="localForm.websockets" />
        </Field>
      </div>
    </div>
  </div>
</template>

<script lang="ts" setup>
import type { AutomaticContainerLabelSelector, ProxyHostConfig } from '@/composables/useProxyHostsApi';

const props = defineProps<{
  localForm: ProxyHostConfig;
  isEdit: boolean;
  hostModeOptions: Array<{ title: string; value: string }>;
  labelKeyOptions: string[];
}>();

function addLabelSelector() {
  props.localForm.automaticContainer.labelSelectors.push(emptyLabelSelector());
}

function removeLabelSelector(index: number) {
  if (index < 0) {
    return;
  }

  props.localForm.automaticContainer.labelSelectors.splice(index, 1);
}

function availableLabelKeys(currentKey: string) {
  return Array.from(new Set([
    ...(currentKey ? [currentKey] : []),
    ...props.labelKeyOptions,
  ])).sort((left, right) => left.localeCompare(right));
}

function emptyLabelSelector(): AutomaticContainerLabelSelector {
  return {
    key: '',
    valuePattern: null,
    valuePatterns: [],
  };
}
</script>
