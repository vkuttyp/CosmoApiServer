export default defineEventHandler(async (event) => {
  const config = useRuntimeConfig()
  const apiBase = config.apiBase

  try {
    const upstream = await $fetch<{ status: string }>(`${apiBase}/api/health`, {
      timeout: 3000,
      headers: getRequestHeaders(event, ['cookie'])
    })
    return { status: upstream.status === 'ok' ? 'ok' : 'degraded', upstream: 'reachable' }
  } catch {
    return { status: 'degraded', upstream: 'unreachable' }
  }
})
