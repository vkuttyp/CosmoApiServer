<script setup lang="ts">
import type { WorkspaceDetail } from '~/types/control-room'

const config = useRuntimeConfig()
const route = useRoute()
const apiBase = config.public.apiBase
const slug = computed(() => String(route.params.slug || ''))

// Pass URL as a getter so useFetch re-runs when the slug changes (e.g. browser
// back/forward between workspace detail pages without a full page reload).
const { data, pending, error, refresh } = await useFetch<WorkspaceDetail>(
  () => `${apiBase}/api/workspaces/${slug.value}`,
  { key: `nuxt-ui-sample-workspace-${slug.value}` }
)

const statusTone = computed(() => {
  if (data.value?.status === 'Live') {
    return 'success'
  }

  if (data.value?.status === 'Review') {
    return 'warning'
  }

  return 'neutral'
})
</script>

<template>
  <main class="mx-auto flex max-w-6xl flex-col gap-8 px-6 py-8">
    <section class="flex flex-wrap items-end justify-between gap-4">
      <div class="space-y-2">
        <p class="text-xs font-semibold uppercase tracking-[0.32em] text-muted">Workspace Detail</p>
        <h2 class="text-3xl font-semibold tracking-tight sm:text-4xl">
          {{ data?.name ?? 'Loading workspace...' }}
        </h2>
        <p class="max-w-3xl text-sm leading-6 text-muted">
          Dynamic Nuxt route backed by a per-workspace Cosmo API endpoint.
        </p>
      </div>

      <div class="flex gap-2">
        <UButton color="neutral" variant="soft" :loading="pending" @click="refresh()">Refresh</UButton>
        <UButton to="/workspaces">Back to board</UButton>
      </div>
    </section>

    <div v-if="error" class="rounded-2xl border border-error/40 bg-error/10 px-5 py-4 text-sm text-error">
      Unable to load this workspace.
    </div>

    <section v-else class="grid gap-6 lg:grid-cols-[1.15fr_0.85fr]">
      <UCard>
        <template #header>
          <div class="flex flex-wrap items-start justify-between gap-4">
            <div>
              <p class="text-sm text-muted">{{ data?.region }} · {{ data?.members }} members</p>
              <h3 class="mt-1 text-2xl font-semibold">Operational summary</h3>
            </div>

            <UBadge :color="statusTone" variant="soft">
              {{ data?.status ?? 'Unknown' }}
            </UBadge>
          </div>
        </template>

        <div class="grid gap-4 sm:grid-cols-3">
          <div class="rounded-2xl border border-default bg-muted/30 p-4">
            <p class="text-sm text-muted">Health score</p>
            <p class="mt-2 text-3xl font-semibold">{{ data?.healthScore ?? '--' }}</p>
          </div>

          <div class="rounded-2xl border border-default bg-muted/30 p-4">
            <p class="text-sm text-muted">Owner</p>
            <p class="mt-2 text-lg font-semibold">{{ data?.owner ?? '--' }}</p>
          </div>

          <div class="rounded-2xl border border-default bg-muted/30 p-4">
            <p class="text-sm text-muted">Modules</p>
            <p class="mt-2 text-lg font-semibold">{{ data?.modules?.length ?? 0 }}</p>
          </div>
        </div>

        <template #footer>
          <div class="flex flex-wrap gap-2">
            <UBadge
              v-for="module in data?.modules ?? []"
              :key="module"
              color="neutral"
              variant="subtle"
            >
              {{ module }}
            </UBadge>
          </div>
        </template>
      </UCard>

      <UCard>
        <template #header>
          <div>
            <p class="text-sm text-muted">Routing surface</p>
            <h3 class="text-xl font-semibold">Nuxt dynamic route + Cosmo endpoint</h3>
          </div>
        </template>

        <div class="space-y-4 text-sm text-muted">
          <div class="rounded-2xl border border-default px-4 py-3">
            <p class="font-medium text-default">Page route</p>
            <p class="mt-1 font-mono">/workspaces/{{ slug }}</p>
          </div>

          <div class="rounded-2xl border border-default px-4 py-3">
            <p class="font-medium text-default">API route</p>
            <p class="mt-1 font-mono">/api/workspaces/{{ slug }}</p>
          </div>

          <p>
            This page demonstrates per-record fetching instead of a board-level aggregate response.
          </p>
        </div>
      </UCard>
    </section>

    <section>
      <UCard>
        <template #header>
          <div>
            <p class="text-sm text-muted">Deployment notes</p>
            <h3 class="text-xl font-semibold">Execution checklist</h3>
          </div>
        </template>

        <div class="space-y-4">
          <div
            v-for="note in data?.notes ?? []"
            :key="note.title"
            class="rounded-2xl border border-default px-4 py-4"
          >
            <div class="flex flex-wrap items-center justify-between gap-3">
              <p class="font-medium">{{ note.title }}</p>
              <UBadge color="primary" variant="outline">{{ note.state }}</UBadge>
            </div>
            <p class="mt-2 text-sm leading-6 text-muted">{{ note.description }}</p>
          </div>
        </div>
      </UCard>
    </section>
  </main>
</template>
