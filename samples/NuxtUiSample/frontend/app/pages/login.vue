<script setup lang="ts">
definePageMeta({ auth: false })

const { fetch: refreshSession } = useUserSession()

const form = reactive({ username: '', password: '' })
const error = ref('')
const loading = ref(false)

async function login() {
  error.value = ''
  loading.value = true

  try {
    await $fetch('/api/auth/login', {
      method: 'POST',
      body: form
    })
    await refreshSession()
    await navigateTo('/')
  } catch (err: any) {
    error.value = err.data?.statusMessage || 'Login failed.'
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <main class="mx-auto flex min-h-[60vh] max-w-sm flex-col items-center justify-center px-6 py-12">
    <UCard class="w-full">
      <template #header>
        <h2 class="text-xl font-semibold">Sign in</h2>
        <p class="mt-1 text-sm text-muted">Demo credentials: admin / admin123</p>
      </template>

      <form class="space-y-4" @submit.prevent="login">
        <UFormField label="Username" name="username">
          <UInput v-model="form.username" autocomplete="username" class="w-full" />
        </UFormField>

        <UFormField label="Password" name="password">
          <UInput v-model="form.password" type="password" autocomplete="current-password" class="w-full" />
        </UFormField>

        <div v-if="error" class="rounded-lg border border-error/50 bg-error/10 px-3 py-2 text-sm text-error">
          {{ error }}
        </div>

        <UButton type="submit" :loading="loading" block>
          Sign in
        </UButton>
      </form>
    </UCard>
  </main>
</template>
