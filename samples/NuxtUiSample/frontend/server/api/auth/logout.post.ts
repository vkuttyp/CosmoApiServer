export default defineEventHandler(async (event) => {
  const config = useRuntimeConfig()

  // Tell the backend to clear its session
  await $fetch(`${config.apiBase}/api/auth/logout`, {
    method: 'POST',
    headers: getRequestHeaders(event, ['cookie']),
    onResponse({ response }) {
      const setCookie = response.headers.getSetCookie()
      for (const cookie of setCookie) {
        appendResponseHeader(event, 'set-cookie', cookie)
      }
    }
  }).catch(() => {
    // Backend unreachable — still clear the Nuxt session
  })

  // Clear the Nuxt sealed session
  await clearUserSession(event)

  return { success: true }
})
