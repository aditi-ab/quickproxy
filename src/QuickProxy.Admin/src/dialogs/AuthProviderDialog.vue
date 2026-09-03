<template>
  <Dialog
    :open="modelValue"
    @update:open="emit('update:modelValue', $event)"
  >
    <DialogContent size="4xl" scrollable>
      <DialogHeader>
        <DialogTitle>{{ isEdit ? 'Edit Auth Provider' : 'Create Auth Provider' }}</DialogTitle>
        <DialogDescription class="sr-only">
          Configure authentication provider settings and access rules.
        </DialogDescription>
      </DialogHeader>
      <CardContent class="dialog-body-content">
        <Alert v-if="error" class="mb-4" variant="destructive">
          {{ error }}
        </Alert>
        <Alert v-if="testMessage" class="mb-4">
          {{ testMessage }}
        </Alert>
        <div class="flex gap-4 flex-wrap mb-4">
          <Field>
            <FieldLabel>
              Id<Input
                :model-value="localForm.id"
                style="min-width: 220px" :disabled="isEdit" @update:model-value="updateProviderId"
              />
            </FieldLabel>
          </Field>
          <Field>
            <FieldLabel>
              Display Name<Input
                :model-value="localForm.displayName"
                style="min-width: 280px" @update:model-value="updateProviderDisplayName"
              />
            </FieldLabel>
          </Field>
        </div>
        <div class="flex gap-4 flex-wrap mb-4">
          <Field orientation="horizontal">
            <FieldLabel>Enabled<Switch v-model="localForm.enabled" /></FieldLabel>
          </Field>
          <Field orientation="horizontal">
            <FieldLabel>Allow Auto Access<Switch v-model="localForm.allowAutoAccess" /></FieldLabel>
          </Field>
        </div>
        <ButtonGroup class="mb-4">
          <Button :variant="localForm.type === 'ldap' ? 'default' : 'outline'" @click="localForm.type = 'ldap'">
            LDAP
          </Button>
          <Button :variant="localForm.type === 'oidc' ? 'default' : 'outline'" @click="localForm.type = 'oidc'">
            OIDC
          </Button>
        </ButtonGroup>

        <div v-if="localForm.type === 'ldap'" class="flex flex-col gap-4">
          <div class="flex gap-4 flex-wrap">
            <Field>
              <FieldLabel>
                Server<Input
                  v-model="localForm.ldap.server"
                  style="min-width: 220px"
                />
              </FieldLabel>
            </Field>
            <Field>
              <FieldLabel>
                Port<Input
                  v-model.number="localForm.ldap.port" type="number"
                  style="max-width: 140px"
                />
              </FieldLabel>
            </Field>
            <Field orientation="horizontal">
              <FieldLabel>
                Use SSL<Switch
                  :model-value="localForm.ldap.useSsl"
                  @update:model-value="updateLdapUseSsl"
                />
              </FieldLabel>
            </Field>
          </div>
          <Field><FieldLabel>Bind DN<Input v-model="localForm.ldap.bindDn" /></FieldLabel></Field>
          <Field>
            <FieldLabel>
              Bind Password<Input
                v-model="localForm.ldap.bindPassword" type="password"

                :placeholder="localForm.ldap.hasBindPassword ? 'Leave blank to keep current password' : ''"
              />
            </FieldLabel>
          </Field>
          <Field orientation="horizontal">
            <FieldLabel>Clear stored bind password<Checkbox v-model="localForm.ldap.clearBindPassword" /></FieldLabel>
          </Field>
          <Field><FieldLabel>Base DN<Input v-model="localForm.ldap.baseDn" /></FieldLabel></Field>
          <Field>
            <FieldLabel>
              User Filter<Input
                v-model="localForm.ldap.userFilter"
              />
            </FieldLabel>
          </Field>
          <div class="flex gap-4 flex-wrap">
            <Field>
              <FieldLabel>
                Email Attribute<Input
                  v-model="localForm.ldap.emailAttribute"
                  style="min-width: 220px"
                />
              </FieldLabel>
            </Field>
            <Field>
              <FieldLabel>
                Full Name Attribute<Input
                  v-model="localForm.ldap.fullNameAttribute"
                  style="min-width: 220px"
                />
              </FieldLabel>
            </Field>
          </div>
        </div>

        <div v-else class="flex flex-col gap-4">
          <Field>
            <FieldLabel>
              Discovery Endpoint<Input
                :model-value="localForm.oidc.metadataUrl"

                @update:model-value="updateOidcDiscoveryEndpoint"
              />
            </FieldLabel><FieldDescription>OpenID Connect discovery document URL</FieldDescription>
          </Field>
          <div class="flex gap-4 flex-wrap">
            <Field>
              <FieldLabel>
                Client Id<Input
                  v-model="localForm.oidc.clientId"
                  style="min-width: 260px"
                />
              </FieldLabel>
            </Field>
            <Field>
              <FieldLabel>
                Client Secret<Input
                  v-model="localForm.oidc.clientSecret" type="password"
                  style="min-width: 260px"
                  :placeholder="localForm.oidc.hasClientSecret ? 'Leave blank to keep current secret' : ''"
                />
              </FieldLabel>
            </Field>
          </div>
          <Field orientation="horizontal">
            <FieldLabel>Clear stored client secret<Checkbox v-model="localForm.oidc.clearClientSecret" /></FieldLabel>
          </Field>
          <Field><FieldLabel>Scopes<Input v-model="localForm.oidc.scopes" /></FieldLabel></Field>
          <div class="flex gap-4 flex-wrap">
            <Field>
              <FieldLabel>
                Email Claim<Input
                  v-model="localForm.oidc.emailClaim"
                  style="min-width: 180px"
                />
              </FieldLabel>
            </Field>
            <Field>
              <FieldLabel>
                Name Claim<Input
                  v-model="localForm.oidc.nameClaim"
                  style="min-width: 180px"
                />
              </FieldLabel>
            </Field>
            <Field>
              <FieldLabel>
                Subject Claim<Input
                  v-model="localForm.oidc.subjectClaim"
                  style="min-width: 180px"
                />
              </FieldLabel>
            </Field>
          </div>
          <Field orientation="horizontal">
            <FieldLabel>Use PKCE<Switch v-model="localForm.oidc.usePkce" /></FieldLabel>
          </Field>
        </div>
      </CardContent>
      <Separator />
      <DialogFooter>
        <Button
          v-if="isEdit"
          @click="emit('delete', localForm.id)" variant="destructive"
        >
          Delete
        </Button>
        <span class="ml-auto" />
        <Button variant="secondary" @click="emit('test', clone(localForm))">
          Test
        </Button>
        <Button variant="ghost" @click="emit('update:modelValue', false)">
          Cancel
        </Button>
        <Button @click="emit('save', clone(localForm))">
          Save
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>

