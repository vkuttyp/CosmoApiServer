# Cosmo Vue Server Template

This template uses:

- `CosmoApiServer` for transport, middleware, and `/api/*` routes
- `Vue 3 + Vue Router` for the frontend
- `Vite` for frontend development and production builds
- a small Node SSR bridge for full Vue-rendered HTML

## Production-style run

The template ships with prebuilt assets in `wwwroot`, so it can run immediately:

```bash
dotnet run
```

## Frontend development

Fast path:

```bash
./run-dev.sh
```

That starts:

- the Vite dev server on `http://127.0.0.1:5173`
- the SSR bridge on `http://127.0.0.1:5174/__cosmo/ssr`
- the Cosmo app on `http://localhost:8080`

Manual mode:

Run the Vite dev server in one terminal:

```bash
cd frontend
npm install
npm run dev
```

Run the SSR bridge in a second terminal:

```bash
cd frontend
npm run dev:ssr
```

Run the Cosmo server in a third terminal:

```bash
VITE_DEV_SERVER_URL=http://localhost:5173 \
VITE_SSR_SERVER_URL=http://127.0.0.1:5174/__cosmo/ssr \
dotnet run
```

Or use the `CosmoVueServerTemplate-VueSsrDev` launch profile.

## Production rebuild

When you change the Vue app and want fresh production assets plus the SSR bundle:

```bash
cd frontend
npm install
npm run build:all
```

That writes the client output and manifest into `wwwroot/`, and the SSR bundle into `frontend/dist/server/`.
