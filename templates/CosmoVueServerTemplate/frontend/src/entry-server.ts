import { renderToString } from '@vue/server-renderer';
import { createCosmoApp, type DashboardPayload, type InitialState } from './app';

type RenderRequest = {
  path?: string;
};

type RenderResponse = {
  headHtml: string;
  appHtml: string;
  initialState: InitialState;
};

function buildDashboard(): DashboardPayload {
  return {
    title: 'Cosmo Vue Server',
    latency: '0.24 ms p50',
    transport: 'Raw sockets -> pipelines -> Vue SSR bridge',
    highlights: [
      'Vue frontend with history-mode routing',
      'CosmoApiServer JSON APIs under /api',
      'Static asset hosting with Vite manifest integration'
    ]
  };
}

export async function render(request: RenderRequest): Promise<RenderResponse> {
  const route = request.path || '/';
  const initialState: InitialState = {
    route,
    dashboard: buildDashboard()
  };

  const { app, router } = createCosmoApp({ initialState, ssr: true });
  await router.push(route);
  await router.isReady();

  const appHtml = await renderToString(app);
  const title = route === '/dashboard'
    ? 'Dashboard | Cosmo Vue Server'
    : route === '/about'
      ? 'About | Cosmo Vue Server'
      : 'Cosmo Vue Server';

  return {
    headHtml: `<title>${title}</title>`,
    appHtml: `<div id="app">${appHtml}</div>`,
    initialState
  };
}
