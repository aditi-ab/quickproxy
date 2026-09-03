<template>
  <TooltipProvider>
    <ConfigProvider :scroll-body="false">
      <div class="app-shell">
        <div v-if="!identityState.initialized" class="grid min-h-dvh place-items-center">
          <Spinner />
        </div>
        <IdentitySignIn
          v-else-if="!identityState.authenticated" :api="identityApi" :status="identityState"
          product-name="QuickProxy" :product-icon-url="iconUrl" @authenticated="authenticated"
        />
        <template v-else>
          <header class="app-header h-28 border-b">
            <div class="flex h-full w-full flex-col">
              <div class="flex h-16 items-center px-5 md:px-8">
                <div class="app-product-icon">
                  <img :src="iconUrl" alt="" class="product-mark-image">
                </div>
                <div class="ml-3">
                  <div class="font-bold">
                    QuickProxy
                  </div><div class="text-xs text-muted-foreground">
                    {{ t('shell.console') }}
                  </div>
                </div>
                <div class="header-actions ml-auto">
                  <Button variant="ghost" as-child class="hidden sm:flex">
                    <a href="/docs/" target="_blank"><BookOpen />{{ t('shell.documentation') }}</a>
                  </Button>
                  <DropdownMenu :modal="false">
                    <DropdownMenuTrigger as-child>
                      <Button variant="secondary" class="language-button" :aria-label="t('shell.language')">
                        <Languages />{{ locale.toUpperCase() }}
                      </Button>
                    </DropdownMenuTrigger><DropdownMenuContent align="end">
                      <DropdownMenuRadioGroup :model-value="locale" @update:model-value="setLanguageValue">
                        <DropdownMenuRadioItem value="en">
                          English
                        </DropdownMenuRadioItem><DropdownMenuRadioItem value="sv">
                          Svenska
                        </DropdownMenuRadioItem>
                      </DropdownMenuRadioGroup>
                    </DropdownMenuContent>
                  </DropdownMenu>
                  <DropdownMenu :modal="false">
                    <DropdownMenuTrigger as-child>
                      <span class="inline-flex"><Tooltip><TooltipTrigger as-child>
                        <Button variant="outline" size="icon" :aria-label="t('shell.theme')"><SunMoon /></Button>
                      </TooltipTrigger><TooltipContent>{{ t('shell.theme') }}</TooltipContent></Tooltip></span>
                    </DropdownMenuTrigger><DropdownMenuContent align="end">
                      <DropdownMenuRadioGroup :model-value="themePreference" @update:model-value="setThemeValue">
                        <DropdownMenuRadioItem v-for="option in themeOptions" :key="option.value" :value="option.value">
                          <component :is="option.icon" />{{ option.label }}
                        </DropdownMenuRadioItem>
                      </DropdownMenuRadioGroup>
                    </DropdownMenuContent>
                  </DropdownMenu>
                  <DropdownMenu :modal="false">
                    <DropdownMenuTrigger as-child>
                      <Button variant="ghost">
                        <UserCircle />{{ identityState.username }}
                      </Button>
                    </DropdownMenuTrigger>
                    <DropdownMenuContent align="end">
                      <DropdownMenuItem @select="passwordDialog = true">
                        <KeyRound />{{ t('shell.changePassword') }}
                      </DropdownMenuItem>
                      <DropdownMenuItem @select="signOut">
                        <LogOut />{{ t('shell.signOut') }}
                      </DropdownMenuItem>
                    </DropdownMenuContent>
                  </DropdownMenu>
                </div>
              </div>
              <nav class="global-nav flex items-center px-4 md:px-7" :aria-label="t('shell.navigation')">
                <NavigationMenu :viewport="false" class="global-navigation-menu">
                  <NavigationMenuList>
                    <NavigationMenuItem v-for="item in primaryItems" :key="item.path">
                      <NavigationMenuLink as-child :active="active(item.path)" :class="navigationMenuTriggerStyle()">
                        <RouterLink :to="item.path" :aria-current="active(item.path) ? 'page' : undefined">
                          {{ item.label }}
                        </RouterLink>
                      </NavigationMenuLink>
                    </NavigationMenuItem>
                    <NavigationMenuItem v-for="group in navigationGroups" :key="group.label">
                      <NavigationMenuTrigger :class="{ 'bg-muted text-foreground': group.items.some(item => active(item.path)) }">
                        {{ group.label }}
                      </NavigationMenuTrigger>
                      <NavigationMenuContent class="min-w-56">
                        <ul class="grid gap-1">
                          <li v-for="item in group.items" :key="item.path">
                            <NavigationMenuLink as-child :active="active(item.path)">
                              <RouterLink :to="item.path" :aria-current="active(item.path) ? 'page' : undefined">
                                <component :is="item.icon" />{{ item.label }}
                              </RouterLink>
                            </NavigationMenuLink>
                          </li>
                        </ul>
                      </NavigationMenuContent>
                    </NavigationMenuItem>
                  </NavigationMenuList>
                </NavigationMenu>
              </nav>
            </div>
          </header>
          <main class="grow">
            <router-view v-slot="{ Component }">
              <transition name="route-view" mode="out-in">
                <div :key="route.fullPath" class="route-view">
                  <component :is="Component" />
                </div>
              </transition>
            </router-view>
          </main>
          <footer class="app-footer border-t">
            <div class="content-shell text-center">
              <a href="https://github.com/aditi-ab/quickproxy" target="_blank" rel="noopener noreferrer" class="text-primary">{{ t('shell.sourceCode') }}</a>
            </div>
          </footer>
        </template>

        <Dialog v-model:open="passwordDialog">
          <DialogContent
            size="md" :show-close-button="!identityState.mustChangePassword"
            @escape-key-down="identityState.mustChangePassword && $event.preventDefault()"
            @pointer-down-outside="identityState.mustChangePassword && $event.preventDefault()"
          >
            <DialogHeader>
              <DialogTitle>{{ t('shell.changePassword') }}</DialogTitle>
              <DialogDescription class="sr-only">
                Change the password for the current QuickProxy account.
              </DialogDescription>
            </DialogHeader>
            <Alert v-if="passwordError" variant="destructive">
              <CircleAlert /><AlertDescription>{{ passwordError }}</AlertDescription>
            </Alert>
            <FieldGroup class="dialog-body-content">
              <Field>
                <FieldLabel for="current-password">
                  {{ t('shell.currentPassword') }}
                </FieldLabel><Input id="current-password" v-model="currentPassword" type="password" autocomplete="current-password" />
              </Field>
              <Field>
                <FieldLabel for="new-password">
                  {{ t('shell.newPassword') }}
                </FieldLabel><Input id="new-password" v-model="newPassword" type="password" autocomplete="new-password" />
              </Field>
            </FieldGroup>
            <DialogFooter>
              <Button v-if="!identityState.mustChangePassword" variant="outline" @click="passwordDialog = false">
                {{ t('shell.cancel') }}
              </Button>
              <Button :disabled="passwordLoading || !currentPassword || !newPassword" @click="changePassword">
                <Spinner v-if="passwordLoading" />{{ t('shell.save') }}
              </Button>
            </DialogFooter>
          </DialogContent>
        </Dialog>
      </div>
    </ConfigProvider>
  </TooltipProvider>
