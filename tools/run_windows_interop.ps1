#Requires -Version 5.1
# HTTP/3 interop validation for Windows
# Uses the H3Interop .NET tool (tools/H3Interop) which runs on .NET 10.
# Run via: powershell -ExecutionPolicy Bypass -File tools\run_windows_interop.ps1

$ErrorActionPreference = 'Stop'

$repo      = Split-Path -Parent $PSScriptRoot
$serverDir = 'C:\Windows\Temp\cosmo-interop-server'
$toolDir   = 'C:\Windows\Temp\cosmo-interop-tool'
$port      = 9443
$base      = "https://localhost:$port"

# ── Cert ──────────────────────────────────────────────────────────────────────
$certPath = Join-Path $env:TEMP 'cosmoapi-h3-interop.pfx'
dotnet dev-certs https -ep $certPath -p password | Out-Null

# ── Build & publish ────────────────────────────────────────────────────────────
Write-Host "Building bench host..."
dotnet publish "$repo\samples\CosmoApiBenchHost\CosmoApiBenchHost.csproj" -c Release -o $serverDir --nologo | Out-Host

Write-Host "Building H3Interop tool..."
dotnet publish "$repo\tools\H3Interop\H3Interop.csproj" -c Release -o $toolDir --nologo | Out-Host

# ── Start server ───────────────────────────────────────────────────────────────
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName         = "$serverDir\CosmoApiBenchHost.exe"
$psi.WorkingDirectory = $serverDir
$psi.UseShellExecute  = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError  = $true
$psi.Environment['COSMO_BENCH_PORT']          = "$port"
$psi.Environment['COSMO_BENCH_CERT_PATH']     = $certPath
$psi.Environment['COSMO_BENCH_CERT_PASSWORD'] = 'password'
$psi.Environment['COSMO_BENCH_ENABLE_HTTP3']  = 'true'

$server = New-Object System.Diagnostics.Process
$server.StartInfo = $psi
$null = $server.Start()

Write-Host "Waiting for HTTP/3 server (QUIC)..."
Start-Sleep -Seconds 5

# ── Run interop tool ───────────────────────────────────────────────────────────
try {
    & "$toolDir\H3Interop.exe" $base 10
    $exitCode = $LASTEXITCODE
} finally {
    try { taskkill /F /T /PID $server.Id 2>$null | Out-Null } catch {}
}

exit $exitCode
