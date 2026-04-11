import http from 'node:http';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { createServer as createViteServer } from 'vite';

const __dirname = dirname(fileURLToPath(import.meta.url));
const port = Number(process.env.SSR_PORT || '5174');
const isProd = process.env.NODE_ENV === 'production';

let vite;
let render;

async function resolveRenderer() {
  if (isProd) {
    if (!render) {
      const mod = await import(join(__dirname, 'dist/server/entry-server.js'));
      render = mod.render;
    }
    return render;
  }

  if (!vite) {
    vite = await createViteServer({
      root: __dirname,
      server: { middlewareMode: true },
      appType: 'custom'
    });
  }

  const mod = await vite.ssrLoadModule('/src/entry-server.ts');
  return mod.render;
}

const server = http.createServer(async (req, res) => {
  if (req.method !== 'POST' || req.url !== '/__cosmo/ssr') {
    res.statusCode = 404;
    res.end('Not Found');
    return;
  }

  try {
    const body = await new Promise((resolve, reject) => {
      let data = '';
      req.setEncoding('utf8');
      req.on('data', chunk => { data += chunk; });
      req.on('end', () => resolve(data));
      req.on('error', reject);
    });

    const payload = body ? JSON.parse(body) : {};
    const renderApp = await resolveRenderer();
    const result = await renderApp(payload);

    res.statusCode = 200;
    res.setHeader('Content-Type', 'application/json; charset=utf-8');
    res.end(JSON.stringify(result));
  } catch (error) {
    const message = error instanceof Error ? error.stack || error.message : String(error);
    res.statusCode = 500;
    res.setHeader('Content-Type', 'application/json; charset=utf-8');
    res.end(JSON.stringify({ message }));
  }
});

server.listen(port, '127.0.0.1', () => {
  console.log(`Cosmo Vue SSR bridge listening on http://127.0.0.1:${port}/__cosmo/ssr`);
});
