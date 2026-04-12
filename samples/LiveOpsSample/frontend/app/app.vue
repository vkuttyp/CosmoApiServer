<template>
  <UApp>
    <div class="min-h-screen bg-gray-950 text-gray-100">
      <header class="border-b border-gray-800 px-6 py-4 flex items-center justify-between">
        <div class="flex items-center gap-3">
          <div class="w-2 h-2 rounded-full bg-emerald-400 animate-pulse" />
          <span class="text-lg font-semibold tracking-tight">Cosmo LiveOps</span>
        </div>
        <span class="text-xs text-gray-500 font-mono">{{ region }} · {{ env }}</span>
      </header>

      <main class="p-6">
        <NuxtPage />
      </main>
    </div>
  </UApp>
</template>

<script setup lang="ts">
const { data } = await useFetch<{ environment: string; region: string }>('/api/status')
const env    = computed(() => data.value?.environment ?? '—')
const region = computed(() => data.value?.region ?? '—')
</script>
