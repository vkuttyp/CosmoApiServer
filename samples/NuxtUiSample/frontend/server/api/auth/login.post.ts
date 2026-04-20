export default defineEventHandler(async (event) => {
  const body = await readBody<{ username: string; password: string }>(event)

  if (!body?.username || !body?.password) {
    throw createError({ statusCode: 400, statusMessage: 'Username and password are required.' })
  }

  const config = useRuntimeConfig()

  // Forward login to the .NET backend
  const backendResponse = await $fetch<{
    token: string
    tokenType: string
    expiresIn: number
    user: { username: string; role: string }
  }>(`${config.apiBase}/api/auth/login`, {
    method: 'POST',
    body: { username: body.username, password: body.password },
    // Forward cookies so the backend .Cosmo.Session cookie gets set
    headers: getRequestHeaders(event, ['cookie']),
    onResponse({ response }) {
      // Relay Set-Cookie from backend so the browser stores .Cosmo.Session
      const setCookie = response.headers.getSetCookie()
      for (const cookie of setCookie) {
        appendResponseHeader(event, 'set-cookie', cookie)
      }
    }
  }).catch((err) => {
    throw createError({
      statusCode: err.statusCode || 401,
      statusMessage: err.data?.error || 'Invalid credentials.'
    })
  })

  // Seed the Nuxt sealed session with user info from the backend response
  await setUserSession(event, {
    user: {
      username: backendResponse.user.username,
      role: backendResponse.user.role
    },
    secure: {
      token: backendResponse.token
    }
  })

  return {
    user: backendResponse.user
  }
})
