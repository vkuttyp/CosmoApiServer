<script setup lang="ts">
import './assets/css/main.css'

const { loggedIn, user, clear: clearSession } = useUserSession()

async function logout() {
  await $fetch('/api/auth/logout', { method: 'POST' })
  await clearSession()
  await navigateTo('/login')
}
</script>

<template>
  <UApp>
    <div class="min-h-screen bg-default text-default">
      <header class="border-b border-default/80 backdrop-blur">
        <div class="mx-auto flex max-w-7xl items-center justify-between gap-6 px-6 py-4">
          <div>
            <p class="text-xs font-semibold uppercase tracking-[0.32em] text-muted">CosmoApiServer Sample</p>
            <h1 class="text-xl font-semibold">Nuxt UI Control Room</h1>
          </div>

          <div class="flex flex-wrap items-center gap-3">
            <nav class="flex items-center gap-2">
              <UButton color="neutral" variant="ghost" to="/">Dashboard</UButton>
              <UButton color="neutral" variant="ghost" to="/workspaces">Workspaces</UButton>
            </nav>

            <template v-if="loggedIn">
              <UBadge color="success" variant="soft">{{ user?.username }} ({{ user?.role }})</UBadge>
              <UButton color="neutral" variant="soft" size="sm" @click="logout">Sign out</UButton>
            </template>
            <template v-else>
              <UButton color="primary" variant="soft" size="sm" to="/login">Sign in</UButton>
            </template>

            <UBadge color="neutral" variant="subtle">Nuxt UI</UBadge>
            <UBadge color="primary" variant="soft">Cosmo API backend</UBadge>
          </div>
        </div>
      </header>

      <NuxtPage />
    </div>
  </UApp>
</template>
