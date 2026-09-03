import { mount } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import { nativeUiPlugin } from '@/plugins/nativeUi';
import KeyValuesEntriesCard from './KeyValuesEntriesCard.vue';

describe('keyValuesEntriesCard', () => {
  it('keeps the entries table rendered while the editor dialog is closed', () => {
    const wrapper = mount(KeyValuesEntriesCard, {
      global: { plugins: [nativeUiPlugin] },
      props: {
        loadingSelectedEntry: false,
        loadingEntries: false,
        isDragOverKeys: false,
        selectedFolderSegments: [{ name: 'test', path: 'test/', type: 'folder' }],
        editDialog: false,
        selectedTableKeys: [],
        visibleEntries: [{
          key: 'test/appsettings.json',
          kind: 'key',
          selectable: true,
          source: 'local',
          entryType: 'data',
          payloadKind: 'text',
          labels: [],
          updatedAtUtc: '2026-08-25T12:00:00Z',
        }],
        currentFolderReadOnly: false,
        editingExisting: false,
        savingEditor: false,
        revealingSecret: false,
        editorErrorMessage: '',
        editorForm: {
          key: '',
          value: '',
          binaryBase64: '',
          mediaType: '',
          entryType: 'data',
          payloadKind: 'text',
          labels: [],
          isRevealed: false,
        },
        editorLanguage: 'json',
        editorReadOnly: false,
        editorSource: '',
        editorHasLocalOverride: false,
        editorHasChanges: false,
        showRevisionHistoryAction: false,
        loadingRevisionHistory: false,
        editorSelectedSource: '',
        editorAvailableSources: [],
        getDisplayKey: (key) => {
          const parts = key.split('/');

          return parts[parts.length - 1] ?? key;
        },
        getRowProps: () => ({ class: '' }),
      },
    });

    expect(wrapper.text()).toContain('Entries');
    expect(wrapper.get('.up-directory-row').text()).toBe('..');
    expect(wrapper.get('.up-directory-row').attributes('aria-label')).toBe('Open parent folder');
    expect(wrapper.text()).toContain('appsettings.json');
    expect(wrapper.find('[data-slot="table"]').exists()).toBe(true);

    const dataCells = wrapper.findAll('tbody tr')[1]?.findAll('td') ?? [];

    expect(dataCells[1]?.text()).toBe('appsettings.json');
    expect(dataCells[2]?.text()).toContain('Data');
    expect(dataCells[2]?.text()).toContain('Text');
  });
});
