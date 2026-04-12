#!/bin/bash
# Benchmarks two deployment modes side-by-side:
#
#   CosmoNuxtIntegrated — Cosmo (.NET) serving pre-built Nuxt static files
#                         from frontend/.output/public (SPA, Brotli, caching)
#
#   NuxtNative          — Nuxt's own Nitro production server doing SSR,
#                         calling the Cosmo API at 9092 for page data
#
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SAMPLE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COSMO_URL="http://127.0.0.1:9092"
NUXT_URL="http://127.0.0.1:3001"
COSMO_PID=""
NUXT_PID=""
COSMO_LOG=""
NUXT_LOG=""

cleanup() {
  kill "${COSMO_PID:-}" "${NUXT_PID:-}" 2>/dev/null || true
  wait "${COSMO_PID:-}" "${NUXT_PID:-}" 2>/dev/null || true
  rm -f "${COSMO_LOG:-}" "${NUXT_LOG:-}"
}
trap cleanup EXIT

wait_for() {
  local url="$1" name="$2" log="$3"
  for _ in $(seq 1 40); do
    if curl -fsS "$url/ping" >/dev/null 2>&1; then return 0; fi
    sleep 1
  done
  echo "ERROR: $name did not become ready at $url" >&2
  cat "$log" >&2
  return 1
}

# ── 1. Build ──────────────────────────────────────────────────────────────────
echo "Building frontend..."
npm run frontend:build --prefix "$SAMPLE_DIR" 2>&1 | grep -E 'error|warn|built|done|✓|✗' || true

echo "Building .NET backend and benchmark runner..."
dotnet build "$SAMPLE_DIR/LiveOpsSample.csproj" -c Release --nologo -v q
dotnet build "$ROOT_DIR/tests/ApiServer.Benchmark/ApiServer.Benchmark.csproj" -c Release --nologo -v q

# ── 2. Start servers ──────────────────────────────────────────────────────────
echo
echo "Starting CosmoApiServer (production, port 9092)..."
COSMO_LOG="$(mktemp)"
ASPNETCORE_ENVIRONMENT=Production \
  dotnet run --project "$SAMPLE_DIR/LiveOpsSample.csproj" -c Release --no-build \
  >"$COSMO_LOG" 2>&1 &
COSMO_PID=$!

echo "Starting Nuxt Nitro server (production, port 3001)..."
NUXT_LOG="$(mktemp)"
PORT=3001 HOST=127.0.0.1 \
  node "$SAMPLE_DIR/frontend/.output/server/index.mjs" \
  >"$NUXT_LOG" 2>&1 &
NUXT_PID=$!

wait_for "$COSMO_URL" "CosmoNuxtIntegrated" "$COSMO_LOG"
wait_for "$NUXT_URL"  "NuxtNative"          "$NUXT_LOG"

# ── 3. Benchmark ──────────────────────────────────────────────────────────────
echo
echo "▶ CosmoNuxtIntegrated (static SPA + API, .NET)"
dotnet run --project "$ROOT_DIR/tests/ApiServer.Benchmark/ApiServer.Benchmark.csproj" \
  -c Release --no-build -- CosmoNuxtIntegrated

echo
echo "▶ NuxtNative (Nitro SSR, Node.js — calls Cosmo at 9092 for API data)"
dotnet run --project "$ROOT_DIR/tests/ApiServer.Benchmark/ApiServer.Benchmark.csproj" \
  -c Release --no-build -- NuxtNative

echo
echo "Done."
