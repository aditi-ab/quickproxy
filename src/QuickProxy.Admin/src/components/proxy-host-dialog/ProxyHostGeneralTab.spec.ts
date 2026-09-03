import type { ProxyHostConfig } from '@/composables/useProxyHostsApi';
import { mount } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import { nativeUiPlugin } from '@/plugins/nativeUi';
import ProxyHostGeneralTab from './ProxyHostGeneralTab.vue';

const form: ProxyHostConfig = {
  id: 'sample',
  mode: 'automaticContainer',
  enabled: true,
  domainNames: [],
  automaticContainer: {
    domainTemplates: ['{label.app}.example.test'],
    labelSelectors: [{
      key: 'custom.label',
      valuePattern: null,
      valuePatterns: [],
    }],
  },
  forceSsl: false,
  cacheAssets: false,
  websockets: true,
  routes: [],
  tls: { mode: 'none' },
};

describe('proxyHostGeneralTab', () => {
  it('uses the themed select for discovered label keys', () => {
    const wrapper = mount(ProxyHostGeneralTab, {
      global: {
        plugins: [nativeUiPlugin],
        stubs: { Teleport: true },
      },
      props: {
        localForm: form,
        isEdit: false,
        hostModeOptions: [
          { title: 'Manual', value: 'manual' },
          { title: 'Automatic', value: 'automaticContainer' },
        ],
        labelKeyOptions: ['app', 'maintainer'],
      },
    });

    expect(wrapper.find('datalist').exists()).toBe(false);

    const selectors = wrapper.findAll('[data-slot="select-trigger"]');

    expect(selectors).toHaveLength(2);

    expect(selectors[1]?.find('[data-slot="select-value"]').exists()).toBe(true);
  });
});
