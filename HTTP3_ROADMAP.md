# HTTP/3 Roadmap

This document tracks the remaining work to move `CosmoApiServer` HTTP/3 support from experimental to production-ready.

## Current state

Implemented today:

- QUIC listener startup via `UseHttp3()`
- Basic buffered request/response handling
- Streamed request bodies over DATA frames
- NDJSON-style streaming responses over DATA frames
- Request trailers and response trailers
- Dynamic QPACK request decoding, blocked-stream handling, and decoder feedback
- GOAWAY on shutdown and stronger control-stream validation
- Internal HTTP/3 transport tests plus Windows VM benchmark/probe coverage

Still intentionally incomplete:

- Response-side dynamic QPACK encoding beyond static-name/static-entry reuse
- Stable stream reuse under repeated larger responses
- Broader external interop validation with browsers, curl, and proxies
- HTTP/3-specific performance tuning

## Phase 1: QPACK groundwork

Status: completed

Scope:

- Introduce a dedicated QPACK decoder-state object for each HTTP/3 connection
- Parse peer SETTINGS related to QPACK table capacity and blocked streams
- Consume QPACK encoder-stream instructions into connection state
- Keep dynamic field-section references disabled until base/index resolution is complete

Exit criteria:

- Encoder/control streams populate connection-level state
- Transport no longer hardcodes QPACK as a stateless parser
- Unit tests cover dynamic table state mutation

## Phase 2: Dynamic QPACK field resolution

Scope:

- Decode field sections with non-zero Required Insert Count / Base
- Resolve indexed and name-reference dynamic entries
- Support both pre-base and post-base references
- Keep robust failure behavior for invalid insert counts and stale references

Exit criteria:

- Requests from real HTTP/3 clients using dynamic QPACK can be decoded
- Unit tests cover dynamic references across multiple header blocks

Status: completed for request-side decoding

## Phase 3: Trailers

Scope:

- Parse request trailers after DATA frames
- Expose request trailers on `HttpContext`
- Support response trailers for streaming and buffered responses

Exit criteria:

- Request trailers and response trailers round-trip correctly
- Tests cover trailer ordering and protocol violations

Status: completed

## Phase 4: Protocol hardening

Scope:

- Better control-stream validation
- GOAWAY and graceful connection shutdown
- Stricter frame/state validation
- Better mapping of protocol errors to QUIC abort codes

Exit criteria:

- Invalid client behavior is rejected consistently
- Shutdown and stream termination are graceful under load

Status: partially completed

## Phase 5: Feature parity audit

Scope:

- Validate static files, Swagger, auth, forms, large uploads, HEAD, range requests
- Close behavior gaps between HTTP/1.1, HTTP/2, and HTTP/3

Exit criteria:

- Existing user-facing features work the same way over HTTP/3 unless explicitly documented otherwise

Status: largely completed

## Phase 6: Interop and performance

Scope:

- Test with `curl --http3`, browsers, and reverse proxies
- Add HTTP/3 benchmark scenarios
- Reduce allocations in header/frame encode-decode paths
- Tune streaming and body transfer behavior

Exit criteria:

- Stable interop with real clients
- Published benchmark numbers for HTTP/3 scenarios

Status: in progress
