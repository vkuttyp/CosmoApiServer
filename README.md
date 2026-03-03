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

---

## Why so fast?

Traditional .NET HTTP servers (including Kestrel and DotNetty-based servers) have at least one thread-pool context switch per request. CosmoApiServer eliminates this:

```
Socket → PipeWriter → PipeReader → Parser → Middleware → PipeWriter → Socket
```

Everything runs inline on the connection task. No EventLoop→ThreadPool hand-off. No intermediate byte arrays. No string allocation on the hot path.

Key design decisions:

- **Zero-copy parsing** — `Http11Parser` uses `SequenceReader<byte>` directly over the pipe buffer; no `string.Split`, no intermediate `byte[]`.
- **Lazy headers** — `ParsedHeaderDict` wraps the raw parsed header list; the backing dictionary is only materialised if your code iterates all headers.
- **Lazy DI scope** — `LazyScopeProvider` only calls `IServiceProvider.CreateScope()` if a service is actually resolved.
- **Span-based routing** — `RouteTemplate.TryMatch()` walks path segments with `ReadOnlySpan<char>`, no heap allocation for parameterless routes.
- **Pre-computed CORS strings** — `Allow-Methods` / `Allow-Headers` values are built once at startup.
- **Content-Length cache** — small integer → ASCII string is cached to avoid per-request allocation.

---

## Features

- HTTP/1.1 keep-alive (pipelined)
- HTTP/2 (h2c cleartext + ALPN over TLS)
- TLS via `SslStream` with ALPN (`h2` / `http/1.1`)
- Attribute-based controllers (`[HttpGet]`, `[HttpPost]`, `[Route]`, `[Authorize]`)
- Convention-based routing (`MapGet`, `MapPost`, …)
- Route parameters (`/items/{id}`)
- Query string parsing
- JSON request/response (`WriteJson`, `ReadJson<T>`)
- `IAsyncEnumerable<T>` → NDJSON streaming response
- Middleware pipeline (`UseLogging`, `UseCors`, `UseJwtAuthentication`, custom `IMiddleware`)
- Dependency injection (`Microsoft.Extensions.DependencyInjection`)
- JWT authentication (`[Authorize]`, `HttpContext.User`)
- CORS with pre-computed headers
- IPv4 + IPv6 dual-stack

---

## Quick Start

```bash
dotnet new console -n MyApp
cd MyApp
dotnet add reference /path/to/src/CosmoApiServer.Core/CosmoApiServer.Core.csproj
```

### Hello World

```csharp
using CosmoApiServer.Core.Hosting;

var app = CosmoWebApplicationBuilder.Create()
    .ListenOn(8080)
    .Build();

app.MapGet("/hello", async ctx =>
    ctx.Response.WriteJson(new { message = "Hello, World!" }));

app.Run();
```

### With middleware, DI, and controllers

```csharp
using CosmoApiServer.Core.Hosting;
using Microsoft.Extensions.DependencyInjection;

var builder = CosmoWebApplicationBuilder.Create()
    .ListenOn(8080)
    .UseLogging()
    .UseCors()
    .UseJwtAuthentication(opts =>
    {
        opts.SecretKey = "your-secret-key-32-chars-minimum!";
        opts.Issuer    = "my-app";
        opts.Audience  = "my-app";
    })
    .AddControllers();   // scans current assembly for [HttpGet]/[HttpPost] controllers

builder.Services.AddSingleton<IMyService, MyService>();

var app = builder.Build();

app.MapGet("/health", async ctx =>
    ctx.Response.WriteJson(new { status = "ok" }));

app.Run();
```

### TLS + HTTP/2

```csharp
var app = CosmoWebApplicationBuilder.Create()
    .ListenOn(8443)
    .UseHttps("./certs/server.pfx", "changeme")
    .UseHttp2()          // enables ALPN h2/http1.1 negotiation
    .Build();

app.Run();
```

---

## Routing

### Convention routing

