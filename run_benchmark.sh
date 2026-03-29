#!/bin/bash

# Port 9001: CosmoApiServer
# Port 9002: AspNetCore
# Port 9003: CosmoRazor
# Port 9004: BlazorSSR

echo "🚀 Building projects..."
dotnet build samples/CosmoApiBenchHost/CosmoApiBenchHost.csproj -c Release --nologo
dotnet build samples/AspNetBenchHost/AspNetBenchHost.csproj -c Release --nologo
# dotnet build samples/CosmoRazorBenchHost/CosmoRazorBenchHost.csproj -c Release --nologo
# dotnet build samples/BlazorBenchHost/BlazorBenchHost.csproj -c Release --nologo
dotnet build tests/ApiServer.Benchmark/ApiServer.Benchmark.csproj -c Release --nologo

echo ""
echo "🔥 Starting benchmark hosts..."
dotnet run --project samples/CosmoApiBenchHost/CosmoApiBenchHost.csproj -c Release --no-build &
COSMO_PID=$!
dotnet run --project samples/AspNetBenchHost/AspNetBenchHost.csproj -c Release --no-build &
ASP_PID=$!
# dotnet run --project samples/CosmoRazorBenchHost/CosmoRazorBenchHost.csproj -c Release --no-build &
# RAZOR_PID=$!
# dotnet run --project samples/BlazorBenchHost/BlazorBenchHost.csproj -c Release --no-build &
# BLAZOR_PID=$!

# Wait for servers to be ready
sleep 5

echo ""
echo "📊 Running CosmoRazor (CosmoApiServer Razor Components) Benchmark..."
dotnet run --project tests/ApiServer.Benchmark/ApiServer.Benchmark.csproj -c Release --no-build -- CosmoRazor

echo ""
echo "📊 Running BlazorSSR Benchmark..."
dotnet run --project tests/ApiServer.Benchmark/ApiServer.Benchmark.csproj -c Release --no-build -- BlazorSSR

echo ""
echo "🛑 Shutting down hosts..."
kill $COSMO_PID $ASP_PID $RAZOR_PID $BLAZOR_PID

echo ""
echo "💡 To test concurrent performance (c=50, n=5000), you can use ApacheBench:"
echo "CosmoRazor: ab -k -c 50 -n 5000 http://127.0.0.1:9003/bench"
echo "BlazorSSR:  ab -k -c 50 -n 5000 http://127.0.0.1:9004/bench"
