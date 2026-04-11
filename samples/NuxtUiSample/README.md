# NuxtUiSample

This sample pairs:

- `CosmoApiServer` as a JSON API backend
- `Nuxt 4` as the frontend runtime
- `@nuxt/ui` as the UI system

## Run the sample

Fast path:

```bash
npm run dev
```

That starts:

- the Cosmo backend at `http://127.0.0.1:9091`
- the Nuxt frontend at `http://127.0.0.1:3000`

Equivalent direct script:

```bash
./run-dev.sh
```

## Docker deployment

Build and run the full stack:

```bash
cd samples/NuxtUiSample
docker compose up --build -d
```

This starts:

- `cosmo-api` on the internal Docker network
- `nuxt-web` on the internal Docker network
- `nginx` on port `80`

Open:

```bash
http://your-vps-ip/
```

Files:

- `docker-compose.yml`
- `Dockerfile.backend`
- `frontend/Dockerfile`
- `nginx.conf`

## Manual mode

Backend:

```bash
dotnet run
```

Frontend:

```bash
cd frontend
npm install
npm run dev
```

The frontend reads the backend origin from `NUXT_PUBLIC_API_BASE`.
Default: `http://127.0.0.1:9091`

## What it shows

- Nuxt UI cards, badges, inputs, buttons, textarea, and app shell
- server-rendered Nuxt page that fetches Cosmo API data
- simple feedback form posting back to Cosmo
- second workspaces page with richer account/deployment data
- dynamic workspace detail route backed by `/api/workspaces/{slug}`
- example Nuxt server API at `frontend/server/api/summary.get.ts` that proxies/aggregates Cosmo API data
