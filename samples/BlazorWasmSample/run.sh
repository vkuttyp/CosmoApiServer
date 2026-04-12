#!/usr/bin/env bash
# Publishes the Blazor WASM client then starts the CosmoApiServer backend.
# The backend serves the WASM bundle from blazor/wwwroot via UseBlazorWasm.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$SCRIPT_DIR"

echo "Publishing Blazor WASM client..."
dotnet publish BlazorClient/BlazorClient.csproj \
  -c Release -o blazor --nologo -v q

echo
echo "Starting server on http://localhost:5050"
echo
ASPNETCORE_ENVIRONMENT=Production \
  dotnet run --project BlazorWasmSample.csproj -c Release
