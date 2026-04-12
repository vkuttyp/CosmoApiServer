export default defineNuxtConfig({
  modules: ['@nuxt/ui', '@nuxt/eslint'],
  devtools: { enabled: true },

  vite: {
    optimizeDeps: {
      include: [
        '@vue/devtools-core',
        '@vue/devtools-kit'
      ]
    }
  },

  nitro: {
    // In dev, forward /api/* to the Cosmo backend so pages can use relative
    // URLs and avoid CORS entirely. In production, configure a reverse proxy
    // (nginx / Caddy) or use the integrated single-port deployment pattern.
    devProxy: {
      '/api': {
        target: process.env.NUXT_API_BASE || 'http://127.0.0.1:9091',
        changeOrigin: true,
        prependPath: false
      }
    }
  },

  routeRules: {
    // Workspace list is non-user-specific — serve stale-while-revalidate
    // so the first request is instant and data refreshes in the background.
    '/workspaces': { swr: 60 }
  },

  runtimeConfig: {
    // Server-only: never sent to the browser. Used by Nuxt server routes.
    apiBase: process.env.NUXT_API_BASE || 'http://127.0.0.1:9091',
    public: {
      // Exposed to the browser. Used for direct client-side fetches.
      apiBase: process.env.NUXT_PUBLIC_API_BASE || 'http://127.0.0.1:9091'
    }
  },

  compatibilityDate: '2026-04-11'
})
