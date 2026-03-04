#!/bin/bash

# Port 9001: CosmoApiServer
# Port 9002: AspNetCore

echo "🚀 Building projects..."
dotnet build samples/CosmoApiBenchHost/CosmoApiBenchHost.csproj -c Release --nologo
dotnet build samples/AspNetBenchHost/AspNetBenchHost.csproj -c Release --nologo
dotnet build tests/ApiServer.Benchmark/ApiServer.Benchmark.csproj -c Release --nologo

echo ""
echo "🔥 Starting benchmark hosts..."
dotnet run --project samples/CosmoApiBenchHost/CosmoApiBenchHost.csproj -c Release --no-build &
COSMO_PID=$!
dotnet run --project samples/AspNetBenchHost/AspNetBenchHost.csproj -c Release --no-build &
ASP_PID=$!

# Wait for servers to be ready
sleep 3

echo ""
echo "📊 Running CosmoApiServer Benchmark..."
dotnet run --project tests/ApiServer.Benchmark/ApiServer.Benchmark.csproj -c Release --no-build -- CosmoApiServer

echo ""
echo "📊 Running AspNetCore Benchmark..."
dotnet run --project tests/ApiServer.Benchmark/ApiServer.Benchmark.csproj -c Release --no-build -- AspNetCore

echo ""
echo "🛑 Shutting down hosts..."
kill $COSMO_PID $ASP_PID

echo ""
echo "💡 To test concurrent performance (c=50, n=5000), you can use ApacheBench:"
echo "CosmoApiServer: ab -k -c 50 -n 5000 http://127.0.0.1:9001/ping"
echo "AspNetCore:     ab -k -c 50 -n 5000 http://127.0.0.1:9002/ping"
