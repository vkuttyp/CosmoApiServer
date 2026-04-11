#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
FRONTEND_DIR="$ROOT_DIR/frontend"
VITE_URL="${VITE_DEV_SERVER_URL:-http://127.0.0.1:5173}"
SSR_URL="${VITE_SSR_SERVER_URL:-http://127.0.0.1:5174/__cosmo/ssr}"

if ! command -v npm >/dev/null 2>&1; then
  echo "npm is required."
  exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet is required."
  exit 1
fi

cleanup() {
  local exit_code=$?

  if [[ -n "${VITE_PID:-}" ]]; then
    kill "$VITE_PID" 2>/dev/null || true
    wait "$VITE_PID" 2>/dev/null || true
  fi

  if [[ -n "${SSR_PID:-}" ]]; then
    kill "$SSR_PID" 2>/dev/null || true
    wait "$SSR_PID" 2>/dev/null || true
  fi

  if [[ -n "${DOTNET_PID:-}" ]]; then
    kill "$DOTNET_PID" 2>/dev/null || true
    wait "$DOTNET_PID" 2>/dev/null || true
  fi

  exit "$exit_code"
}

trap cleanup EXIT INT TERM

if [[ ! -d "$FRONTEND_DIR/node_modules" ]]; then
  echo "Installing frontend dependencies..."
  (cd "$FRONTEND_DIR" && npm install)
fi

echo "Starting Vite dev server..."
(cd "$FRONTEND_DIR" && npm run dev) &
VITE_PID=$!

echo "Starting Vue SSR bridge..."
(cd "$FRONTEND_DIR" && npm run dev:ssr) &
SSR_PID=$!

echo "Starting Cosmo server..."
(
  cd "$ROOT_DIR"
  VITE_DEV_SERVER_URL="$VITE_URL" \
  VITE_SSR_SERVER_URL="$SSR_URL" \
  dotnet run
) &
DOTNET_PID=$!

wait "$DOTNET_PID"
