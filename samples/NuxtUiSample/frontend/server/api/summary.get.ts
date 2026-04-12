import type { DashboardResponse } from '~/types/control-room'

export default defineEventHandler(async () => {
  const config = useRuntimeConfig()
  // Use the server-only apiBase — not exposed to the browser bundle.
  const apiBase = config.apiBase

  const dashboard = await $fetch<DashboardResponse>(`${apiBase}/api/dashboard`)

  return {
    productName: dashboard.productName,
    releaseChannel: dashboard.releaseChannel,
    workspaceCount: dashboard.workspaces.length,
    metricCount: dashboard.metrics.length,
    topMetric: dashboard.metrics[0] ?? null
  }
})
