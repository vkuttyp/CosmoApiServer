import { createCosmoApp, type InitialState } from './app';

declare global {
  interface Window {
    __COSMO_VITE_STATE__?: InitialState;
  }
}

const initialState = window.__COSMO_VITE_STATE__ ?? {};
const { app, router } = createCosmoApp({ initialState, ssr: false });

router.isReady().then(() => {
  app.mount('#app');
});
