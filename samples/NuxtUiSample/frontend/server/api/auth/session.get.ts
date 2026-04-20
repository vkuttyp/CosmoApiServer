export default defineEventHandler(async (event) => {
  const config = useRuntimeConfig()

  // Check if the backend session is still valid by forwarding the cookie
  const backendSession = await $fetch<{
    authenticated: boolean
    user?: { username: string; role: string }
  }>(`${config.apiBase}/api/auth/session`, {
    headers: getRequestHeaders(event, ['cookie'])
  }).catch(() => ({ authenticated: false }))

  const nuxtSession = await getUserSession(event)

  // If backend says not authenticated, clear the Nuxt session too
  if (!backendSession.authenticated && nuxtSession.user) {
    await clearUserSession(event)
    return { authenticated: false }
  }

  // If backend is authenticated but Nuxt session is stale, re-sync
  if (backendSession.authenticated && backendSession.user && !nuxtSession.user) {
    await setUserSession(event, {
      user: {
        username: backendSession.user.username,
        role: backendSession.user.role
      }
    })
  }

  return {
    authenticated: backendSession.authenticated,
    user: backendSession.user ?? null
  }
})
