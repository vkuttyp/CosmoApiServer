export default defineEventHandler(async (event) => {
  // Only run on navigational (SSR) requests, not API calls
  const path = getRequestURL(event).pathname
  if (path.startsWith('/api/') || path.startsWith('/_nuxt/')) return

  const nuxtSession = await getUserSession(event)
  if (!nuxtSession.user) return

  const config = useRuntimeConfig()

  // Validate the backend session is still alive
  const backendSession = await $fetch<{ authenticated: boolean }>(`${config.apiBase}/api/auth/session`, {
    headers: getRequestHeaders(event, ['cookie'])
  }).catch(() => ({ authenticated: false }))

  // Backend session expired — clear the Nuxt session so the UI reflects it
  if (!backendSession.authenticated) {
    await clearUserSession(event)
  }
})
