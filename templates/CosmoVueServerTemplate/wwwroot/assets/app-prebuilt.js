import { createApp, ref, computed, onMounted } from 'https://cdn.jsdelivr.net/npm/vue@3.5.13/dist/vue.esm-browser.prod.js';
import { createRouter, createWebHistory } from 'https://cdn.jsdelivr.net/npm/vue-router@4.4.5/dist/vue-router.esm-browser.prod.js';

const apiState = () => {
    const pending = ref(true);
    const error = ref('');
    const payload = ref(null);

    const load = async () => {
        pending.value = true;
        error.value = '';

        try {
            const response = await fetch('/api/dashboard');
            if (!response.ok) {
                throw new Error(`Request failed with ${response.status}`);
            }

            payload.value = await response.json();
        } catch (err) {
            error.value = err instanceof Error ? err.message : String(err);
        } finally {
            pending.value = false;
        }
    };

    onMounted(load);
    return { pending, error, payload, load };
};

const HomeView = {
    setup() {
        const api = apiState();
        const heroCode = computed(() => `var builder = CosmoWebApplicationBuilder.Create()
    .UseStaticFiles("wwwroot")
    .UseViteFrontend(...);

app.MapGet("/api/dashboard", ctx => ctx.Response.WriteJson(...));`);

        return { ...api, heroCode };
    },
    template: `
        <section class="hero">
            <div>
                <div class="eyebrow">Vite + Vue</div>
                <h1>Cosmo API on the back. Vue on the front.</h1>
                <p>This starter uses a Vite source tree, browser history routing, and manifest-based asset loading on the server.</p>
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
                <div class="label">Asset strategy</div>
                <strong>Manifest driven</strong>
                <p>Cosmo injects hashed JS and CSS from Vite's manifest at request time.</p>
            </article>
            <article class="metric">
                <div class="label">Dev loop</div>
                <strong>Vite server</strong>
                <p>Set <code>VITE_DEV_SERVER_URL</code> and Cosmo will render the Vite client instead.</p>
            </article>
            <article class="metric">
                <div class="label">Server boundaries</div>
                <strong>/api/*</strong>
                <p>API paths bypass the frontend shell and remain explicit backend routes.</p>
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
    setup() {
        return apiState();
    },
    template: `
        <section class="panel stack">
            <div>
                <div class="eyebrow">Dashboard</div>
                <h2>Nuxt-style split: app shell plus JSON endpoints</h2>
                <p>The server owns transport and APIs. Vue owns composition, navigation, and client state.</p>
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
    template: `
        <section class="panel stack">
            <div class="eyebrow">About</div>
            <h2>What this template is doing</h2>
            <ul class="list">
                <li>Frontend source lives under <code>frontend/</code>.</li>
                <li>Built assets are emitted into <code>wwwroot/</code>.</li>
                <li>Cosmo renders the HTML shell and injects Vite assets from the manifest.</li>
                <li>In development, <code>VITE_DEV_SERVER_URL</code> switches the shell to the Vite dev server.</li>
            </ul>
        </section>
    `
};

const router = createRouter({
    history: createWebHistory(),
    routes: [
        { path: '/', component: HomeView },
        { path: '/dashboard', component: DashboardView },
        { path: '/about', component: AboutView }
    ]
});

const Root = {
    template: `
        <div class="shell">
            <header class="topbar">
                <div class="brand">
                    <span class="eyebrow">CosmoApiServer</span>
                    <strong>Vue + Vite Template</strong>
                </div>
                <nav class="nav">
                    <RouterLink to="/" custom v-slot="{ href, navigate, isActive }">
                        <a :href="href" :class="{ active: isActive }" @click="navigate">Home</a>
                    </RouterLink>
                    <RouterLink to="/dashboard" custom v-slot="{ href, navigate, isActive }">
                        <a :href="href" :class="{ active: isActive }" @click="navigate">Dashboard</a>
                    </RouterLink>
                    <RouterLink to="/about" custom v-slot="{ href, navigate, isActive }">
                        <a :href="href" :class="{ active: isActive }" @click="navigate">About</a>
                    </RouterLink>
                </nav>
            </header>
            <RouterView />
        </div>
    `
};

createApp(Root).use(router).mount('#app');