```csharp
app.MapGet("/",              async ctx => { ... });
app.MapGet("/items/{id}",    async ctx =>
{
    var id = ctx.Request.RouteValues["id"];
    var q  = ctx.Request.Query["filter"];   // query string
    ctx.Response.WriteJson(new { id, q });
});
app.MapPost("/items",        async ctx => { ... });
app.MapPut("/items/{id}",    async ctx => { ... });
app.MapDelete("/items/{id}", async ctx => { ... });
app.MapPatch("/items/{id}",  async ctx => { ... });
```

### Attribute-based controllers

```csharp
using CosmoApiServer.Core.Controllers;

[Route("/api/products")]
public class ProductController
{
    private readonly IProductService _svc;
    public ProductController(IProductService svc) => _svc = svc;

    [HttpGet("")]
    public async Task<IEnumerable<Product>> GetAll() =>
        await _svc.GetAllAsync();

    [HttpGet("{id}")]
    public async Task<Product?> GetById(int id) =>
        await _svc.GetByIdAsync(id);

    [HttpPost("")]
    [Authorize]
    public async Task<Product> Create([FromBody] CreateProductRequest req) =>
        await _svc.CreateAsync(req);

    [HttpDelete("{id}")]
    [Authorize]
    public async Task Delete(int id) =>
        await _svc.DeleteAsync(id);
}
```

**Supported action return types:**

| Return type | Behaviour |
|---|---|
| `void` / `Task` | 200 OK, empty body |
| `T` / `Task<T>` | 200 OK, JSON-serialised |
| `IActionResult` | status + body determined by result |
| `IAsyncEnumerable<T>` | 200 OK, chunked NDJSON stream |

---

## Request & Response API

```csharp
// Request
string path       = ctx.Request.Path;
HttpMethod method = ctx.Request.Method;
string? header    = ctx.Request.Headers["Content-Type"];
string? query     = ctx.Request.Query["page"];
string? routeVal  = ctx.Request.RouteValues["id"];
byte[]  body      = ctx.Request.Body;
T?      obj       = ctx.Request.ReadJson<T>();

// Response
ctx.Response.StatusCode = 201;
ctx.Response.Headers["X-Custom"] = "value";
ctx.Response.WriteText("plain text");
ctx.Response.WriteJson(new { id = 1 });
ctx.Response.WriteBytes(bytes, "application/octet-stream");

// User (JWT, set by UseJwtAuthentication)
ClaimsPrincipal? user = ctx.User;
string? userId        = ctx.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
```

---

## Middleware

### Built-in middleware

| Method | Effect |
|---|---|
| `.UseLogging()` | Logs method, path, status, duration for every request |
| `.UseCors()` | Adds CORS headers; handles OPTIONS preflight |
| `.UseJwtAuthentication(opts)` | Validates Bearer token; sets `ctx.User` |

### Custom middleware

```csharp
using CosmoApiServer.Core.Middleware;

public class TimingMiddleware : IMiddleware
{
    public async Task InvokeAsync(HttpContext ctx, RequestDelegate next)
    {
        var sw = Stopwatch.StartNew();
        await next(ctx);
        ctx.Response.Headers["X-Elapsed-Ms"] = sw.ElapsedMilliseconds.ToString();
    }
}

// Register
builder.UseMiddleware(new TimingMiddleware());
```

---

## Streaming with IAsyncEnumerable

Return `IAsyncEnumerable<T>` from a controller action to stream NDJSON line-by-line:

```csharp
[HttpGet("stream")]
public async IAsyncEnumerable<Product> StreamAll()
{
    await foreach (var product in _svc.GetAllStreamingAsync())
        yield return product;
}
```

The transport writes chunked transfer-encoding (HTTP/1.1) or DATA frames (HTTP/2) automatically. The connection closes after the stream completes.

---

## JWT Authentication

