<template>
  <div class="space-y-6">

    <!-- ── Service grid ──────────────────────────────────────────────────── -->
    <section>
      <h2 class="text-sm font-medium text-gray-400 uppercase tracking-widest mb-3">Services</h2>
      <div class="grid grid-cols-2 md:grid-cols-3 xl:grid-cols-5 gap-3">
        <div
          v-for="svc in status?.services"
          :key="svc.name"
          class="rounded-xl border border-gray-800 bg-gray-900 p-4 flex flex-col gap-1"
        >
          <div class="flex items-center justify-between">
            <span class="text-xs font-mono text-gray-400">{{ svc.region }}</span>
            <span
              :class="svc.status === 'healthy' ? 'text-emerald-400' : svc.status === 'degraded' ? 'text-amber-400' : 'text-red-400'"
              class="text-xs font-semibold uppercase"
            >{{ svc.status }}</span>
          </div>
          <p class="text-sm font-medium leading-snug">{{ svc.name }}</p>
          <p class="text-xs text-gray-500">{{ svc.uptime }} · {{ svc.replicas }}r</p>
        </div>
      </div>
    </section>

    <!-- ── Live metrics ──────────────────────────────────────────────────── -->
    <section class="grid grid-cols-2 md:grid-cols-4 gap-3">
      <MetricCard label="CPU" :value="latest?.cpu ?? 0" unit="%" :warn="70" :danger="90" />
      <MetricCard label="Memory" :value="latest?.memory ?? 0" unit="%" :warn="80" :danger="92" />
      <MetricCard label="Requests/s" :value="latest?.requests ?? 0" unit="" />
      <MetricCard label="P99 Latency" :value="latest?.latencyMs ?? 0" unit="ms" :warn="150" :danger="300" />
    </section>

    <!-- ── Log stream ────────────────────────────────────────────────────── -->
    <section>
      <h2 class="text-sm font-medium text-gray-400 uppercase tracking-widest mb-3">Live Logs</h2>
      <div class="rounded-xl border border-gray-800 bg-gray-900 overflow-hidden">
        <div class="divide-y divide-gray-800 font-mono text-xs max-h-80 overflow-y-auto">
          <div
            v-for="(entry, i) in logEntries"
            :key="i"
            class="flex items-start gap-3 px-4 py-2"
          >
            <span
              :class="{
                'text-sky-400':   entry.level === 'info',
                'text-amber-400': entry.level === 'warn',
                'text-red-400':   entry.level === 'error',
              }"
              class="w-9 shrink-0 text-center uppercase font-bold"
            >{{ entry.level }}</span>
            <span class="text-gray-500 shrink-0">{{ fmtTime(entry.timestamp) }}</span>
            <span class="text-violet-300 shrink-0">{{ entry.service }}</span>
            <span class="text-gray-200 break-all">{{ entry.message }}</span>
          </div>
          <div v-if="logEntries.length === 0" class="px-4 py-6 text-center text-gray-600">
            Waiting for log events…
          </div>
        </div>
      </div>
    </section>

  </div>
</template>

<script setup lang="ts">
import type { StatusResponse } from '~/types/liveops'

const config = useRuntimeConfig()
const { data: status } = await useFetch<StatusResponse>('/api/status', {
  baseURL: config.public.apiBase
})
const { latest }       = useMetricStream()
const { entries: logEntries } = useLogStream()

function fmtTime(iso: string): string {
  return new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' })
}
</script>
