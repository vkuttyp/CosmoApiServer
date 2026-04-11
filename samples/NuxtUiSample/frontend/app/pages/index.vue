<script setup lang="ts">
import type { DashboardResponse, FeedbackResponse } from '~/types/control-room'

const config = useRuntimeConfig()
const apiBase = config.public.apiBase

const form = reactive({
  workspace: 'Northwind Retail',
  message: 'Push the workspace insights panel into the next release cut.'
})

const submission = ref<FeedbackResponse | null>(null)
const submitting = ref(false)

const { data: health } = await useFetch<{ status: string, server: string, sample: string }>(`${apiBase}/api/health`, {
  key: 'nuxt-ui-sample-health'
})

const { data: summary } = await useFetch('/api/summary', {
  key: 'nuxt-ui-sample-summary'
})

const {
  data: dashboard,
  pending,
  error,
  refresh
} = await useFetch<DashboardResponse>(`${apiBase}/api/dashboard`, {
  key: 'nuxt-ui-sample-dashboard'
})

async function submitFeedback() {
  submitting.value = true

  try {
    submission.value = await $fetch<FeedbackResponse>(`${apiBase}/api/feedback`, {
      method: 'POST',
      body: form
    })
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <main class="mx-auto flex max-w-7xl flex-col gap-8 px-6 py-8">
    <section class="grid gap-6 lg:grid-cols-[1.5fr_1fr]">
      <UCard class="overflow-hidden">
        <template #header>
          <div class="flex flex-wrap items-start justify-between gap-4">
            <div class="space-y-3">
              <div class="flex flex-wrap items-center gap-2">
                <UBadge color="primary" variant="soft">{{ dashboard?.releaseChannel ?? 'Loading' }}</UBadge>
                <UBadge color="neutral" variant="subtle">{{ health?.sample ?? 'NuxtUiSample' }}</UBadge>
              </div>

              <div class="space-y-2">
                <h2 class="text-3xl font-semibold tracking-tight sm:text-4xl">
                  {{ dashboard?.productName ?? 'Loading dashboard...' }}
                </h2>
                <p class="max-w-2xl text-sm leading-6 text-muted">
                  A product-style sample app using Nuxt UI on the frontend while CosmoApiServer stays focused on backend transport and JSON APIs.
                </p>
              </div>
            </div>

            <div class="flex flex-wrap gap-2">
              <UButton color="neutral" variant="soft" :loading="pending" @click="refresh()">Refresh data</UButton>
              <UButton color="primary" variant="subtle" to="/workspaces">Open workspace board</UButton>
              <UButton to="https://ui.nuxt.com" target="_blank">Nuxt UI Docs</UButton>
            </div>
          </div>
        </template>

        <div class="grid gap-4 sm:grid-cols-3">
          <div
            v-for="metric in dashboard?.metrics ?? []"
            :key="metric.title"
            class="rounded-2xl border border-default bg-muted/30 p-4"
          >
            <p class="text-sm text-muted">{{ metric.title }}</p>
            <p class="mt-3 text-3xl font-semibold">{{ metric.value }}</p>
            <p class="mt-2 text-sm text-toned">{{ metric.caption }}</p>
          </div>
        </div>
      </UCard>

      <UCard>
        <template #header>
          <div class="space-y-2">
            <p class="text-sm font-medium text-muted">Runtime snapshot</p>
            <h3 class="text-xl font-semibold">Health and release signal</h3>
          </div>
        </template>

        <div class="space-y-4">
          <div class="flex items-center justify-between rounded-2xl border border-default px-4 py-3">
            <span class="text-sm text-muted">Backend status</span>
            <UBadge :color="health?.status === 'ok' ? 'success' : 'error'" variant="soft">
              {{ health?.status ?? 'unknown' }}
            </UBadge>
          </div>

          <div class="flex items-center justify-between rounded-2xl border border-default px-4 py-3">
            <span class="text-sm text-muted">Release channel</span>
            <span class="font-medium">{{ dashboard?.releaseChannel ?? '...' }}</span>
          </div>

          <div class="flex items-center justify-between rounded-2xl border border-default px-4 py-3">
            <span class="text-sm text-muted">Platform uptime</span>
            <span class="font-medium">{{ dashboard?.uptime ?? '...' }}</span>
          </div>

          <div class="rounded-2xl border border-default px-4 py-3">
            <p class="text-sm text-muted">Nuxt server API</p>
            <p class="mt-1 font-medium">{{ summary?.workspaceCount ?? 0 }} workspaces · {{ summary?.metricCount ?? 0 }} metrics</p>
            <p class="mt-1 text-sm text-muted">
              This card is loaded from <code>/api/summary</code> inside the Nuxt app, which in turn calls the Cosmo backend.
            </p>
          </div>

          <div v-if="error" class="rounded-2xl border border-error/50 bg-error/10 px-4 py-3 text-sm text-error">
            Failed to load dashboard data.
          </div>
        </div>
      </UCard>
    </section>

    <section class="grid gap-6 xl:grid-cols-[1.2fr_0.8fr]">
      <UCard>
        <template #header>
          <div class="flex items-center justify-between gap-4">
            <div>
              <p class="text-sm text-muted">Customer workspaces</p>
              <h3 class="text-xl font-semibold">Active rollout board</h3>
            </div>
            <UBadge color="primary" variant="subtle">{{ dashboard?.workspaces?.length ?? 0 }} workspaces</UBadge>
          </div>
        </template>

        <div class="space-y-3">
          <div
            v-for="workspace in dashboard?.workspaces ?? []"
            :key="workspace.name"
            class="flex flex-wrap items-center justify-between gap-4 rounded-2xl border border-default px-4 py-4"
          >
            <div>
              <p class="font-medium">{{ workspace.name }}</p>
              <p class="text-sm text-muted">{{ workspace.region }} · {{ workspace.members }} members</p>
            </div>

            <UBadge :color="workspace.status === 'Live' ? 'success' : 'warning'" variant="soft">
              {{ workspace.status }}
            </UBadge>
          </div>
        </div>

        <template #footer>
          <div class="flex justify-end">
            <UButton color="neutral" variant="ghost" to="/workspaces">View detailed workspace plan</UButton>
          </div>
        </template>
      </UCard>

      <UCard>
        <template #header>
          <div>
            <p class="text-sm text-muted">Operator note</p>
            <h3 class="text-xl font-semibold">Queue feedback to Cosmo</h3>
          </div>
        </template>

        <form class="space-y-4" @submit.prevent="submitFeedback">
          <UFormField label="Workspace" name="workspace">
            <UInput v-model="form.workspace" class="w-full" />
          </UFormField>

          <UFormField label="Message" name="message">
            <UTextarea v-model="form.message" :rows="5" class="w-full" />
          </UFormField>

          <UButton type="submit" :loading="submitting" block>
            Send feedback
          </UButton>
        </form>

        <div v-if="submission" class="mt-5 rounded-2xl border border-default bg-muted/30 p-4">
          <div class="flex items-center justify-between gap-3">
            <p class="font-medium">{{ submission.message }}</p>
            <UBadge color="success" variant="soft">{{ submission.status }}</UBadge>
          </div>
          <p class="mt-2 text-sm text-muted">{{ submission.preview }}</p>
        </div>
      </UCard>
    </section>

    <section>
      <UCard>
        <template #header>
          <div>
            <p class="text-sm text-muted">Activity stream</p>
            <h3 class="text-xl font-semibold">Recent control-room events</h3>
          </div>
        </template>

        <div class="space-y-4">
          <div
            v-for="item in dashboard?.timeline ?? []"
            :key="item.title"
            class="rounded-2xl border border-default px-4 py-4"
          >
            <div class="flex flex-wrap items-center justify-between gap-3">
              <p class="font-medium">{{ item.title }}</p>
              <span class="text-sm text-muted">{{ item.at }}</span>
            </div>
            <p class="mt-2 text-sm leading-6 text-muted">{{ item.description }}</p>
          </div>
        </div>
      </UCard>
    </section>
  </main>
</template>
