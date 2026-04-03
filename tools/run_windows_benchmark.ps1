$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$outDir = 'C:\Windows\Temp\cosmo-bench'

$certPath = Join-Path $env:TEMP 'cosmoapi-h3-devcert.pfx'
dotnet dev-certs https -ep $certPath -p password | Out-Null

function Publish-Project($project, $dest) {
    dotnet publish "$repo\$project" -c Release -o $dest --nologo | Out-Host
}

function Start-Published($exe, $envMap) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exe
    $psi.WorkingDirectory = Split-Path $exe
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    foreach ($k in $envMap.Keys) { $psi.Environment[$k] = $envMap[$k] }
    $p = New-Object System.Diagnostics.Process
    $p.StartInfo = $psi
    $null = $p.Start()
    return $p
}

function Wait-Ready($addr, $port, $maxSec = 60) {
    $deadline = (Get-Date).AddSeconds($maxSec)
    while ((Get-Date) -lt $deadline) {
        try {
            $tcp = New-Object System.Net.Sockets.TcpClient
            $tcp.Connect($addr, $port)
            $tcp.Close()
            return
        } catch {}
        Start-Sleep -Milliseconds 300
    }
    throw "Timed out waiting for ${addr}:${port}"
}

function Kill-All($processes) {
    foreach ($p in $processes) {
        if ($null -ne $p -and -not $p.HasExited) {
            try { taskkill /F /T /PID $p.Id 2>$null | Out-Null } catch {}
        }
    }
    Start-Sleep -Milliseconds 500
}

function Dump-ProcessOutput($label, $process) {
    if ($null -eq $process) { return }
    $null = $process.WaitForExit(3000)
    Write-Host "=== $label stdout ==="
    try { $process.StandardOutput.ReadToEnd() | Out-Host } catch {}
    Write-Host "=== $label stderr ==="
    try { $process.StandardError.ReadToEnd() | Out-Host } catch {}
}

$cosmo = $null
$asp   = $null
$h3    = $null

try {
    if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }

    Publish-Project 'samples\CosmoApiBenchHost\CosmoApiBenchHost.csproj'  "$outDir\cosmo"
    Publish-Project 'samples\AspNetBenchHost\AspNetBenchHost.csproj'       "$outDir\asp"
    Publish-Project 'tests\ApiServer.Benchmark\ApiServer.Benchmark.csproj' "$outDir\bench"

    $cosmo = Start-Published "$outDir\cosmo\CosmoApiBenchHost.exe" @{ COSMO_BENCH_PORT = '9102' }
    $asp   = Start-Published "$outDir\asp\AspNetBenchHost.exe"     @{}
        $h3    = Start-Published "$outDir\cosmo\CosmoApiBenchHost.exe" @{
            COSMO_BENCH_PORT          = '9443'
            COSMO_BENCH_CERT_PATH     = $certPath
            COSMO_BENCH_CERT_PASSWORD = 'password'
            COSMO_BENCH_ENABLE_HTTP3  = 'true'
            COSMO_HTTP3_SUPPRESS_ABORT_LOGS = '1'
        }

    try {
        Wait-Ready '127.0.0.1' 9102
        Wait-Ready '127.0.0.1' 9103
    }
    catch {
        Write-Host "ERROR: readiness check failed: $_"
        Kill-All @($cosmo, $asp, $h3)
        Dump-ProcessOutput 'Cosmo host' $cosmo
        Dump-ProcessOutput 'AspNet host' $asp
        Dump-ProcessOutput 'HTTP/3 host' $h3
        throw
    }

    Write-Host 'Waiting 5 s for HTTP/3 QUIC init'
    Start-Sleep -Seconds 5

    Write-Host '=== CosmoApiServer (Windows) ==='
    & "$outDir\bench\ApiServer.Benchmark.exe" CosmoApiServer

    Write-Host '=== AspNetCore (Windows) ==='
    & "$outDir\bench\ApiServer.Benchmark.exe" AspNetCore

    Write-Host '=== CosmoApiServer HTTP/3 (Windows) ==='
    & "$outDir\bench\ApiServer.Benchmark.exe" CosmoApiServerHttp3
}
finally {
    Kill-All @($cosmo, $asp, $h3)
    Dump-ProcessOutput 'Cosmo host'  $cosmo
    Dump-ProcessOutput 'AspNet host' $asp
    Dump-ProcessOutput 'HTTP/3 host' $h3
}
