---
name: HTTP/3 optimization work
description: Current state, known bugs, and optimization targets for CosmoApiServer HTTP/3 transport
type: project
---

Phase 6 (Interop + Performance) optimization pass is complete. Phases 1–5 are complete.

**Why:** Moving HTTP/3 from experimental to production-ready. Phase 6 goal is stable interop + published benchmarks.

**How to apply:** All known allocation and stability bugs in Http3Connection.cs are fixed. Remaining work is external interop validation and benchmark publishing — avoid re-litigating completed changes.

## Completed in Phase 6
- `WriteFrameAsync` stability fix: `FlushAsync` guarded by `if (!completeWrites)`
- `ReadVarIntAsync` / `WriteVarIntAsync`: now use `ArrayPool<byte>` (no heap alloc per call)
- Frame header in `WriteFrameAsync`: uses `ArrayPool<byte>` (was `GC.AllocateUninitializedArray`)
- `EncodeResponseHeaders`: uses `ArrayBufferWriter<byte>` end-to-end (no `MemoryStream.ToArray()`)
- SETTINGS encoding: uses stack-local `byte[16]` (no `MemoryStream`)
- `QpackEncoderState` (new file): per-connection dynamic table for response QPACK encoding
- `Http3DataFrameStream.FlushAsync`: payloads ≤ 8 KB coalesced into one `WriteAsync` (2 async ops vs 3)
- `MaxRequestsPerConnection` raised to 100 (was 16)
- `BufferedDataFrameChunkSize` raised to 32 KB (was 4 KB)

## Remaining TODO
- Validate interop: `curl --http3`, browsers, reverse proxies
- Investigate stream reuse under repeated large responses (QuicException: Stream aborted by peer (268) seen in benchmark stderr — does not affect request success count but may surface under heavier load)
- `TryReadFrameAsync` still uses `GC.AllocateUninitializedArray<byte>` per frame — skipped due to lifetime complexity, low priority

## Benchmark numbers (macOS arm64, .NET 10, 2026-04-03, v2.0.7)
HTTP/1.1 Cosmo vs ASP.NET: **10/10 wins**. ping=10905 vs 9671, echo=11087 vs 7819, stream=12642 vs 9775 (+29%), file=6165 vs 4446.
/stream fix: flush-coalescing (flushToSocket:false in ChunkedBodyStream) — all NDJSON chunks + terminator in one syscall.

## Benchmark numbers (Windows 11 VM, .NET 10, 2026-04-03, v2.0.7)
HTTP/3 all-1000/1000. Key ops/sec: ping=2481, json=2605, query=3276, headers=6116, stream=2581, file=1464.
HTTP/1.1 Cosmo vs ASP.NET: **10/10 wins**. ping=7605 vs 6154, echo=7184 vs 6274, stream=8177 vs 6892 (+19%), file=3422 vs 2150.
/stream fixed by flush-coalescing (flushToSocket:false) — was -20%, now +19%.
