import { computed, createSSRApp, onMounted, ref } from 'vue';
import { createMemoryHistory, createRouter, createWebHistory, RouterLink, RouterView } from 'vue-router';
import './style.css';

export type DashboardPayload = {
  title: string;
  latency: string;
  transport: string;
  highlights: string[];
};

export type InitialState = {
  route?: string;
  dashboard?: DashboardPayload;
};

type AppOptions = {
  initialState?: InitialState;
  ssr?: boolean;
};

function useDashboardData(initialState: InitialState) {
  const payload = ref<DashboardPayload | null>(initialState.dashboard ?? null);
  const pending = ref(payload.value === null);
  const error = ref('');

  const load = async () => {
    if (payload.value) {
      pending.value = false;
      return;
    }

    pending.value = true;
    error.value = '';

    try {
      const response = await fetch('/api/dashboard');
      if (!response.ok) {
        throw new Error(`Request failed with ${response.status}`);
      }

      payload.value = await response.json() as DashboardPayload;
    } catch (err) {
      error.value = err instanceof Error ? err.message : String(err);
    } finally {
      pending.value = false;
    }
  };

  onMounted(load);
  return { pending, error, payload, load };
}

export function createCosmoApp(options: AppOptions = {}) {
  const initialState = options.initialState ?? {};

  const HomeView = {
    name: 'HomeView',
    setup() {
      const api = useDashboardData(initialState);
      const heroCode = computed(() => `var builder = CosmoWebApplicationBuilder.Create()
    .UseStaticFiles("wwwroot")
    .UseViteFrontend(...);

app.MapGet("/api/dashboard", ctx => ctx.Response.WriteJson(...));`);

      return { ...api, heroCode };
    },
    template: `
      <section class="hero">
        <div>
          <div class="eyebrow">Vue SSR Bridge</div>
          <h1>Cosmo API on the back. Vue on the front.</h1>
          <p>
            This starter uses Vite for assets and a small external SSR renderer, while Cosmo keeps transport and APIs.
          </p>
          <div class="actions">
            <RouterLink class="button primary" to="/dashboard">Open dashboard</RouterLink>
            <a class="button secondary" href="/api/health">Check API health</a>
          </div>
        </div>
        <div class="codecard">
          <div class="eyebrow">Server setup</div>
          <pre>{{ heroCode }}</pre>
        </div>
      </section>
      <section class="grid">
        <article class="metric">
          <div class="label">Render model</div>
          <strong>External SSR</strong>
          <p>Cosmo requests app HTML and state from a Vue SSR bridge over HTTP.</p>
        </article>
        <article class="metric">
          <div class="label">Dev loop</div>
          <strong>Vite + SSR bridge</strong>
          <p>Run the Vite client and the SSR renderer separately during development.</p>
        </article>
        <article class="metric">
          <div class="label">Server boundaries</div>
          <strong>/api/*</strong>
          <p>API routes remain backend-owned and bypass frontend rendering.</p>
        </article>
      </section>
      <section class="panel stack" style="margin-top: 24px;">
        <div>
          <h2>Live API payload</h2>
          <p class="status" v-if="pending">Loading dashboard data</p>
          <p class="status" v-else-if="error">{{ error }}</p>
        </div>
        <div class="api-result" v-if="payload">
          <h3>{{ payload.title }}</h3>
          <p>{{ payload.transport }}</p>
          <ul>
            <li v-for="item in payload.highlights" :key="item">{{ item }}</li>
          </ul>
        </div>
      </section>
    `
  };

  const DashboardView = {
    name: 'DashboardView',
    setup() {
      return useDashboardData(initialState);
    },
    template: `
      <section class="panel stack">
        <div>
          <div class="eyebrow">Dashboard</div>
          <h2>Nuxt-style split: app shell plus JSON endpoints</h2>
          <p>
            The server owns transport and APIs. Vue owns route rendering, hydration, and client state.
          </p>
        </div>
        <div class="api-result" v-if="payload">
          <h3>{{ payload.title }}</h3>
          <p><strong>Latency:</strong> {{ payload.latency }}</p>
          <p><strong>Transport:</strong> {{ payload.transport }}</p>
          <ul>
            <li v-for="item in payload.highlights" :key="item">{{ item }}</li>
          </ul>
        </div>
        <p class="status" v-else-if="pending">Loading dashboard data</p>
        <p class="status" v-else>{{ error }}</p>
      </section>
    `
  };

  const AboutView = {
    name: 'AboutView',
    template: `
      <section class="panel stack">
        <div class="eyebrow">About</div>
        <h2>What this template is doing</h2>
        <ul class="list">
          <li>Frontend source lives under <code>frontend/</code>.</li>
          <li>Built client assets are emitted into <code>wwwroot/</code>.</li>
          <li>A Node SSR bridge renders HTML and state for Cosmo.</li>
          <li>Cosmo serves the final document and all API routes.</li>
        </ul>
      </section>
    `
  };

  const router = createRouter({
    history: options.ssr ? createMemoryHistory() : createWebHistory(),
    routes: [
      { path: '/', component: HomeView },
      { path: '/dashboard', component: DashboardView },
      { path: '/about', component: AboutView }
    ]
  });

  const Root = {
    name: 'RootView',
    components: { RouterLink, RouterView },
    template: `
      <div class="shell">
        <header class="topbar">
          <div class="brand">
            <span class="eyebrow">CosmoApiServer</span>
            <strong>Vue + Vite Template</strong>
          </div>
          <nav class="nav">
            <RouterLink to="/">Home</RouterLink>
            <RouterLink to="/dashboard">Dashboard</RouterLink>
            <RouterLink to="/about">About</RouterLink>
          </nav>
        </header>
        <RouterView />
      </div>
    `
  };

  const app = createSSRApp(Root);
  app.use(router);

  return { app, router, initialState };
}
