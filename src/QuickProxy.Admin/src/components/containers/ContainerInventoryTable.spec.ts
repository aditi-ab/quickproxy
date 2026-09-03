import type { ContainerInventoryItem } from '@/composables/useContainersApi';
import { TooltipProvider } from '@aditify/ui';
import { mount } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import { h } from 'vue';
import { nativeUiPlugin } from '@/plugins/nativeUi';
import ContainerInventoryTable from './ContainerInventoryTable.vue';

const digest = `sha256:${'a'.repeat(64)}`;

const container: ContainerInventoryItem = {
  id: 'container-1',
  name: 'sample',
  image: 'example/sample:latest',
  imageId: 'image-1',
  imageDigest: digest,
  imageArchitecture: 'amd64',
  imageOs: 'linux',
  state: 'running',
  status: 'running',
  containerLabels: {},
  imageLabels: {},
  imageUpdate: {
    status: 'current',
    updateAvailable: false,
    localDigest: digest,
    remoteDigest: digest,
    remoteLabels: {},
  },
  ports: [],
  networks: [],
  compose: {},
  logsSupported: true,
  lastSeenUtc: '2026-08-29T12:00:00Z',
};

describe('containerInventoryTable', () => {
  it('renders long image digests in dedicated wrapping panels', async () => {
    const wrapper = mount({
      render: () => h(TooltipProvider, null, {
        default: () => h(ContainerInventoryTable, {
          items: [container],
          busyContainerName: '',
          busyAction: '',
        }),
      }),
    }, {
      global: { plugins: [nativeUiPlugin] },
    });

    await wrapper.get('button[aria-label="Expand details"]').trigger('click');

    const digestPanels = wrapper.findAll('.digest-metadata-item');
    const digestValues = wrapper.findAll('code.metadata-mono');

    expect(digestPanels).toHaveLength(2);
    expect(digestValues).toHaveLength(2);
    expect(digestValues.every(value => value.text() === digest)).toBe(true);
  });
});
