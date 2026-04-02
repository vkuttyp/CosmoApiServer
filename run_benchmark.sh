#!/bin/bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COSMO_URL="http://127.0.0.1:9102"
COSMO_H3_URL="https://127.0.0.1:9443"
ASPNET_URL="http://127.0.0.1:9103"

COSMO_PID=""
COSMO_H3_PID=""
ASP_PID=""
COSMO_LOG=""
COSMO_H3_LOG=""
ASP_LOG=""
BENCH_CERT=""
BENCH_CERT_PASSWORD="cosmo-bench"

cleanup() {
  kill "${COSMO_PID:-}" "${COSMO_H3_PID:-}" "${ASP_PID:-}" 2>/dev/null || true
  wait "${COSMO_PID:-}" "${COSMO_H3_PID:-}" "${ASP_PID:-}" 2>/dev/null || true
  rm -f "${COSMO_LOG:-}" "${COSMO_H3_LOG:-}" "${ASP_LOG:-}" "${BENCH_CERT:-}"
}

wait_for_url() {
  local url="$1"
  local name="$2"
  local log_file="$3"
  local insecure="${4:-false}"

  for _ in $(seq 1 30); do
    if [[ "$insecure" == "true" ]]; then
      if curl -kfsS "$url/ping" >/dev/null 2>&1; then
        return 0
      fi
    elif curl -fsS "$url/ping" >/dev/null 2>&1; then
      return 0
    fi
    sleep 1
  done

  echo "ERROR: $name did not become ready at $url" >&2
  if [[ -f "$log_file" ]]; then
    cat "$log_file" >&2
  fi
  return 1
}

trap cleanup EXIT

cd "$ROOT_DIR"

ensure_bench_cert() {
  BENCH_CERT="$(mktemp /tmp/cosmo-bench-XXXXXX.pfx)"
  if ! dotnet dev-certs https -ep "$BENCH_CERT" -p "$BENCH_CERT_PASSWORD" >/dev/null 2>&1; then
    echo "WARNING: could not create local HTTPS certificate with dotnet dev-certs; skipping HTTP/3 benchmark." >&2
    BENCH_CERT=""
    return 1
  fi
  return 0
}

echo "Building projects..."
dotnet build samples/CosmoApiBenchHost/CosmoApiBenchHost.csproj -c Release --nologo
dotnet build samples/AspNetBenchHost/AspNetBenchHost.csproj -c Release --nologo
dotnet build tests/ApiServer.Benchmark/ApiServer.Benchmark.csproj -c Release --nologo

echo
echo "Starting benchmark hosts..."
COSMO_LOG="$(mktemp)"
COSMO_H3_LOG="$(mktemp)"
ASP_LOG="$(mktemp)"

dotnet run --project samples/CosmoApiBenchHost/CosmoApiBenchHost.csproj -c Release --no-build >"$COSMO_LOG" 2>&1 &
COSMO_PID=$!
dotnet run --project samples/AspNetBenchHost/AspNetBenchHost.csproj -c Release --no-build >"$ASP_LOG" 2>&1 &
ASP_PID=$!

if ensure_bench_cert; then
  COSMO_BENCH_PORT=9443 \
  COSMO_BENCH_CERT_PATH="$BENCH_CERT" \
  COSMO_BENCH_CERT_PASSWORD="$BENCH_CERT_PASSWORD" \
  COSMO_BENCH_ENABLE_HTTP3=true \
  dotnet run --project samples/CosmoApiBenchHost/CosmoApiBenchHost.csproj -c Release --no-build >"$COSMO_H3_LOG" 2>&1 &
  COSMO_H3_PID=$!
fi

wait_for_url "$COSMO_URL" "CosmoApiBenchHost" "$COSMO_LOG" || exit 1
wait_for_url "$ASPNET_URL" "AspNetBenchHost" "$ASP_LOG" || exit 1
if [[ -n "${COSMO_H3_PID:-}" ]]; then
  if ! wait_for_url "$COSMO_H3_URL" "CosmoApiBenchHost HTTP/3" "$COSMO_H3_LOG" true; then
    echo "WARNING: skipping HTTP/3 benchmark on this machine." >&2
    kill "${COSMO_H3_PID:-}" 2>/dev/null || true
    wait "${COSMO_H3_PID:-}" 2>/dev/null || true
    COSMO_H3_PID=""
  fi
fi

echo
echo "Running CosmoApiServer benchmark..."
dotnet run --project tests/ApiServer.Benchmark/ApiServer.Benchmark.csproj -c Release --no-build -- CosmoApiServer

if [[ -n "${COSMO_H3_PID:-}" ]]; then
  echo
  echo "Running CosmoApiServer HTTP/3 benchmark..."
  dotnet run --project tests/ApiServer.Benchmark/ApiServer.Benchmark.csproj -c Release --no-build -- CosmoApiServerHttp3
fi

echo
echo "Running AspNetCore benchmark..."
dotnet run --project tests/ApiServer.Benchmark/ApiServer.Benchmark.csproj -c Release --no-build -- AspNetCore

echo
echo "Shutting down hosts..."
