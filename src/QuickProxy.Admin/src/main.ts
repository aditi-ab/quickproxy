import { createApp } from 'vue';
import { installAuthenticatedFetch } from '@/composables/identity';
import { registerPlugins } from '@/plugins';
import { initializeMonaco } from '@/plugins/monaco';
import App from './App.vue';
import '@fontsource-variable/inter';
import '@aditify/identity/styles.css';
import '@xterm/xterm/css/xterm.css';
import './styles.css';

initializeMonaco();
installAuthenticatedFetch();

const app = createApp(App);

registerPlugins(app);
app.mount('#app');
