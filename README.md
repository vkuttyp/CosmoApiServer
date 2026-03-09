# CosmoApiServer

A high-performance, zero-dependency HTTP server framework for .NET 10, built entirely on `System.IO.Pipelines` and `System.Net.Sockets`.

No DotNetty. No ASP.NET. No Kestrel. Just raw sockets → pipes → your handlers.

---

## Benchmark

ApacheBench · c=50 concurrent · n=5,000 requests · keep-alive · macOS arm64

| Scenario | CosmoApiServer | ASP.NET Core Kestrel | Advantage |
|---|---|---|---|
| GET /ping | **53,159 req/s** | 17,053 req/s | **+212%** |
| GET /items (20-item list) | **42,752 req/s** | 15,759 req/s | **+171%** |
| POST /echo (JSON body) | **54,624 req/s** | 17,943 req/s | **+204%** |

Zero failed requests. Performance holds at c=200 (53,874 req/s).

### Razor Component Rendering (100-row table)
| Framework | Throughput | P50 Latency | Advantage |
|---|---|---|---|
| **CosmoApiServer** | **4,235 ops/sec** | **0.24 ms** | **+141%** |
| Blazor SSR (Static) | 1,754 ops/sec | 0.57 ms | Baseline |

---

## Why so fast?

Traditional .NET HTTP servers (including Kestrel and DotNetty-based servers) have at least one thread-pool context switch per request. CosmoApiServer eliminates this:

```
Socket → PipeWriter → PipeReader → Parser → Middleware → PipeWriter → Socket
```

Everything runs inline on the connection task. No EventLoop→ThreadPool hand-off. No intermediate byte arrays. No string allocation on the hot path.

Key design decisions:

- **Zero-copy parsing** — `Http11Parser` uses `ReadOnlySpan<byte>` and `SequenceReader<byte>` directly over the pipe buffer.
- **Zero-allocation Headers** — Headers are stored as `ReadOnlyMemory<byte>` and only materialized to strings if accessed.
- **Lazy DI scope** — `LazyScopeProvider` only calls `IServiceProvider.CreateScope()` if a service is actually resolved.
- **Object Pooling** — `HttpContext`, `HttpRequest`, and `HttpResponse` are pooled and reused to eliminate GC pressure.
- **Async State Machine Rendering** — `RenderTreeBuilder` uses a struct-based command buffer for non-blocking, high-speed SSR.
- **Span-based routing** — `RouteTable` uses a `ConcurrentDictionary` cache and span-based matching for O(1) lookups.

---

## Features

- HTTP/1.1 keep-alive (pipelined)
- HTTP/2 (h2c cleartext + ALPN over TLS)
- TLS via `SslStream` with ALPN (`h2` / `http/1.1`)
- **Razor Components** — Full `.razor` support with `@page`, `[Parameter]`, and `CascadingParameters`
- **Routable Components** — Components can define their own routes via `@page` without a controller
- Attribute-based controllers (`[HttpGet]`, `[HttpPost]`, `[Route]`, `[Authorize]`)
- Convention-based routing (`MapGet`, `MapPost`, …)
- JSON request/response (`WriteJson`, `ReadJson<T>`)
- `IAsyncEnumerable<T>` → NDJSON streaming response
- Middleware pipeline (`UseLogging`, `UseCors`, `UseJwtAuthentication`, custom `IMiddleware`)
- WebSockets (`HttpContext.AcceptWebSocketAsync()`)
- OpenAPI & Swagger UI auto-generation
- Security Middlewares (CSRF, HSTS, HTTPS Redirection, CSRF Validation)
- Model Validation via DataAnnotations (Controllers & Components)
- **Zero-Copy File Serving** — `HttpResponse.SendFileAsync()` streams directly from disk to socket

---

## Razor Components

`CosmoApiServer` includes a first-class implementation of Razor Components (similar to Blazor SSR). This provides the power of Razor syntax (C# + HTML) with the performance of a zero-dependency framework.

### Why Razor Components?
- **High Performance:** Renders directly to `CosmoApiServer`'s `HttpResponse` buffers using an optimized async state machine.
- **Routable:** Use `@page "/my-route"` directly in your `.razor` files.
- **Cascading Parameters:** Share state (like `ModelState` or `User`) down the component tree automatically.
- **Validation:** Support for `DataAnnotations` with built-in `<ValidationSummary />`.

### Usage
Create a `.razor` file in your project:
```razor
@page "/hello/{Name}"
@inherits Microsoft.AspNetCore.Components.ComponentBase

<h1>Hello, @Name!</h1>

<div class="alert alert-success">
    Current Time: @DateTime.Now
</div>

@code {
    [Parameter] public string Name { get; set; }
}
```

Enable in **Program.cs**:
```csharp
var builder = CosmoWebApplicationBuilder.Create()
    .AddRazorComponents(); // Scans for @page components
```

---

## Projects in this repository

| Project | Description |
|---|---|
| `src/CosmoApiServer.Core` | Core framework library |
| `samples/BlazorSqlSample` | Replicated Blazor structure with SQL streaming and components |
| `samples/WeatherApp` | Full REST API: JWT auth, DI, streaming, CosmoSQLClient |
| `templates/CosmoRazorServerTemplate` | `dotnet new cosmorazor` template |
| `tests/CosmoApiServer.Core.Tests` | 68+ unit tests for routing, middleware, components, validation |

---

## Credits

**Razor Components** in CosmoApiServer is inspired by **Microsoft Blazor**, but implemented as a lightweight, SSR-only engine focused on raw performance and zero dependencies. 

Portions of the templating engine are based on the excellent **[RazorSlices](https://github.com/DamianEdwards/RazorSlices)** project by **Damian Edwards**, licensed under the **MIT License**.
