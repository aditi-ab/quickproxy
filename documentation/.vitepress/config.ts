import { defineConfig } from 'vitepress';

const base = process.env.DOCS_BASE ?? '/docs/';

export default defineConfig({
  title: 'QuickProxy Docs',
  description: 'Admin UI and provisioning documentation for QuickProxy.',
  base,
  outDir: process.env.DOCS_OUT_DIR ?? '../src/QuickProxy/wwwroot/docs',
  cleanUrls: true,
  head: [['link', { rel: 'icon', type: 'image/svg+xml', href: `${base}aditi-logo.svg` }]],
  themeConfig: {
    nav: [
      { text: 'Guide', link: '/getting-started' },
      { text: 'Provisioning', link: '/provisioning/overview' },
      { text: 'UI', link: '/ui/dashboard' },
      { text: 'Integrations', link: '/integrations/public-api-sdk' },
    ],
    sidebar: [
      {
        text: 'Getting Started',
        items: [
          { text: 'Introduction', link: '/index' },
          { text: 'Setup', link: '/getting-started' },
        ],
      },
      {
        text: 'UI Guide',
        items: [
          { text: 'Dashboard', link: '/ui/dashboard' },
          { text: 'Proxy Hosts', link: '/ui/proxy-hosts' },
          { text: 'Containers', link: '/ui/containers' },
          { text: 'Certificates and Issuers', link: '/ui/certificates' },
          { text: 'Key/Values', link: '/ui/key-values' },
          { text: 'Audit', link: '/ui/audit' },
          { text: 'Settings', link: '/ui/settings' },
          { text: 'Users', link: '/ui/users' },
          { text: 'Common Workflows', link: '/ui/workflows' },
        ],
      },
      {
        text: 'Integrations',
        items: [
          { text: 'Public API and SDK', link: '/integrations/public-api-sdk' },
        ],
      },
      {
        text: 'Provisioning',
        items: [
          { text: 'Overview', link: '/provisioning/overview' },
          { text: 'Schema', link: '/provisioning/schema' },
          { text: 'Examples', link: '/provisioning/examples' },
          { text: 'Troubleshooting', link: '/provisioning/troubleshooting' },
        ],
      },
    ],
    search: {
      provider: 'local',
    },
    socialLinks: [
      { icon: 'github', link: 'https://github.com/' },
    ],
  },
});
