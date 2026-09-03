import { createRouter, createWebHistory } from 'vue-router';
import { identityState, refreshIdentity } from '@/composables/identity';
import { useAppStore } from '@/stores/app';

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    { path: '/', component: () => import('@/pages/index.vue') },
    { path: '/proxy-hosts', component: () => import('@/pages/proxy-hosts.vue') },
    { path: '/containers', component: () => import('@/pages/containers.vue') },
    { path: '/key-values', component: () => import('@/pages/key-values.vue') },
    { path: '/certificates', component: () => import('@/pages/certificates.vue') },
    { path: '/settings', component: () => import('@/pages/settings.vue') },
    { path: '/audit', component: () => import('@/pages/audit.vue') },
    { path: '/users', component: () => import('@/pages/users.vue') },
    { path: '/login', redirect: '/' },
    { path: '/issuers', redirect: '/certificates' },
  ],
});

router.beforeEach(async (to) => {
  if (!identityState.initialized)
    await refreshIdentity();

  if (!identityState.authenticated)
    return true;

  const app = useAppStore();

  await app.loadSystemInfo().catch(() => undefined);

  if (!app.proxyEnabled && ['/proxy-hosts', '/certificates', '/settings'].includes(to.path))
    return '/';

  if (!app.containersEnabled && to.path === '/containers')
    return '/';

  if (!app.configEnabled && to.path === '/key-values')
    return '/';

  if (!app.auditEnabled && to.path === '/audit')
    return '/';

  return true;
});

export default router;
