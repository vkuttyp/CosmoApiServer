<script setup lang="ts">
import { workspaceSlug, type WorkspaceDetailResponse } from '~/types/control-room'

const config = useRuntimeConfig()
const apiBase = config.public.apiBase

const { data, pending, refresh } = await useFetch<WorkspaceDetailResponse>(`${apiBase}/api/workspaces`, {
  key: 'nuxt-ui-sample-workspaces'
})

const statusTone = (status: string) => {
  if (status === 'Live') {
    return 'success'
  }

  if (status === 'Review') {
    return 'warning'
  }

  return 'neutral'
}
</script>

<template>
  <main class="mx-auto flex max-w-7xl flex-col gap-8 px-6 py-8">
    <section class="flex flex-wrap items-end justify-between gap-4">
      <div class="space-y-2">
        <p class="text-xs font-semibold uppercase tracking-[0.32em] text-muted">Workspace Board</p>
        <h2 class="text-3xl font-semibold tracking-tight sm:text-4xl">Deployment readiness by account</h2>
        <p class="max-w-3xl text-sm leading-6 text-muted">
          A second Nuxt UI page showing a richer account view, operational notes, and ownership data from the Cosmo backend.
        </p>
      </div>

      <div class="flex gap-2">
        <UButton color="neutral" variant="soft" :loading="pending" @click="refresh()">Refresh</UButton>
        <UButton to="/">Back to dashboard</UButton>
      </div>
    </section>

    <section class="grid gap-6 lg:grid-cols-3">
      <UCard
        v-for="workspace in data?.items ?? []"
        :key="workspace.name"
        class="h-full"
      >
        <template #header>
          <div class="space-y-4">
            <div class="flex items-start justify-between gap-3">
              <div>
                <p class="text-lg font-semibold">{{ workspace.name }}</p>
                <p class="text-sm text-muted">{{ workspace.region }} · {{ workspace.members }} members</p>
              </div>

              <UBadge :color="statusTone(workspace.status)" variant="soft">
                {{ workspace.status }}
              </UBadge>
            </div>

            <div class="rounded-2xl border border-default bg-muted/30 p-4">
              <p class="text-sm text-muted">Workspace health</p>
              <div class="mt-2 flex items-end justify-between gap-3">
                <p class="text-3xl font-semibold">{{ workspace.healthScore }}</p>
                <p class="text-sm text-muted">Owner: {{ workspace.owner }}</p>
              </div>
            </div>
          </div>
        </template>

        <div class="space-y-5">
          <div>
            <p class="mb-2 text-sm font-medium text-muted">Modules</p>
            <div class="flex flex-wrap gap-2">
              <UBadge
                v-for="module in workspace.modules"
                :key="module"
                color="neutral"
                variant="subtle"
              >
                {{ module }}
              </UBadge>
            </div>
          </div>

          <div class="space-y-3">
            <div
              v-for="note in workspace.notes"
              :key="`${workspace.name}-${note.title}`"
              class="rounded-2xl border border-default px-4 py-4"
            >
              <div class="flex items-center justify-between gap-3">
                <p class="font-medium">{{ note.title }}</p>
                <UBadge color="primary" variant="outline">{{ note.state }}</UBadge>
              </div>
              <p class="mt-2 text-sm leading-6 text-muted">{{ note.description }}</p>
            </div>
          </div>
        </div>

        <template #footer>
          <div class="flex justify-end">
            <UButton
              color="primary"
              variant="soft"
              :to="`/workspaces/${workspaceSlug(workspace.name)}`"
            >
              Open detail view
            </UButton>
          </div>
        </template>
      </UCard>
    </section>
  </main>
</template>
