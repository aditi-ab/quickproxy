<template>
  <div class="page-container">
    <IdentityManagement :api="identityApi" :format-date-time="formatDateTime" />
  </div>
</template>

<script setup lang="ts">
import { IdentityManagement } from '@aditify/identity';
import { format } from 'date-fns';
import { identityApi } from '@/composables/identity';

function formatDateTime(value: string) {
  return format(new Date(value), 'yyyy-MM-dd HH:mm:ss');
}
</script>
