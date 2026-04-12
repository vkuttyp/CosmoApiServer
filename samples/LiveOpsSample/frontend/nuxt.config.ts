export default defineNuxtConfig({
  modules: ['@nuxt/ui', '@nuxt/eslint'],
  devtools: { enabled: true },

  nitro: {
    // In dev the Nuxt dev server forwards /api/* to the Cosmo backend so the
    // browser can use relative URLs without any CORS headers needed.
    devProxy: {
      '/api': {
        target: process.env.NUXT_API_BASE || 'http://127.0.0.1:9092',
        changeOrigin: true,
        prependPath: false
      }
    }
  },

  runtimeConfig: {
    // Server-only — never sent to the browser.
    apiBase: process.env.NUXT_API_BASE || 'http://127.0.0.1:9092',
    public: {
      // Exposed to the browser for direct EventSource / fetch calls.
      apiBase: process.env.NUXT_PUBLIC_API_BASE || 'http://127.0.0.1:9092'
    }
  },

  vite: {
    optimizeDeps: {
      include: ['@vue/devtools-core', '@vue/devtools-kit']
    }
  },

  compatibilityDate: '2026-04-12'
})