<script lang="ts" setup>
import { ref, watch } from 'vue';

interface ProviderFormModel {
  id: string;
  displayName: string;
  enabled: boolean;
  allowAutoAccess: boolean;
  type: 'ldap' | 'oidc';
  ldap: {
    server: string;
    port: number;
    useSsl: boolean;
    bindDn: string;
    bindPassword: string;
    clearBindPassword: boolean;
    hasBindPassword: boolean;
    baseDn: string;
    userFilter: string;
    emailAttribute: string;
    fullNameAttribute: string;
  };
  oidc: {
    authority: string;
    metadataUrl: string;
    clientId: string;
    clientSecret: string;
    clearClientSecret: boolean;
    hasClientSecret: boolean;
    scopes: string;
    emailClaim: string;
    nameClaim: string;
    subjectClaim: string;
    usePkce: boolean;
  };
}

const props = defineProps<{
  modelValue: boolean;
  isEdit: boolean;
  form: ProviderFormModel;
  error?: string;
  testMessage?: string;
  testSucceeded: boolean;
}>();

const emit = defineEmits<{
  'update:modelValue': [value: boolean];
  'delete': [id: string];
  'save': [value: ProviderFormModel];
  'test': [value: ProviderFormModel];
}>();

const localForm = ref<ProviderFormModel>(clone(props.form));
const providerIdTouched = ref(false);
const lastAutoGeneratedProviderId = ref('');

watch(
  () => props.form,
  (value) => {
    localForm.value = clone(value);
    providerIdTouched.value = props.isEdit;
    lastAutoGeneratedProviderId.value = '';
  },
  { deep: true, immediate: true },
);

function updateLdapUseSsl(value: boolean | null) {
  const nextValue = value === true;
  const previousPort = localForm.value.ldap.port;

  localForm.value.ldap.useSsl = nextValue;

  if (nextValue && previousPort === 389) {
    localForm.value.ldap.port = 636;
  }
  else if (!nextValue && previousPort === 636) {
    localForm.value.ldap.port = 389;
  }
}

function updateOidcDiscoveryEndpoint(value: string | null) {
  localForm.value.oidc.metadataUrl = value?.trim() ?? '';
  localForm.value.oidc.authority = '';
}

function updateProviderId(value: string | null) {
  const normalizedValue = toKebabCase(value ?? '');

  localForm.value.id = normalizedValue;
  providerIdTouched.value = normalizedValue.length > 0;
  lastAutoGeneratedProviderId.value = '';
}

function updateProviderDisplayName(value: string | null) {
  const nextDisplayName = value ?? '';

  localForm.value.displayName = nextDisplayName;

  if (props.isEdit) {
    return;
  }

  const nextAutoId = toKebabCase(nextDisplayName);
  const currentId = localForm.value.id;
  const shouldSyncId
    = !providerIdTouched.value
      || currentId.length === 0
      || currentId === lastAutoGeneratedProviderId.value;

  if (!shouldSyncId) {
    return;
  }

  localForm.value.id = nextAutoId;
  lastAutoGeneratedProviderId.value = nextAutoId;
}

function toKebabCase(value: string) {
  return value
    .normalize('NFKD')
    .replace(/[\u0300-\u036F]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .replace(/-{2,}/g, '-');
}

function clone(value: ProviderFormModel): ProviderFormModel {
  return JSON.parse(JSON.stringify(value)) as ProviderFormModel;
}
</script>
