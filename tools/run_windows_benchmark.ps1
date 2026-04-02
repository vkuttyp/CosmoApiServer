$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

$certPath = Join-Path $env:TEMP 'cosmoapi-h3-devcert.pfx'
dotnet dev-certs https -ep $certPath -p password | Out-Null

function Start-Host($project, $envMap) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'dotnet'
    $psi.WorkingDirectory = $repo
    $psi.Arguments = "run --project $project -c Release --no-build"
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    foreach ($k in $envMap.Keys) {
        $psi.Environment[$k] = $envMap[$k]
    }
    $p = New-Object System.Diagnostics.Process
    $p.StartInfo = $psi
    $null = $p.Start()
    return $p
}

function Wait-Ready($url, $maxSec = 30) {
    $deadline = (Get-Date).AddSeconds($maxSec)
    while ((Get-Date) -lt $deadline) {
        try {
            $resp = Invoke-WebRequest -UseBasicParsing -SkipCertificateCheck -Uri $url -TimeoutSec 2
            if ($resp.StatusCode -ge 200 -and $resp.StatusCode -lt 500) {
                return
            }
        } catch {}
        Start-Sleep -Milliseconds 500
    }
    throw "Timed out waiting for $url"
}

function Dump-ProcessOutput($label, $process) {
    if ($null -eq $process) {
        return
    }

    Write-Host "=== $label stdout ==="
    try {
        if (-not $process.StandardOutput.EndOfStream) {
            $process.StandardOutput.ReadToEnd() | Out-Host
        }
    } catch {}

    Write-Host "=== $label stderr ==="
    try {
        if (-not $process.StandardError.EndOfStream) {
            $process.StandardError.ReadToEnd() | Out-Host
        }
    } catch {}
}

$cosmo = $null
$asp = $null
$h3 = $null

try {
    dotnet build samples/CosmoApiBenchHost/CosmoApiBenchHost.csproj -c Release --nologo | Out-Host
    dotnet build samples/AspNetBenchHost/AspNetBenchHost.csproj -c Release --nologo | Out-Host
    dotnet build tests/ApiServer.Benchmark/ApiServer.Benchmark.csproj -c Release --nologo | Out-Host

    $cosmo = Start-Host 'samples/CosmoApiBenchHost/CosmoApiBenchHost.csproj' @{
        COSMO_BENCH_PORT = '9102'
    }
    $asp = Start-Host 'samples/AspNetBenchHost/AspNetBenchHost.csproj' @{}
    $h3 = Start-Host 'samples/CosmoApiBenchHost/CosmoApiBenchHost.csproj' @{
        ASPNETCORE_URLS = 'https://localhost:9443'
        COSMO_BENCH_PORT = '9443'
        COSMO_BENCH_CERT_PATH = $certPath
        COSMO_BENCH_CERT_PASSWORD = 'password'
        COSMO_BENCH_ENABLE_HTTP3 = 'true'
    }

    try {
        Wait-Ready 'http://127.0.0.1:9102/ping'
        Wait-Ready 'http://127.0.0.1:9103/ping'
        Wait-Ready 'https://localhost:9443/ping'
    }
    catch {
        Dump-ProcessOutput 'Cosmo host' $cosmo
        Dump-ProcessOutput 'AspNet host' $asp
        Dump-ProcessOutput 'HTTP/3 host' $h3
        throw
    }

    Write-Host '=== CosmoApiServer (Windows) ==='
    dotnet run --project tests/ApiServer.Benchmark/ApiServer.Benchmark.csproj -- CosmoApiServer | Out-Host

    Write-Host '=== AspNetCore (Windows) ==='
    dotnet run --project tests/ApiServer.Benchmark/ApiServer.Benchmark.csproj -- AspNetCore | Out-Host

    Write-Host '=== CosmoApiServer HTTP/3 (Windows) ==='
    dotnet run --project tests/ApiServer.Benchmark/ApiServer.Benchmark.csproj -- CosmoApiServerHttp3 | Out-Host

    Dump-ProcessOutput 'Cosmo host' $cosmo
    Dump-ProcessOutput 'AspNet host' $asp
    Dump-ProcessOutput 'HTTP/3 host' $h3
}
finally {
    foreach ($p in @($cosmo, $asp, $h3)) {
        if ($null -ne $p -and -not $p.HasExited) {
            try { $p.Kill($true) } catch {}
        }
    }
}
