import type { SystemInfo } from '@/composables/useSystemApi';
import { reactive } from 'vue';
import { useSystemApi } from '@/composables/useSystemApi';

const state = reactive<{ initialized: boolean; systemInfo: SystemInfo | null }>({ initialized: false, systemInfo: null });

export function useAppStore() {
  return {
    get initialized() { return state.initialized; },
    get systemInfo() { return state.systemInfo; },
    get proxyEnabled() { return state.systemInfo?.proxy.enabled ?? true; },
    get configEnabled() { return state.systemInfo?.config.enabled ?? true; },
    get auditEnabled() { return state.systemInfo?.audit.enabled ?? true; },
    get containersEnabled() { return state.systemInfo?.containers.enabled ?? true; },
    async loadSystemInfo(force = false) {
      if (state.initialized && state.systemInfo && !force)
        return state.systemInfo;

      state.systemInfo = await useSystemApi().getSystemInfo();
      state.initialized = true;
      return state.systemInfo;
    },
    reset() {
      state.initialized = false;
      state.systemInfo = null;
    },
  };
}
