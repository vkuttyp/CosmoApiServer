<script setup lang="ts">
import './assets/css/main.css'
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

            <UBadge color="neutral" variant="subtle">Nuxt UI</UBadge>
            <UBadge color="primary" variant="soft">Cosmo API backend</UBadge>
          </div>
        </div>
      </header>

      <NuxtPage />
    </div>
  </UApp>
</template>
