#!/bin/bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SAMPLE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
URL="http://127.0.0.1:9092"
SERVER_PID=""
SERVER_LOG=""

cleanup() {
  kill "${SERVER_PID:-}" 2>/dev/null || true
  wait "${SERVER_PID:-}" 2>/dev/null || true
  rm -f "${SERVER_LOG:-}"
}
trap cleanup EXIT

wait_for_ready() {
  for _ in $(seq 1 30); do
    if curl -fsS "$URL/ping" >/dev/null 2>&1; then return 0; fi
    sleep 1
  done
  echo "ERROR: LiveOpsSample did not become ready at $URL" >&2
  cat "${SERVER_LOG:-}" >&2
  return 1
}

echo "Building..."
dotnet build "$SAMPLE_DIR/LiveOpsSample.csproj" -c Release --nologo -v q
dotnet build "$ROOT_DIR/tests/ApiServer.Benchmark/ApiServer.Benchmark.csproj" -c Release --nologo -v q

echo
echo "Starting LiveOpsSample (production mode, no Nuxt)..."
SERVER_LOG="$(mktemp)"
ASPNETCORE_ENVIRONMENT=Production \
  dotnet run --project "$SAMPLE_DIR/LiveOpsSample.csproj" -c Release --no-build \
  >"$SERVER_LOG" 2>&1 &
SERVER_PID=$!

wait_for_ready

echo
dotnet run --project "$ROOT_DIR/tests/ApiServer.Benchmark/ApiServer.Benchmark.csproj" \
  -c Release --no-build -- LiveOpsSample

echo
echo "Done. Server shutting down..."
