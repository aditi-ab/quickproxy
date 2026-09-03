<template>
  <div class="page-container">
    <Alert v-if="errorMessage" class="mb-4" variant="destructive">
      <CircleAlert /><AlertDescription>{{ errorMessage }}</AlertDescription>
    </Alert>

    <header class="mb-6 flex flex-wrap items-end justify-between gap-4">
      <div>
        <div class="eyebrow">
          Administration
        </div><h1 class="page-title mt-1">
          Configuration
        </h1><p class="page-lead">
          Configure fallback responses, error pages, and diagnostics.
        </p>
      </div>
    </header>

    <Tabs v-model="activeTab">
      <TabsList>
        <TabsTrigger value="unknown-host">
          Unknown Host
        </TabsTrigger>
        <TabsTrigger value="error-pages">
          Error Pages
        </TabsTrigger>
        <TabsTrigger value="debug">
          Debug
        </TabsTrigger>
      </TabsList>

      <TabsContent class="mt-4" value="unknown-host">
        <Card>
          <CardHeader class="border-b">
            <CardTitle>Unknown host response</CardTitle>
            <CardDescription>
              Choose what to return when a request host does not match a configured proxy host. Upstream failures on
              known hosts still use the gateway error responses.
            </CardDescription>
          </CardHeader>
          <CardContent>
            <div class="grid grid-cols-12 gap-4">
              <div class="col-span-12 md:col-span-4">
                <Field>
                  <FieldLabel>Mode</FieldLabel>
                  <Select v-model="fallbackSettings.mode">
                    <SelectTrigger><SelectValue placeholder="Mode" /></SelectTrigger><SelectContent>
                      <SelectItem v-for="option in fallbackModes" :key="option" :value="option">
                        {{ fallbackModeLabels[option] }}
                      </SelectItem>
                    </SelectContent>
                  </Select>
                </Field>
              </div>
              <div class="col-span-12 md:col-span-4">
                <Field>
                  <FieldLabel>{{ fallbackSettings.mode === 'redirect' ? 'Redirect Status Code' : 'Status Code' }}</FieldLabel>
                  <Input v-model.number="fallbackSettings.statusCode" type="number" />
                </Field>
              </div>
              <div v-if="fallbackSettings.mode !== 'redirect'" class="col-span-12 md:col-span-4">
                <Field><FieldLabel>Content Type</FieldLabel><Input v-model="fallbackSettings.contentType" /></Field>
              </div>
              <div v-if="fallbackSettings.mode === 'htmlFile'" class="col-span-12">
                <Field>
                  <FieldLabel>HTML File Path (server relative)</FieldLabel>
                  <Input v-model="fallbackSettings.htmlFilePath" />
                  <FieldDescription>Used only for HTML file mode. Default mode uses the embedded not-found page.</FieldDescription>
                </Field>
              </div>
              <div v-if="fallbackSettings.mode === 'redirect'" class="col-span-12">
                <Field>
                  <FieldLabel>Redirect URL</FieldLabel>
                  <Input v-model="fallbackSettings.redirectUrl" />
                  <FieldDescription>Absolute URL, for example https://www.example.com/</FieldDescription>
                </Field>
              </div>
              <div class="col-span-12">
                <Field orientation="horizontal" class="w-fit items-center rounded-lg border px-3 py-2">
                  <Switch id="unknown-host-enabled" v-model="fallbackSettings.enabled" />
                  <FieldLabel for="unknown-host-enabled">
                    Enabled
                  </FieldLabel>
                </Field>
              </div>
            </div>
          </CardContent>
          <CardFooter>
            <span class="ml-auto" />
            <Button @click="saveSettings">
              Save
            </Button>
          </CardFooter>
        </Card>
      </TabsContent>
      <TabsContent class="mt-4" value="error-pages">
        <Card>
          <CardHeader class="border-b">
            <CardTitle>Gateway error responses</CardTitle>
            <CardDescription>
              Configure responses for matched upstream requests that fail. Default mode uses the embedded QuickProxy pages.
            </CardDescription>
          </CardHeader>
          <CardContent>
            <div class="grid grid-cols-1 gap-4 lg:grid-cols-2">
              <section class="overflow-hidden rounded-lg border">
                <h3 class="border-b px-4 py-3 text-base font-semibold">
                  502 Bad Gateway
                </h3>
                <div class="grid grid-cols-12 gap-4 p-4">
                  <div class="col-span-12 md:col-span-5">
                    <Field>
                      <FieldLabel>Mode</FieldLabel>
                      <Select v-model="fallbackSettings.badGatewayMode">
                        <SelectTrigger><SelectValue placeholder="Mode" /></SelectTrigger><SelectContent>
                          <SelectItem v-for="option in gatewayModes" :key="option" :value="option">
                            {{ gatewayModeLabels[option] }}
                          </SelectItem>
                        </SelectContent>
                      </Select>
                    </Field>
                  </div>
                  <div v-if="fallbackSettings.badGatewayMode === 'htmlFile'" class="col-span-12 md:col-span-7">
                    <Field><FieldLabel>HTML File Path</FieldLabel><Input v-model="fallbackSettings.badGatewayHtmlFilePath" /></Field>
                  </div>
                  <div v-if="fallbackSettings.badGatewayMode !== 'statusCode'" class="col-span-12">
                    <Field><FieldLabel>Content Type</FieldLabel><Input v-model="fallbackSettings.badGatewayContentType" /></Field>
                  </div>
                </div>
              </section>
              <section class="overflow-hidden rounded-lg border">
                <h3 class="border-b px-4 py-3 text-base font-semibold">
                  504 Gateway Timeout
                </h3>
                <div class="grid grid-cols-12 gap-4 p-4">
                  <div class="col-span-12 md:col-span-5">
                    <Field>
                      <FieldLabel>Mode</FieldLabel>
                      <Select v-model="fallbackSettings.gatewayTimeoutMode">
                        <SelectTrigger><SelectValue placeholder="Mode" /></SelectTrigger><SelectContent>
                          <SelectItem v-for="option in gatewayModes" :key="option" :value="option">
                            {{ gatewayModeLabels[option] }}
                          </SelectItem>
                        </SelectContent>
                      </Select>
                    </Field>
                  </div>
                  <div v-if="fallbackSettings.gatewayTimeoutMode === 'htmlFile'" class="col-span-12 md:col-span-7">
                    <Field><FieldLabel>HTML File Path</FieldLabel><Input v-model="fallbackSettings.gatewayTimeoutHtmlFilePath" /></Field>
                  </div>
                  <div v-if="fallbackSettings.gatewayTimeoutMode !== 'statusCode'" class="col-span-12">
                    <Field><FieldLabel>Content Type</FieldLabel><Input v-model="fallbackSettings.gatewayTimeoutContentType" /></Field>
                  </div>
                </div>
              </section>
            </div>
          </CardContent>
          <CardFooter>
            <span class="ml-auto" />
            <Button @click="saveSettings">
              Save
            </Button>
          </CardFooter>
        </Card>
      </TabsContent>
      <TabsContent class="mt-4" value="debug">
        <Card>
          <CardHeader class="border-b">
            <CardTitle>Proxy diagnostics</CardTitle>
            <CardDescription>
              Log request and response metadata for matched proxy-host traffic while diagnosing routing, redirects,
              forwarded headers, and host-header behavior.
            </CardDescription>
          </CardHeader>
          <CardContent class="space-y-4">
            <Field orientation="horizontal" class="w-fit items-center rounded-lg border px-3 py-2">
              <Switch id="proxy-debug-logging" v-model="fallbackSettings.proxyDebugLoggingEnabled" />
              <FieldLabel for="proxy-debug-logging">
                Enable proxy debug header logging
              </FieldLabel>
            </Field>
            <Alert class="border-amber-500/40 text-amber-700 dark:text-amber-300">
              <TriangleAlert /><AlertDescription>
                Logs include selected request and response headers for proxied requests. Avoid enabling this longer
                than necessary in shared environments.
              </AlertDescription>
            </Alert>
          </CardContent>
          <CardFooter>
            <span class="ml-auto" />
            <Button @click="saveSettings">
              Save
            </Button>
          </CardFooter>
        </Card>
      </TabsContent>
    </Tabs>

    <Alert v-if="showSavedSnackbar" class="fixed bottom-4 right-4 z-50 w-auto min-w-72 border-emerald-500/40 text-emerald-700 shadow-lg dark:text-emerald-300">
      <CircleCheck /><AlertDescription>Settings saved</AlertDescription>
    </Alert>
  </div>
