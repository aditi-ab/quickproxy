<template>
  <Dialog
    :open="modelValue" @update:open="emit('update:modelValue', $event)"
  >
    <DialogContent size="4xl" scrollable>
      <DialogHeader>
        <DialogTitle>{{ isEdit ? 'Edit User' : 'Create User' }}</DialogTitle>
        <DialogDescription class="sr-only">
          Configure the user's identity, access, and account status.
        </DialogDescription>
      </DialogHeader>
      <CardContent class="dialog-body-content">
        <Alert v-if="saveError" class="mb-4" variant="destructive">
          {{ saveError }}
        </Alert>

        <div class="grid grid-cols-12 gap-4">
          <div class="col-span-12">
            <Field><FieldLabel>Email<Input v-model="localForm.email" :disabled="isEdit" type="email" /></FieldLabel></Field>
          </div>
          <div class="col-span-12">
            <Field><FieldLabel>Full Name (optional)<Input v-model="localForm.fullName" /></FieldLabel></Field>
          </div>
          <div class="col-span-12">
            <Field orientation="horizontal" class="rounded-lg border p-3">
              <FieldLabel for="user-enabled">
                Enabled
              </FieldLabel><Switch id="user-enabled" v-model="localForm.enabled" />
            </Field>
          </div>
          <div class="col-span-12" v-if="!isEdit || changePassword">
            <Field>
              <FieldLabel>
                {{ isEdit ? 'New Password' : 'Password' }}<Input
                  v-model="localForm.password" :type="showPassword ? 'text' : 'password'"

                  @click:append-inner="showPassword = !showPassword"
                />
              </FieldLabel><FieldDescription>At least 8 characters</FieldDescription>
            </Field>
          </div>
          <div class="col-span-12" v-if="isEdit">
            <Button variant="secondary" @click="togglePasswordChange">
              {{ changePassword ? 'Cancel Password Change' : 'Change Password' }}
            </Button>
          </div>
        </div>
      </CardContent>
      <Separator />
      <DialogFooter>
        <Button v-if="isEdit" @click="emit('delete', localForm.email.trim())" variant="destructive">
          Delete
        </Button>
        <span class="ml-auto" />
        <Button variant="ghost" @click="emit('update:modelValue', false)">
          Cancel
        </Button>
        <Button @click="save">
          Save
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>

<script lang="ts" setup>
import { ref, watch } from 'vue';

interface UserFormModel {
  email: string;
  fullName: string;
  enabled: boolean;
  password: string;
}

const props = defineProps<{
  modelValue: boolean;
  isEdit: boolean;
  saveError?: string;
  user: UserFormModel;
}>();

const emit = defineEmits<{
  'update:modelValue': [value: boolean];
  'delete': [email: string];
  'save': [value: { email: string; fullName?: string; enabled: boolean; password?: string; changePassword: boolean }];
}>();

const localForm = ref<UserFormModel>(clone(props.user));
const showPassword = ref(false);
const changePassword = ref(false);

watch(
  () => props.user,
  (value) => {
    localForm.value = clone(value);
    changePassword.value = false;
  },
  { deep: true, immediate: true },
);

function togglePasswordChange() {
  changePassword.value = !changePassword.value;

  if (!changePassword.value) {
    localForm.value.password = '';
  }
}

function save() {
  emit('save', {
    email: localForm.value.email.trim(),
    fullName: localForm.value.fullName.trim() || undefined,
    enabled: localForm.value.enabled,
    password: localForm.value.password,
    changePassword: changePassword.value || !props.isEdit,
  });
}

function clone(value: UserFormModel): UserFormModel {
  return JSON.parse(JSON.stringify(value)) as UserFormModel;
}
</script>