```csharp
// Configure
builder.UseJwtAuthentication(opts =>
{
    opts.SecretKey = "your-32-char-minimum-secret-key!";
    opts.Issuer    = "my-app";
    opts.Audience  = "my-app";
    opts.ExpiryMinutes = 60;
});

// Issue a token (in a login controller)
[HttpPost("")]
[Route("/auth/login")]
public IActionResult Login([FromBody] LoginRequest req)
{
    if (!_authService.Validate(req.Username, req.Password))
        return new StatusCodeResult(401);

    var claims = new[] { new Claim(ClaimTypes.NameIdentifier, req.Username) };
    var token  = JwtTokenHelper.Generate(claims, _jwtOptions);
    return new JsonResult(new { token });
}

// Protect a route
[HttpGet("")]
[Route("/api/me")]
[Authorize]
public object GetMe(ClaimsPrincipal user) =>
    new { id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value };
```

---

## Architecture

```
CosmoApiServer.Core
├── Transport/
│   ├── PipelineHttpServer.cs   TCP accept loop (Socket.AcceptAsync)
│   │                           TLS via SslStream, ALPN h2/http1.1
│   ├── Http11Connection.cs     Per-connection HTTP/1.1 keep-alive loop
│   │                           h2c preface detection → Http2Connection
│   ├── Http11Parser.cs         Zero-alloc parser (SequenceReader<byte>)
│   ├── Http11Writer.cs         Response writer direct to PipeWriter
│   ├── Http2Connection.cs      HTTP/2 frame dispatch + stream multiplexing
│   ├── HpackDecoder.cs         RFC 7541 HPACK + Huffman codec
│   └── StreamingBodyWriter.cs  IAsyncEnumerable → NDJSON chunked stream
├── Hosting/
│   ├── CosmoWebApplicationBuilder.cs   Fluent builder (services, options)
│   └── CosmoWebApplication.cs          Wires middleware + starts server
├── Middleware/
│   ├── MiddlewarePipeline.cs
│   ├── RouterMiddleware.cs
│   ├── LoggingMiddleware.cs
│   ├── CorsMiddleware.cs
│   └── JwtMiddleware.cs
├── Controllers/
│   └── ControllerScanner.cs    Reflection-based attribute controller scan
├── Routing/
│   ├── RouteTable.cs           Method-keyed O(1) route lookup
│   └── RouteTemplate.cs        Span-based path matching
├── Auth/
│   └── JwtTokenHelper.cs
└── Http/
    ├── HttpContext.cs
    ├── HttpRequest.cs
    ├── HttpResponse.cs
    └── HttpMethod.cs
```

---

## Projects in this repository

| Project | Description |
|---|---|
| `src/CosmoApiServer.Core` | Core framework library |
| `src/CosmoS3` | Amazon S3–compatible object storage built on CosmoApiServer |
| `samples/HelloWorldSample` | Minimal hello world + controller demo |
| `samples/WeatherApp` | Full REST API: JWT auth, DI, streaming, CosmoSQLClient |
| `samples/CosmoS3Host` | Standalone CosmoS3 server host |
| `samples/CosmoApiBenchHost` | Benchmark server (port 9001) |
| `samples/AspNetBenchHost` | ASP.NET Core equivalent for comparison (port 9002) |
| `tests/CosmoApiServer.Core.Tests` | Unit tests for routing, middleware, JWT |
| `tests/CosmoS3.Tests` | S3 integration tests (requires running CosmoS3Host) |
| `tests/ApiServer.Benchmark` | Sequential HTTP benchmark tool |

---

## Dependencies

`CosmoApiServer.Core` has two NuGet dependencies:

| Package | Purpose |
|---|---|
| `Microsoft.Extensions.DependencyInjection` | DI container |
| `System.IdentityModel.Tokens.Jwt` | JWT validation / issuance |

Everything else — HTTP parsing, TLS, HTTP/2, HPACK, routing, middleware — is implemented in-repo using only .NET 10 BCL types (`System.IO.Pipelines`, `System.Net.Sockets`, `System.Buffers`).

---

## Related projects

- **[CosmoS3](src/CosmoS3/README.md)** — Amazon S3–compatible middleware built on CosmoApiServer
- **[CosmoSQLClient](https://github.com/vkuttyp/CosmoSQLClient-Dotnet)** — Zero-dependency SQL client (SQL Server, PostgreSQL, MySQL, SQLite) used by WeatherApp and CosmoS3