</template>

<script setup lang="ts">
import type { AcceptableValue } from 'reka-ui';
import type { Component } from 'vue';
import type { ThemePreference } from '@/composables/themePreference';
import { IdentitySignIn } from '@aditify/identity';
import { Alert, AlertDescription, Button, ConfigProvider, Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle, DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuRadioGroup, DropdownMenuRadioItem, DropdownMenuTrigger, Field, FieldGroup, FieldLabel, Input, NavigationMenu, NavigationMenuContent, NavigationMenuItem, NavigationMenuLink, NavigationMenuList, NavigationMenuTrigger, navigationMenuTriggerStyle, Spinner, Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from '@aditify/ui';
import { BookOpen, CircleAlert, ClipboardClock, KeyRound, Languages, LogOut, Monitor, Moon, Settings, Sun, SunMoon, UserCircle, Users } from '@lucide/vue';
import { computed, onMounted, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { RouterLink, useRoute, useRouter } from 'vue-router';
import { identityApi, identityState, refreshIdentity } from '@/composables/identity';
import { initializeTheme, setThemePreference, themePreference } from '@/composables/themePreference';
import { useAppStore } from '@/stores/app';

const { locale, t } = useI18n({ messages: {
  en: { shell: { console: 'Management Console', documentation: 'Documentation', navigation: 'Primary navigation', language: 'Language', theme: 'Theme preference', system: 'System', light: 'Light', dark: 'Dark', changePassword: 'Change password', signOut: 'Sign out', overview: 'Overview', proxy: 'Proxy', containers: 'Containers', keyValues: 'KV Store', certificates: 'Certificates', administration: 'Administration', configuration: 'Configuration', audit: 'Audit', users: 'Users and providers', currentPassword: 'Current password', newPassword: 'New password', cancel: 'Cancel', save: 'Save', passwordError: 'Unable to change the password.', sourceCode: 'View QuickProxy on GitHub' } },
  sv: { shell: { console: 'Administrationskonsol', documentation: 'Dokumentation', navigation: 'Huvudnavigering', language: 'Språk', theme: 'Temainställning', system: 'System', light: 'Ljust', dark: 'Mörkt', changePassword: 'Byt lösenord', signOut: 'Logga ut', overview: 'Översikt', proxy: 'Proxy', containers: 'Containrar', keyValues: 'KV-lager', certificates: 'Certifikat', administration: 'Administration', configuration: 'Konfiguration', audit: 'Granskning', users: 'Användare och leverantörer', currentPassword: 'Nuvarande lösenord', newPassword: 'Nytt lösenord', cancel: 'Avbryt', save: 'Spara', passwordError: 'Det gick inte att byta lösenord.', sourceCode: 'Visa QuickProxy på GitHub' } },
} });
const route = useRoute();
const router = useRouter();
const app = useAppStore();
const passwordDialog = ref(false);
const currentPassword = ref('');
const newPassword = ref('');
const passwordError = ref('');
const passwordLoading = ref(false);
const iconUrl = `${import.meta.env.BASE_URL}quickproxy.svg`;
const primaryItems = computed(() => [
  { path: '/', label: t('shell.overview') },
  ...(app.proxyEnabled ? [{ path: '/proxy-hosts', label: t('shell.proxy') }] : []),
  ...(app.containersEnabled ? [{ path: '/containers', label: t('shell.containers') }] : []),
  ...(app.configEnabled ? [{ path: '/key-values', label: t('shell.keyValues') }] : []),
  ...(app.proxyEnabled ? [{ path: '/certificates', label: t('shell.certificates') }] : []),
]);
const navigationGroups = computed<Array<{ label: string; items: Array<{ path: string; label: string; icon: Component }> }>>(() => [{ label: t('shell.administration'), items: [
  ...(app.proxyEnabled ? [{ path: '/settings', label: t('shell.configuration'), icon: Settings }] : []),
  ...(app.auditEnabled ? [{ path: '/audit', label: t('shell.audit'), icon: ClipboardClock }] : []),
  { path: '/users', label: t('shell.users'), icon: Users },
] }]);
const themeOptions = computed<Array<{ label: string; value: ThemePreference; icon: Component }>>(() => [
  { label: t('shell.system'), value: 'system', icon: Monitor },
  { label: t('shell.light'), value: 'light', icon: Sun },
  { label: t('shell.dark'), value: 'dark', icon: Moon },
]);

function active(path: string) { return path === '/' ? route.path === '/' : route.path === path || route.path.startsWith(`${path}/`); }
function setLanguageValue(value: AcceptableValue) {
  if (typeof value === 'string')
    setLanguage(value);
}
function setThemeValue(value: AcceptableValue) {
  if (value === 'system' || value === 'light' || value === 'dark')
    setThemePreference(value);
}
onMounted(async () => {
  initializeTheme();

  const storedLocale = localStorage.getItem('quickproxy.locale');

  if (storedLocale === 'en' || storedLocale === 'sv')
    locale.value = storedLocale;

  await refreshIdentity();

  if (identityState.authenticated)
    await app.loadSystemInfo().catch(() => undefined);

  passwordDialog.value = Boolean(identityState.mustChangePassword);
});
async function authenticated() { await refreshIdentity(); await app.loadSystemInfo(true).catch(() => undefined); passwordDialog.value = Boolean(identityState.mustChangePassword); await router.replace('/'); }
async function signOut() { await identityApi.logout(); app.reset(); await refreshIdentity(); }
function setLanguage(value: string) { if (value === 'en' || value === 'sv') { locale.value = value; localStorage.setItem('quickproxy.locale', value); } }
async function changePassword() {
  passwordLoading.value = true; passwordError.value = '';

  try { await identityApi.changePassword(currentPassword.value, newPassword.value); currentPassword.value = ''; newPassword.value = ''; passwordDialog.value = false; await refreshIdentity(); }
  catch (error) { passwordError.value = error instanceof Error ? error.message : t('shell.passwordError'); }
  finally { passwordLoading.value = false; }
}
</script>
