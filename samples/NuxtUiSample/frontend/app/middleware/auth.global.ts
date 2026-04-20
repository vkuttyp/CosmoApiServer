export default defineNuxtRouteMiddleware((to) => {
  const { loggedIn } = useUserSession()

  // Allow the login page without auth
  if (to.path === '/login') return

  // Redirect to login if not authenticated
  if (!loggedIn.value) {
    return navigateTo('/login')
  }
})
