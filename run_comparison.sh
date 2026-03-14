#!/bin/bash

# Port 9001: C# Benchmark (http://127.0.0.1:9001)
# Port 19000: Swift Benchmark

echo "🚀 Building C# Benchmark..."
dotnet build samples/CosmoApiBenchHost/CosmoApiBenchHost.csproj -c Release --nologo > /dev/null

echo "🚀 Building Swift Benchmark..."
cd ../CosmoApiServer-Swift
swift build -c release --target CosmoApiServerBench > /dev/null
cd ../CosmoApiServer

echo ""
echo "🔥 Starting benchmark hosts..."
dotnet run --project samples/CosmoApiBenchHost/CosmoApiBenchHost.csproj -c Release --no-build > /dev/null &
CS_PID=\$!

cd ../CosmoApiServer-Swift
swift run -c release CosmoApiServerBench > /dev/null &
SWIFT_PID=\$!
cd ../CosmoApiServer

# Wait for servers to be ready
echo "⏳ Waiting for servers to initialize..."
sleep 5

echo ""
echo "📊 Running C# Benchmark (Port 9001)..."
dotnet run --project tests/ApiServer.Benchmark/ApiServer.Benchmark.csproj -c Release -- http://127.0.0.1:9001

echo ""
echo "📊 Running Swift Benchmark (Port 19000)..."
# Pass URL directly
dotnet run --project tests/ApiServer.Benchmark/ApiServer.Benchmark.csproj -c Release -- "http://127.0.0.1:19000"

echo ""
echo "🛑 Shutting down hosts..."
kill \$CS_PID \$SWIFT_PID
