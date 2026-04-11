import { defineConfig } from 'vite';
import { fileURLToPath, URL } from 'node:url';

export default defineConfig({
  root: fileURLToPath(new URL('./', import.meta.url)),
  publicDir: false,
  build: {
    outDir: fileURLToPath(new URL('../wwwroot', import.meta.url)),
    emptyOutDir: false,
    manifest: true,
    rollupOptions: {
      input: fileURLToPath(new URL('./src/entry-client.ts', import.meta.url))
    }
  },
  server: {
    port: 5173,
    strictPort: true
  }
});
