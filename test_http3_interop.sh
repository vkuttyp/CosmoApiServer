#!/usr/bin/env bash
# HTTP/3 interop validation script
#
# Usage:
#   ./test_http3_interop.sh [host] [port]
#   INSECURE=1 ./test_http3_interop.sh localhost 5001
#
# Requires curl with HTTP/3 support. On macOS:
#   brew install curl
# Then the script will automatically use /opt/homebrew/opt/curl/bin/curl.
#
# The server must be running with UseHttps() + UseHttp3().

HOST="${1:-localhost}"
PORT="${2:-5001}"
BASE="https://${HOST}:${PORT}"

# Prefer Homebrew curl (has HTTP/3 via ngtcp2) over system curl (does not)
CURL_BIN="curl"
for candidate in /opt/homebrew/opt/curl/bin/curl /usr/local/opt/curl/bin/curl; do
    if [[ -x "$candidate" ]] && "$candidate" --version 2>/dev/null | grep -q "ngtcp2\|quiche\|nghttp3"; then
        CURL_BIN="$candidate"
        break
    fi
done

# Verify HTTP/3 support
if ! "$CURL_BIN" --version 2>/dev/null | grep -qE "ngtcp2|quiche|nghttp3"; then
    echo "ERROR: $CURL_BIN does not support HTTP/3."
    echo "Install with: brew install curl"
    echo "Then re-run this script."
    exit 1
fi

echo "Using: $CURL_BIN ($(${CURL_BIN} --version | head -1))"

CURL_FLAGS="--http3 --silent --show-error --max-time 10"
[[ "${INSECURE:-0}" == "1" ]] && CURL_FLAGS+=" --insecure"

PASS=0
FAIL=0
SKIP=0

check() {
    local name="$1"; shift
    local expected_code="$1"; shift
    local actual_code
    actual_code=$("$CURL_BIN" $CURL_FLAGS --write-out "%{http_code}" --output /tmp/h3_body.txt "$@" 2>/tmp/h3_err.txt)
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
    actual_code=$("$CURL_BIN" $CURL_FLAGS --write-out "%{http_code}" --output /tmp/h3_body.txt "$@" 2>/tmp/h3_err.txt)
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

# ── Core transport (required — all servers) ───────────────────────────────────
echo "--- Core transport ---"
check          "GET /ping"                   200  "${BASE}/ping"
check_body     "GET /ping body"              200  "pong"                  "${BASE}/ping"
check          "GET /json"                   200  "${BASE}/json"
check          "POST /echo"                  200  "${BASE}/echo"  -d '{"msg":"hello"}' -H "Content-Type: application/json"
check          "GET /not-found → 404"        404  "${BASE}/this-does-not-exist"

# ── Streaming ─────────────────────────────────────────────────────────────────
echo ""
echo "--- Streaming ---"
check          "GET /stream"                 200  "${BASE}/stream"
check          "GET /large-json"             200  "${BASE}/large-json"
check          "GET /file"                   200  "${BASE}/file"

# ── Optional features (skip if server not configured) ─────────────────────────
check_optional() {
    local name="$1" expected_code="$2"; shift 2
    local actual_code
    actual_code=$("$CURL_BIN" $CURL_FLAGS --write-out "%{http_code}" --output /dev/null "$@" 2>/dev/null)
    if [[ "$actual_code" == "000" || "$actual_code" == "404" && "$expected_code" != "404" ]]; then
        echo "SKIP [$name]: not configured on this server (got $actual_code)"
        ((SKIP++))
    elif [[ "$actual_code" == "$expected_code" ]]; then
        echo "PASS [$name]: HTTP $actual_code"
        ((PASS++))
    else
        echo "FAIL [$name]: expected HTTP $expected_code, got $actual_code"
        ((FAIL++))
    fi
}

echo ""
echo "--- Optional features ---"
check_optional "GET /health"                 200  "${BASE}/health"
check_optional "GET /openapi.json"           200  "${BASE}/openapi.json"
check_optional "GET /swagger"                200  "${BASE}/swagger"
check_optional "GET /index.html"             200  "${BASE}/index.html"
check_optional "HEAD /ping"                  200  "${BASE}/ping"  --head
check_optional "Range /file bytes=0-99"      206  "${BASE}/file-static"  -H "Range: bytes=0-99"
check_optional "GET /protected (no token)"   401  "${BASE}/protected"

# ── Regression: repeated large responses (QuicException stream abort) ─────────
echo ""
echo "--- Regression: repeated large responses (stream abort fix v2.1.1) ---"
for i in $(seq 1 10); do
    check "GET /large-json #${i}" 200 "${BASE}/large-json"
done
for i in $(seq 1 10); do
    check "GET /stream #${i}" 200 "${BASE}/stream"
done

# ── Summary ───────────────────────────────────────────────────────────────────
echo ""
echo "=== Results: PASS=$PASS FAIL=$FAIL SKIP=$SKIP ==="
[[ $FAIL -eq 0 ]] && exit 0 || exit 1
