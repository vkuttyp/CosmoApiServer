# CosmoApiServer

A .NET HTTP server built on `System.IO.Pipelines` and `System.Net.Sockets` with support for HTTP/1.1, HTTP/2, and HTTP/3 over QUIC. Includes a middleware pipeline, routing, frontend hosting, and real-time primitives.

---

## Contents

- [Protocols](#protocols)
- [Quick Start](#quick-start)
- [Frontend Integration](#frontend-integration)
- [Real-time](#real-time)
- [Security](#security)
- [Middleware & Pipeline](#middleware--pipeline)
- [Caching](#caching)
- [Diagnostics & Observability](#diagnostics--observability)
- [Razor Components](#razor-components)
- [Samples](#samples)
- [Benchmarks](#benchmarks)
- [Deployment](#deployment)
- [Changelog](#changelog)

---

## Protocols

| Protocol | Support |
|---|---|
| HTTP/1.1 | Keep-alive, pipelining |
| HTTP/2 | h2c cleartext + ALPN over TLS |
| HTTP/3 | QUIC via MsQuic (Windows) and Network.framework (macOS). Request/response trailers, streamed bodies, NDJSON streaming, QPACK, graceful GOAWAY. |
| TLS | `SslStream` with ALPN (`h2` / `http/1.1`) |

```csharp
var builder = CosmoWebApplicationBuilder.Create()
    .ListenOn(9092)
    .UseHttps("cert.pfx", "password")
    .UseHttp3();
```

---

## Quick Start

```bash
# Minimal API server
var builder = CosmoWebApplicationBuilder.Create().ListenOn(9092);
var app = builder.Build();

app.MapGet("/health", ctx => {
    ctx.Response.WriteJson(new { status = "ok" });
    return ValueTask.CompletedTask;
});

app.Run();
```

---

## Frontend Integration

### Nuxt / Vite Dev Mode

`UseViteDevServer` spawns the frontend dev process as a managed `IHostedService`, blocking server startup until it is ready. `UseViteDevProxy` forwards Vite module-graph paths to the dev server so the browser can use a single origin.

```csharp
builder.UseViteDevServer(o =>
{
    o.WorkingDirectory = "frontend";
    o.Command          = "npm";
    o.Arguments        = "run dev";
    o.ReadyPattern     = "Local:";
    o.ReadyTimeout     = TimeSpan.FromSeconds(45);
    o.LogPrefix        = "[nuxt]";
});

builder.UseViteDevProxy(o =>
{
    o.DevServerUrl    = "http://127.0.0.1:3000";
    o.ProxiedPrefixes = ["/@vite", "/@fs", "/@id", "/_nuxt", "/__nuxt", "/__vite_ping"];
});
```

The `ViteDevServerService` shuts down the child process tree (`Kill(entireProcessTree: true)`) when the host stops, freeing the dev port cleanly.

### Nuxt Integrated Production

Serves a pre-built Nuxt SPA from `.output/public` with tiered cache headers, Brotli/GZip compression, and SPA fallback.

```csharp
builder.UseNuxtIntegrated(
    outputPath: "frontend/.output/public",
    configureFallback: o => o.ExcludedPrefixes = ["/api", "/health"]);
```

### Vue / Vite Production

```csharp
builder.UseViteFrontend(o =>
{
    o.HtmlTemplatePath = "frontend/index.html";
    o.ManifestPath     = "wwwroot/.vite/manifest.json";
    o.ExcludedPrefixes = ["/api", "/health"];
});

// With server-provided initial state
builder.UseViteFrontend(o =>
{
    o.RenderAsync = ctx => ValueTask.FromResult<ViteRenderResult?>(new ViteRenderResult
    {
        HeadHtml     = "<title>Dashboard</title>",
        InitialState = new { user = "alice", route = ctx.HttpContext.Request.Path }
    });
});
```

### Content Security Policy

Generates a per-request nonce and injects it into inline scripts emitted by `ViteFrontendMiddleware`.

```csharp
builder.UseCsp(o =>
{
    o.DefaultSrc = ["'self'"];
    o.ScriptSrc  = ["'self'", "'nonce-{nonce}'"];
    o.StyleSrc   = ["'self'", "'unsafe-inline'"];
    o.ConnectSrc = ["'self'"];
    o.ImgSrc     = ["'self'", "data:"];
});
```

### Reverse Proxy

```csharp
builder.UseReverseProxy(o =>
    o.Routes.Add(new ProxyRoute
    {
        PathPrefix       = "/",
        Destination      = "http://127.0.0.1:3000",
        ExcludedPrefixes = ["/api", "/health"]
    }));
```

---

## Real-time

### Server-Sent Events

`MapSse` sets `text/event-stream` headers and calls `BeginSseAsync` automatically.

```csharp
app.MapSse("/api/live/metrics", async ctx =>
{
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
    while (!ctx.RequestAborted.IsCancellationRequested &&
           await timer.WaitForNextTickAsync(ctx.RequestAborted))
    {
        var json = JsonSerializer.Serialize(new { cpu = 42.1, memory = 68.3 });
        await ctx.Response.WriteSseAsync(json, eventName: "metric",
            cancellationToken: ctx.RequestAborted);
    }
});
```

Heartbeat helper prevents proxies from closing idle connections:

```csharp
var heartbeat = ctx.Response.SendSseHeartbeatsAsync(TimeSpan.FromSeconds(15), ctx.RequestAborted);
// ... event loop ...
await heartbeat;
```

### SignalR

```csharp
var builder = CosmoWebApplicationBuilder.Create().AddSignalR();
var app = builder.Build();
app.MapHub<ChatHub>("/chat");

public sealed class ChatHub : Hub
{
    public async Task SendMessage(string user, string message) =>
        await Clients.All.SendAsync("ReceiveMessage", user, message);
}
```

Supports JSON and MessagePack protocols, groups, server-push via `IHubContext<T>`, server streaming, cancellation, and reconnect-after-restart. Compatible with standard ASP.NET SignalR clients.

### gRPC

```csharp
var builder = CosmoWebApplicationBuilder.Create().AddGrpc();
var app = builder.Build();
app.MapGrpcService<GreeterService>();

public sealed class GreeterService : GrpcServiceBase, IGrpcServiceDescriptor
{
    public IReadOnlyList<GrpcMethodDescriptor> Methods =>
    [
        new GrpcMethodDescriptor("Greeter", "SayHello", GrpcMethodType.Unary, typeof(GreeterService),
            async (svc, ctx, ct) =>
            {
                var req = await ctx.ReadRequestAsync<HelloRequest>(ct);
                await ctx.WriteResponseAsync(new HelloReply { Message = $"Hello {req.Name}" }, ct);
            })
    ];
}
```

Supports unary and server-streaming. Uses standard 5-byte gRPC framing.

---

## Security

### JWT / OAuth / OIDC

```csharp
builder
    .UseJwtAuthentication()
    .UseOAuthAuthentication()   // JWKS discovery
    .AddAuthorization();
```

### Antiforgery

```csharp
builder.AddAntiforgery();
// ...
app.UseAntiforgery();

app.MapGet("/form", ctx =>
{
    var svc = ctx.RequestServices.GetRequiredService<IAntiforgeryService>();
    var tokens = svc.GetAndStoreTokens(ctx);
    return TypedResults.Text($"<input name='__RequestVerificationToken' value='{tokens.RequestToken}' />");
});
```

### Rate Limiting

Fixed-window per-IP limiter with `X-Forwarded-For` support:

```csharp
builder.UseRateLimiting(opts =>
{
    opts.Limit      = 200;
    opts.Window     = TimeSpan.FromMinutes(1);
    opts.StatusCode = 429;
    opts.TrustProxy = true;
});
```

---

## Middleware & Pipeline

```
Request
  ↓ GlobalExceptionHandlerMiddleware
  ↓ CorsMiddleware
  ↓ CspMiddleware
  ↓ ViteDevProxyMiddleware   (dev)
  ↓ ViteFrontendMiddleware   (dev) / NuxtIntegratedMiddleware (prod)
  ↓ RouterMiddleware
```

Registration follows a builder pattern:

```csharp
var builder = CosmoWebApplicationBuilder.Create()
    .ListenOn(9092)
    .UseLogging()
    .UseExceptionHandler()
    .UseCors(o => { o.AllowAnyOrigin(); o.AllowAnyMethod(); o.AllowAnyHeader(); })
    .UseSession(new SessionOptions { IdleTimeout = TimeSpan.FromMinutes(20) })
    .UseRequestTimeouts(new RequestTimeoutOptions { DefaultTimeout = TimeSpan.FromSeconds(30) })
    .UseRateLimiting(opts => { opts.Limit = 100; opts.Window = TimeSpan.FromMinutes(1); })
    .UseForwardedHeaders()
    .AddOutputCache()
    .AddHealthChecks();
```

### Routing

```csharp
app.MapGet("/items/{id}", ctx => { ... });
app.MapPost("/items", ctx => { ... });

// Typed results
app.MapGet("/items/{id}", ctx =>
    id is null ? TypedResults.NotFound() : TypedResults.Ok(new { id, name = "Widget" }));
```

### Exception Handling

```csharp
builder
    .AddExceptionHandler<ValidationExceptionHandler>()
    .AddExceptionHandler<DatabaseExceptionHandler>();

public sealed class ValidationExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        if (ex is not ValidationException vex) return false;
        ctx.Response.StatusCode = 422;
        await ctx.Response.WriteJsonAsync(new { errors = vex.Errors }, ct);
        return true;
    }
}
```

### Scheduling

```csharp
builder.AddScheduler();
// ...
app.UseScheduler(scheduler =>
{
    scheduler.Schedule(() => Console.WriteLine("tick")).EveryMinute();
    scheduler.ScheduleAsync(async () => await SyncInvoices()).Cron("0 */1 * * *");
    scheduler.Schedule<CleanupJob>().DailyAt(2, 30);
});
```

---

## Caching

### Output Cache

```csharp
builder.AddOutputCache();
// ...
app.UseOutputCaching();

var policy = OutputCachePolicy.Build()
    .Expire(TimeSpan.FromMinutes(5))
    .VaryByQuery("page", "sort")
    .Tag("products")
    .ToPolicy();

app.MapGet("/products", async ctx =>
{
    ctx.SetOutputCachePolicy(policy);
    await ctx.Response.WriteJsonAsync(GetProducts());
});

// Tag-based invalidation
var store = ctx.RequestServices.GetRequiredService<IOutputCacheStore>();
await store.EvictByTagAsync("products");
```

### Response Cache (ETag / 304)

```csharp
builder.UseResponseCaching();
// Handlers set ctx.Response.ETag; returns 304 when If-None-Match matches.
```

### Memory & Distributed Cache

```csharp
builder
    .AddMemoryCache()
    .AddDistributedMemoryCache();
```

---

## Diagnostics & Observability

### Health Checks

```csharp
builder.AddHealthChecks()
    .AddCheck("db", () => HealthCheckResult.Healthy("Connected"))
    .AddCheck<MyDbHealthCheck>("database");

app.MapHealthChecks("/health");
```

### Problem Details (RFC 7807)

```csharp
builder.AddProblemDetails();

app.MapGet("/items/{id}", async ctx =>
{
    var svc = ctx.RequestServices.GetRequiredService<IProblemDetailsService>();
    await svc.WriteAsync(new ProblemDetailsContext
    {
        HttpContext    = ctx,
        ProblemDetails = new ProblemDetails { Status = 404, Title = "Not Found" }
    });
});
```

### Distributed Tracing

W3C `traceparent` propagation, OpenTelemetry-compatible `ActivitySource`:

```csharp
builder.UseTracing("MyService");
```

---

## Razor Components

Full `.razor` support with `@page`, `[Parameter]`, `[CascadingParameter]`, `@inject`, `@bind`, `EventCallback`, and validation components.

```razor
@page "/hello/{Name}"
@inherits Microsoft.AspNetCore.Components.ComponentBase
@inject NavigationManager Nav

<h1>Hello, @Name!</h1>
<p>Path: @Nav.Uri</p>

@code {
    [Parameter] public string Name { get; set; }
}
```

### Forms

```razor
<EditForm Model="@person" Action="/contact" Method="post">
    <InputText     Name="name"    Value="@person.Name" />
    <InputNumber   Name="age"     Value="@person.Age" />
    <InputSelect   Name="country" Value="@person.Country">
        <option value="us">United States</option>
    </InputSelect>
    <ValidationMessage For="Name" />
    <ValidationSummary />
    <button type="submit">Submit</button>
</EditForm>
```

### Change Detection

```csharp
var ctx = new EditContext(model);
model.Name = "Bob";
ctx.NotifyFieldChanged("Name");

ctx.IsModified("Name");      // true
ctx.GetModifiedFields();     // ["Name"]
ctx.FieldCssClass("Name");   // "modified valid"
ctx.MarkAsUnmodified();
```

---

## Samples

| Sample | Description |
|---|---|
| `samples/HelloWorldSample` | Minimal server with a single route |
| `samples/CosmoKitchenSink` | Covers most backend features in one project |
| `samples/FeatureShowcase` | Auth, SignalR, gRPC, output cache, and more |
| `samples/WeatherApp` | REST API with JWT auth, DI, streaming, and SQL |
| `samples/NuxtUiSample` | Nuxt 4 + Nuxt UI frontend backed by Cosmo APIs |
| `samples/LiveOpsSample` | Real-time dashboard: SSE, CSP, Vite dev server, Nuxt integrated deployment |
| `samples/CosmoBlazorSample` | SSR with Razor components |

### LiveOpsSample

```bash
cd samples/LiveOpsSample
npm run frontend:install
npm run dev
```

Benchmarks:

```bash
npm run benchmark          # API-only latency (no Nuxt)
npm run benchmark:nuxt     # Cosmo integrated vs Nuxt Nitro standalone
```

### NuxtUiSample

```bash
cd samples/NuxtUiSample
npm run dev
```

---

## Benchmarks

Single-client sequential HTTP/1.1, connection reused across rounds. 100 warmup + 1000 measured per scenario. All times in milliseconds.

### macOS arm64

| Scenario | CosmoApiServer | ASP.NET Core |
|---|---|---|
| GET /ping | P50 0.12ms · 8,600 ops/s | P50 0.14ms · 7,300 ops/s |
| GET /json | P50 0.13ms · 8,400 ops/s | P50 0.18ms · 5,600 ops/s |
| GET /large-json | P50 0.41ms · 2,400 ops/s | P50 0.58ms · 1,700 ops/s |
| POST /echo | P50 0.15ms · 6,500 ops/s | P50 0.20ms · 5,000 ops/s |
| GET /stream | P50 0.22ms · 4,500 ops/s | P50 0.31ms · 3,200 ops/s |

> Note: these are single-threaded sequential measurements. Concurrent throughput will differ.

### LiveOpsSample API (production mode)

| Scenario | P50 | P99 | ops/sec |
|---|---|---|---|
| GET /ping | 0.12ms | 0.27ms | 8,643 |
| GET /health | 0.13ms | 0.27ms | 7,994 |
| GET /api/status | 0.12ms | 0.27ms | 8,432 |

JSON serialisation of 5 service records (`/api/status`) adds no measurable latency over a plain text response.

---

## Deployment

### HTTP/3 in production

Add `UseHttp3()` with a valid certificate:

```csharp
var builder = CosmoWebApplicationBuilder.Create()
    .ListenOn(9092)
    .UseHttps("cert.pfx", "password")
    .UseHttp3();
```

When running Nuxt through `UseNuxtIntegrated`, all assets — HTML, JS, CSS — are served by Cosmo over the same QUIC connection. SSE streams benefit from QUIC's per-stream flow control, which avoids head-of-line blocking between concurrent requests.

### Cloudflare Pages / Workers

Nuxt can be deployed to Cloudflare using the `cloudflare_pages` preset:

```bash
npx nuxi build --preset=cloudflare_pages
```

This gives HTTP/3 at the Cloudflare edge automatically. However, there are trade-offs relevant to this architecture:

| | Cloudflare | Cosmo + `UseHttp3()` |
|---|---|---|
| HTTP/3 on browser leg | Yes (edge) | Yes (direct) |
| SSE streams | Fragile — 100s connection timeout on free plan | Native |
| API colocation | No — `/api/*` still needs an origin server | Yes |
| Cloudflare → origin leg | HTTP/2 at best | N/A |

Cloudflare is suitable for frontends that are mostly static or read-heavy without persistent connections. For SSE-heavy or API-coupled deployments, `UseHttp3()` on Cosmo keeps the entire stack on one connection and one protocol.

---

## Changelog

### v3.1.0
- Server-Sent Events, Content Security Policy, Vite Dev Proxy, Vite Dev Server, Nuxt Integrated, Reverse Proxy
- `samples/LiveOpsSample` demonstrating all six features
- Bug fixes: CORS origin spoofing guard, CSRF path bypass, `Retry-After` off-by-one, `ForwardedHeaders` trusted-proxy gate, `AntiforgeryMiddleware` Content-Type guard, SPA fallback cache-control
- 373 tests

### v3.0.3
- Vue frontend hosting via `ViteFrontendMiddleware`
- `samples/NuxtUiSample`

### v3.0.2
- `HtmlEditorComponent` for Razor slices

### v3.0.1
- Razor: `InputDate`, `InputRadioGroup`, `InputRadio`, `InputFile`, `RenderTreeBuilder` stubs

### v3.0.0
- HTTP/3 production-ready
- Rate Limiting
- H3Interop validation tool (32/32 scenarios)
- Windows benchmark scripts
- 313 tests

### v2.1.0 — v2.1.4
- SignalR (JSON + MessagePack), gRPC, Sessions, Request Timeouts, Response Caching, Forwarded Headers, Request Decompression, Distributed Tracing, Endpoint Filters, `IHttpContextAccessor`
- Output Caching, Antiforgery, TypedResults, `IExceptionHandler`, `IHostedService`, WebSockets

### v2.0.5 — v2.0.7
- Health Checks, Problem Details, Policy-Based Authorization, OAuth/OIDC, Memory Cache, Distributed Cache, `IHttpClientFactory`
- Stream flush coalescing, HTTP/3 QPACK, trailers, GOAWAY

---

## Credits

- Scheduling powered by [Coravel](https://github.com/jamesmh/coravel)
