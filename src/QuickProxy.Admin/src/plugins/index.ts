import type { App } from 'vue';
import router from '../router';
import i18n from './i18n';
import { registerNativeUi } from './nativeUi';

export function registerPlugins(app: App) {
  registerNativeUi(app);
  app.use(i18n);
  app.use(router);
}
