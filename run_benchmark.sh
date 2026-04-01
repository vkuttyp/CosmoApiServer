#!/bin/bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COSMO_URL="http://127.0.0.1:9102"
ASPNET_URL="http://127.0.0.1:9103"

COSMO_PID=""
ASP_PID=""
COSMO_LOG=""
ASP_LOG=""

cleanup() {
  kill "${COSMO_PID:-}" "${ASP_PID:-}" 2>/dev/null || true
  wait "${COSMO_PID:-}" "${ASP_PID:-}" 2>/dev/null || true
  rm -f "${COSMO_LOG:-}" "${ASP_LOG:-}"
}

wait_for_url() {
  local url="$1"
  local name="$2"
  local log_file="$3"

  for _ in $(seq 1 30); do
    if curl -fsS "$url/ping" >/dev/null 2>&1; then
      return 0
    fi
    sleep 1
  done

  echo "ERROR: $name did not become ready at $url" >&2
  if [[ -f "$log_file" ]]; then
    cat "$log_file" >&2
  fi
  exit 1
}

trap cleanup EXIT

cd "$ROOT_DIR"

echo "Building projects..."
dotnet build samples/CosmoApiBenchHost/CosmoApiBenchHost.csproj -c Release --nologo
dotnet build samples/AspNetBenchHost/AspNetBenchHost.csproj -c Release --nologo
dotnet build tests/ApiServer.Benchmark/ApiServer.Benchmark.csproj -c Release --nologo

echo
echo "Starting benchmark hosts..."
COSMO_LOG="$(mktemp)"
ASP_LOG="$(mktemp)"

dotnet run --project samples/CosmoApiBenchHost/CosmoApiBenchHost.csproj -c Release --no-build >"$COSMO_LOG" 2>&1 &
COSMO_PID=$!
dotnet run --project samples/AspNetBenchHost/AspNetBenchHost.csproj -c Release --no-build >"$ASP_LOG" 2>&1 &
ASP_PID=$!

wait_for_url "$COSMO_URL" "CosmoApiBenchHost" "$COSMO_LOG"
wait_for_url "$ASPNET_URL" "AspNetBenchHost" "$ASP_LOG"

echo
echo "Running CosmoApiServer benchmark..."
dotnet run --project tests/ApiServer.Benchmark/ApiServer.Benchmark.csproj -c Release --no-build -- CosmoApiServer

echo
echo "Running AspNetCore benchmark..."
dotnet run --project tests/ApiServer.Benchmark/ApiServer.Benchmark.csproj -c Release --no-build -- AspNetCore

echo
echo "Shutting down hosts..."
