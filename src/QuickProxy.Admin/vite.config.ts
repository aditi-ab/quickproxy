import { fileURLToPath, URL } from 'node:url';
import tailwindcss from '@tailwindcss/vite';
import vue from '@vitejs/plugin-vue';
import { defineConfig, loadEnv } from 'vite';

export default defineConfig(({ mode }) => {
  const environment = loadEnv(mode, process.cwd(), '');
  const adminUrl = environment.QUICKPROXY_ADMIN_URL || 'http://localhost:9000';

  return {
    base: '/admin/',
    plugins: [tailwindcss(), vue()],
    resolve: { alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) }, dedupe: ['vue', 'reka-ui', '@lucide/vue'] },
    build: { outDir: '../QuickProxy/wwwroot/admin', emptyOutDir: true },
    server: { port: 3000, proxy: { '/api': { target: adminUrl, changeOrigin: true, secure: false, ws: true }, '/admin/auth': { target: adminUrl, changeOrigin: true, secure: false }, '/admin/identity': { target: adminUrl, changeOrigin: true, secure: false } } },
    test: { environment: 'jsdom' },
  };
});