</template>

<script lang="ts" setup>
import type { FallbackResponseMode, FallbackSettings, GatewayFallbackResponseMode } from '@/composables/useFallbackSettingsApi';
import { CircleAlert, CircleCheck, TriangleAlert } from '@lucide/vue';
import { onMounted, ref } from 'vue';
import { useFallbackSettingsApi } from '@/composables/useFallbackSettingsApi';

const errorMessage = ref('');
const showSavedSnackbar = ref(false);
const fallbackSettingsApi = useFallbackSettingsApi();
const fallbackModes: FallbackResponseMode[] = ['default', 'statusCode', 'htmlFile', 'redirect'];
const gatewayModes: GatewayFallbackResponseMode[] = ['default', 'statusCode', 'htmlFile'];
const fallbackModeLabels: Record<FallbackResponseMode, string> = {
  default: 'Default page',
  statusCode: 'Status code only',
  htmlFile: 'HTML file',
  redirect: 'Redirect',
};
const gatewayModeLabels: Record<GatewayFallbackResponseMode, string> = {
  default: 'Default page',
  statusCode: 'Status code only',
  htmlFile: 'HTML file',
};
const activeTab = ref<'unknown-host' | 'error-pages' | 'debug'>('unknown-host');
const fallbackSettings = ref<FallbackSettings>(defaultFallbackSettings());

onMounted(loadFallbackSettings);

async function loadFallbackSettings() {
  try {
    errorMessage.value = '';
    fallbackSettings.value = await fallbackSettingsApi.getSettings();
  }
  catch (error) {
    errorMessage.value = (error as Error).message;
  }
}

async function saveSettings() {
  try {
    errorMessage.value = '';
    await fallbackSettingsApi.updateSettings(fallbackSettings.value);
    await loadFallbackSettings();
    showSavedSnackbar.value = true;
  }
  catch (error) {
    errorMessage.value = (error as Error).message;
  }
}

function defaultFallbackSettings(): FallbackSettings {
  return {
    enabled: true,
    mode: 'default',
    statusCode: 404,
    htmlFilePath: 'ClientFallback/not-found.html',
    redirectUrl: '',
    contentType: 'text/html; charset=utf-8',
    badGatewayMode: 'default',
    badGatewayHtmlFilePath: 'ClientFallback/bad-gateway.html',
    badGatewayContentType: 'text/html; charset=utf-8',
    gatewayTimeoutMode: 'default',
    gatewayTimeoutHtmlFilePath: 'ClientFallback/gateway-timeout.html',
    gatewayTimeoutContentType: 'text/html; charset=utf-8',
    proxyDebugLoggingEnabled: false,
  };
}
</script>
