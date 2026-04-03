#!/usr/bin/env bash
# HTTP/3 interop validation script
# Requires curl with HTTP/3 support (brew install curl-quiche or curl >= 7.88 with quiche/ngtcp2)
#
# Usage:
#   ./test_http3_interop.sh [host] [port]
#   ./test_http3_interop.sh localhost 5001
#
# The server must be running with UseHttps() + UseHttp3() on the specified port.
# Self-signed cert: pass -k / --insecure flag (set INSECURE=1).

HOST="${1:-localhost}"
PORT="${2:-5001}"
BASE="https://${HOST}:${PORT}"
CURL_FLAGS="--http3 --silent --show-error --max-time 10"
[[ "${INSECURE:-0}" == "1" ]] && CURL_FLAGS+=" --insecure"

PASS=0
FAIL=0

check() {
    local name="$1"; shift
    local expected_code="$1"; shift
    local actual_code
    actual_code=$(curl $CURL_FLAGS --write-out "%{http_code}" --output /tmp/h3_body.txt "$@" 2>/tmp/h3_err.txt)
    local exit_code=$?
    if [[ $exit_code -ne 0 ]]; then
        echo "FAIL [$name]: curl error $exit_code: $(cat /tmp/h3_err.txt)"
        ((FAIL++))
    elif [[ "$actual_code" != "$expected_code" ]]; then
        echo "FAIL [$name]: expected HTTP $expected_code, got $actual_code"
        ((FAIL++))
    else
        echo "PASS [$name]: HTTP $actual_code"
        ((PASS++))
    fi
}

check_body() {
    local name="$1"; shift
    local expected_code="$1"; shift
    local expected_pattern="$1"; shift
    local actual_code
    actual_code=$(curl $CURL_FLAGS --write-out "%{http_code}" --output /tmp/h3_body.txt "$@" 2>/tmp/h3_err.txt)
    local exit_code=$?
    if [[ $exit_code -ne 0 ]]; then
        echo "FAIL [$name]: curl error $exit_code: $(cat /tmp/h3_err.txt)"
        ((FAIL++))
    elif [[ "$actual_code" != "$expected_code" ]]; then
        echo "FAIL [$name]: expected HTTP $expected_code, got $actual_code"
        ((FAIL++))
    elif ! grep -q "$expected_pattern" /tmp/h3_body.txt; then
        echo "FAIL [$name]: body missing pattern '$expected_pattern' (got: $(head -c 200 /tmp/h3_body.txt))"
        ((FAIL++))
    else
        echo "PASS [$name]: HTTP $actual_code, body matches '$expected_pattern'"
        ((PASS++))
    fi
}

echo "=== HTTP/3 Interop Tests: ${BASE} ==="
echo ""

# ── Basic requests ────────────────────────────────────────────────────────────
check          "GET /ping"                   200  "${BASE}/ping"
check_body     "GET /ping body"              200  "pong"                  "${BASE}/ping"
check          "GET /json"                   200  "${BASE}/json"
check          "POST /echo"                  200  "${BASE}/echo"  -d '{"msg":"hello"}' -H "Content-Type: application/json"
check          "GET /health"                 200  "${BASE}/health"
check          "GET /openapi.json"           200  "${BASE}/openapi.json"
check          "GET /swagger"               200  "${BASE}/swagger"

# ── Streaming ─────────────────────────────────────────────────────────────────
check          "GET /stream"                 200  "${BASE}/stream"
check          "GET /large-json"             200  "${BASE}/large-json"
check          "GET /file"                   200  "${BASE}/file"

# ── Static files ──────────────────────────────────────────────────────────────
check          "GET /index.html"             200  "${BASE}/index.html"

# ── HEAD ──────────────────────────────────────────────────────────────────────
check          "HEAD /ping"                  200  "${BASE}/ping"  -I

# ── Range request ─────────────────────────────────────────────────────────────
check          "Range /file bytes=0-99"      206  "${BASE}/file"  -H "Range: bytes=0-99"

# ── Auth ──────────────────────────────────────────────────────────────────────
check          "GET /protected (no token)"   401  "${BASE}/protected"

# ── Error handling ────────────────────────────────────────────────────────────
check          "GET /not-found"              404  "${BASE}/not-found"

# ── Repeated large responses (stream abort regression) ────────────────────────
echo ""
echo "--- Regression: repeated large responses (QuicException stream abort) ---"
for i in $(seq 1 10); do
    check "GET /large-json #${i}" 200 "${BASE}/large-json"
done
for i in $(seq 1 10); do
    check "GET /stream #${i}" 200 "${BASE}/stream"
done

# ── Summary ───────────────────────────────────────────────────────────────────
echo ""
echo "=== Results: PASS=$PASS FAIL=$FAIL ==="
[[ $FAIL -eq 0 ]] && exit 0 || exit 1
