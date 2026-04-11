#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
FRONTEND_DIR="$ROOT_DIR/frontend"

cleanup() {
  local exit_code=$?

  if [[ -n "${NPM_PID:-}" ]]; then
    kill "$NPM_PID" 2>/dev/null || true
    wait "$NPM_PID" 2>/dev/null || true
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

if [[ -d "$FRONTEND_DIR/.nuxt" ]]; then
  echo "Clearing stale Nuxt build cache..."
  rm -rf "$FRONTEND_DIR/.nuxt"
fi

echo "Starting Cosmo backend..."
(cd "$ROOT_DIR" && dotnet run) &
DOTNET_PID=$!

echo "Starting Nuxt frontend..."
(
  cd "$FRONTEND_DIR"
  NUXT_PUBLIC_API_BASE="${NUXT_PUBLIC_API_BASE:-http://127.0.0.1:9091}" npm run dev
) &
NPM_PID=$!

wait "$NPM_PID"
