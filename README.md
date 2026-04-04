# CosmoApiServer

A high-performance, zero-dependency HTTP server framework for .NET 10, built entirely on `System.IO.Pipelines` and `System.Net.Sockets`.

No DotNetty. No ASP.NET. No Kestrel. Just raw sockets → pipes → your handlers.

---

## Benchmark

Sequential · 1,000 rounds · keep-alive

### macOS arm64

| Scenario | CosmoApiServer | ASP.NET Core | P50 (Cosmo) | Advantage |
|---|---|---|---|---|
| GET /ping | **10,905 ops/s** | 9,671 ops/s | 0.09 ms | **+13%** |
| GET /json | **9,990 ops/s** | 9,166 ops/s | 0.10 ms | **+9%** |
| GET /route/{id} | **10,526 ops/s** | 8,453 ops/s | 0.10 ms | **+25%** |
| POST /echo | **11,087 ops/s** | 7,819 ops/s | 0.09 ms | **+42%** |
| GET /large-json (1000 items) | **2,087 ops/s** | 1,840 ops/s | 0.48 ms | **+13%** |
| GET /query | **11,494 ops/s** | 8,170 ops/s | 0.09 ms | **+41%** |
| POST /form | **10,582 ops/s** | 8,157 ops/s | 0.09 ms | **+30%** |
| GET /headers | **10,081 ops/s** | 8,143 ops/s | 0.10 ms | **+24%** |
| GET /stream (NDJSON, 10 items) | **12,642 ops/s** | 9,775 ops/s | 0.08 ms | **+29%** |
| GET /file (64 KB) | **6,165 ops/s** | 4,446 ops/s | 0.16 ms | **+39%** |

**10 of 10 scenarios win** on macOS. The `/stream` result flipped from a −11% loss to a +29% win after coalescing all NDJSON chunks into a single socket flush per request.

### Windows 11 VM

| Scenario | CosmoApiServer | ASP.NET Core | P50 (Cosmo) | Advantage |
|---|---|---|---|---|
| GET /ping | **7,158 ops/s** | 6,382 ops/s | 0.14 ms | **+12%** |
| GET /json | **7,236 ops/s** | 7,289 ops/s | 0.14 ms | ≈ even |
| GET /route/{id} | **7,283 ops/s** | 6,234 ops/s | 0.14 ms | **+17%** |
| POST /echo | **7,273 ops/s** | 5,935 ops/s | 0.14 ms | **+23%** |
| GET /large-json (1000 items) | **1,424 ops/s** | 1,106 ops/s | 0.70 ms | **+29%** |
| GET /query | **8,163 ops/s** | 7,052 ops/s | 0.12 ms | **+16%** |
| POST /form | **7,441 ops/s** | 6,766 ops/s | 0.13 ms | **+10%** |
| GET /headers | **7,716 ops/s** | 6,878 ops/s | 0.13 ms | **+12%** |
| GET /stream (NDJSON, 10 items) | **8,460 ops/s** | 7,283 ops/s | 0.12 ms | **+16%** |
| GET /file (64 KB) | **3,386 ops/s** | 2,311 ops/s | 0.30 ms | **+46%** |

**9 of 10 scenarios win** on Windows (`/json` is statistical noise at this concurrency level).



### Windows HTTP/3 (MsQuic)

Production-ready HTTP/3 over QUIC. Interop validated: 32/32 scenarios pass using .NET `HttpClient` Version30 against a live server (`tools/run_windows_interop.ps1`). Latest benchmark (1,000 rounds, keep-alive):

| Scenario | CosmoApiServer HTTP/3 | P50 | P99 |
|---|---|---|---|
| GET /ping | **3,046 ops/s** | 0.33 ms | 4.88 ms |
| GET /json | **2,988 ops/s** | 0.33 ms | 3.10 ms |
| GET /route/{id} | **2,967 ops/s** | 0.34 ms | 2.94 ms |
| POST /echo | **2,645 ops/s** | 0.38 ms | 5.51 ms |
| GET /large-json (1000 items) | **1,136 ops/s** | 0.88 ms | 6.09 ms |
| GET /query | **3,840 ops/s** | 0.26 ms | 1.37 ms |
| POST /form | **3,772 ops/s** | 0.27 ms | 2.13 ms |
| GET /headers | **5,774 ops/s** | 0.17 ms | 0.31 ms |
| GET /stream (NDJSON, 10 items) | **2,693 ops/s** | 0.37 ms | 5.63 ms |
| GET /file (64 KB) | **1,612 ops/s** | 0.62 ms | 6.62 ms |

P99 spikes on some scenarios reflect per-request QUIC stream setup overhead (MsQuic on Windows). P50 latency is stable. The `/headers` scenario is fastest because it skips body serialization entirely.

**Phase 6 final optimisations** (P99 spike reduction and frame coalescing):
- Successful stream disposal is now awaited inline instead of via `Task.Run` — eliminates thread-pool queue latency that previously caused 4–6 ms P99 spikes under sustained load
- `WriteFrameAsync` combines frame header + payload into a single `WriteAsync` call for frames ≤ 16 KB, reducing async QUIC operations per response from 3 to 1
- `Http3DataFrameStream` coalesce threshold raised from 8 KB to 32 KB — streaming DATA frames now batch uniformly at the same chunk size as buffered responses
- `ServerOptions.Http3MaxRequestsPerConnection` (default 100) exposes the per-connection GOAWAY threshold for operator tuning

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
- HTTP/3 over QUIC (`UseHttp3()`) — production-ready; request/response trailers, streamed request bodies, NDJSON streaming, dynamic QPACK decode, graceful GOAWAY, interop validated on Windows MsQuic and macOS Network.framework
- TLS via `SslStream` with ALPN (`h2` / `http/1.1`)
- **Razor Components** — Full `.razor` support with `@page`, `[Parameter]`, and `CascadingParameters`
- **Routable Components** — Components can define their own routes via `@page` without a controller
- **Form Components** — `<EditForm>`, `<InputText>`, `<InputNumber>`, `<InputSelect>`, `<InputCheckbox>`, `<InputTextArea>`
- **Change Detection** — Snapshot-based dirty tracking with `EditContext`, `FieldIdentifier`, and `FieldState`
- **`@inject` Dependency Injection** — Inject services into Razor components via `@inject`
- **`EventCallback` / `EventCallback<T>`** — Parent-child component communication
- **`<CascadingValue>`** — Provide values to the entire descendant component tree
- **`NavigationManager`** — Programmatic navigation and URI utilities
- **`@bind` Support** — Two-way data binding via `BindConverter`
- **Validation** — `<ValidationMessage>`, `<ValidationSummary>`, per-field CSS classes (`modified`, `valid`, `invalid`)
- Attribute-based controllers (`[HttpGet]`, `[HttpPost]`, `[Route]`, `[Authorize]`)
- Convention-based routing (`MapGet`, `MapPost`, …)
- JSON request/response (`WriteJson`, `ReadJson<T>`)
- `IAsyncEnumerable<T>` → NDJSON streaming response
- Middleware pipeline (`UseLogging`, `UseCors`, `UseJwtAuthentication`, custom `IMiddleware`)
- Built-in scheduler via `AddScheduler()` / `UseScheduler(...)`
- WebSockets (`UseWebSockets()` + `context.AcceptWebSocketAsync()`)
- **SignalR** — Hub base class, `MapHub<THub>(path)`, `IHubContext<THub>`, groups, all-except, and ASP.NET SignalR client-compatible JSON and MessagePack support over the standard WebSocket path, including server streaming, cancellation, and reconnect-after-restart
- **gRPC** — 5-byte framing, `GrpcServiceBase`, `MapGrpcService<T>()`, unary + server-streaming contexts
- **Health Checks** — `AddHealthChecks()`, `AddCheck<T>()`, `/health` endpoint with JSON report
- **Problem Details** — RFC 7807 `IProblemDetailsService`, `AddProblemDetails()`
- **Policy-Based Authorization** — `AddAuthorization()`, `[Authorize(Policy="...")]`, `IAuthorizationService`
- **JWT + OAuth/OIDC** — `UseJwtAuthentication()`, `UseOAuthAuthentication()` with JWKS discovery
- **Memory Cache** — `AddMemoryCache()`, `IMemoryCache`
- **Distributed Cache** — `AddDistributedMemoryCache()`, `IDistributedCache`
- **Sessions** — `UseSession()`, cookie-backed in-memory sessions with idle timeout
- **Response Caching** — `UseResponseCaching()` with ETag/304 support
- **Output Caching** — `AddOutputCache()` + `UseOutputCaching()`, vary-by-header/query, tag-based invalidation, `X-Output-Cache: HIT/MISS`
- **Antiforgery** — `AddAntiforgery()` + `UseAntiforgery()`, cookie+header/form token pattern, `IAntiforgeryService`, `[ValidateAntiforgery]`
- **Response Compression** — `UseResponseCompression()` (GZip/Deflate/Brotli)
- **Request Decompression** — `UseRequestDecompression()`
- **Request Timeouts** — `UseRequestTimeouts()`, 504 on breach
- **Forwarded Headers** — `UseForwardedHeaders()` (X-Forwarded-For/Host/Proto)
- **Distributed Tracing** — `UseTracing()`, W3C `traceparent`, `ActivitySource` (OpenTelemetry-compatible)
- **Endpoint Filters** — `AddEndpointFilter()` on minimal-API routes
- **TypedResults** — `TypedResults.Ok/Created/NotFound/Problem/Stream/…` factory methods
- **IExceptionHandler** — `AddExceptionHandler<T>()`, structured exception handling in registration order
- **IHostedService** — `AddHostedService<T>()`, start/stop lifecycle tied to the server
- **IHttpClientFactory** — `AddHttpClient()`, named and typed clients
- **IHttpContextAccessor** — `AddHttpContextAccessor()`, `AsyncLocal`-based ambient context
- OpenAPI & Swagger UI auto-generation
- Security Middlewares (CSRF, HSTS, HTTPS Redirection)
- Model Validation via DataAnnotations (Controllers & Components)
- **Rate Limiting** — `UseRateLimiting()`, fixed-window per-IP limiter, configurable limit/window/status code, `Retry-After` header, proxy-trust mode (`TrustProxy`) for X-Forwarded-For

---

## HTTP/3

`CosmoApiServer` includes HTTP/3 support over QUIC (Phase 6 complete).

Current scope:

- Buffered request/response handling over HTTP/3
- NDJSON streaming responses over HTTP/3 DATA frames
- Streamed request bodies across multiple DATA frames
- Request trailers and response trailers
- Dynamic QPACK request decoding with blocked-stream handling
- Graceful shutdown via GOAWAY; configurable per-connection request limit (`Http3MaxRequestsPerConnection`)
- Feature-parity coverage for HEAD, static files, ranges, forms, multipart uploads, OpenAPI, Swagger UI, and auth/header propagation
- Single-`WriteAsync` per frame for payloads ≤ 16 KB; coalesced streaming DATA frames up to 32 KB

Enable it on a TLS listener:

```csharp
var builder = CosmoWebApplicationBuilder.Create()
    .UseHttps("cert.pfx", "password")
    .UseHttp3();
```

Notes:

- HTTP/3 requires TLS and runtime QUIC support on the host platform.
- `UseHttp3()` runs alongside the existing HTTP/1.1 and HTTP/2 support on the same port.
- The implementation plan and change history are tracked in [`HTTP3_ROADMAP.md`](HTTP3_ROADMAP.md).

Streaming response example:

```csharp
app.MapGet("/stream", async ctx =>
{
    await ctx.Response.WriteStreamingResponseAsync(200, async body =>
    {
        await body.WriteAsync("""{"id":1}\n"""u8.ToArray());
        await body.FlushAsync();
        await body.WriteAsync("""{"id":2}\n"""u8.ToArray());
    }, ctx.RequestAborted);
});
```

---

## Scheduling

`CosmoApiServer` includes a built-in scheduler based on the Coravel-style scheduling API already shipped in the core package.

Register it during startup:

```csharp
var builder = CosmoWebApplicationBuilder.Create()
    .AddScheduler();

var app = builder.Build();

app.UseScheduler(scheduler =>
{
    scheduler.Schedule(() => Console.WriteLine("runs every minute"))
        .EveryMinute();

    scheduler.ScheduleAsync(async () =>
    {
        await Task.Delay(10);
        Console.WriteLine("runs hourly at xx:15");
    })
    .HourlyAt(15);

    scheduler.Schedule<CleanupJob>()
        .DailyAt(2, 30);

    scheduler.ScheduleAsync(() => SyncInvoices())
        .Cron("0 */1 * * *");
});
```

Invocable jobs are supported through `IInvocable`:

```csharp
using Coravel.Invocable;

public sealed class CleanupJob : IInvocable
{
    public Task Invoke()
    {
        Console.WriteLine("cleanup");
        return Task.CompletedTask;
    }
}
```

Available scheduling styles include:

- `EveryMinute()`, `EveryFiveMinutes()`, `EveryTenMinutes()`, `EveryFifteenMinutes()`, `EveryThirtyMinutes()`
- `Hourly()`, `HourlyAt(minute)`
- `Daily()`, `DailyAtHour(hour)`, `DailyAt(hour, minute)`
- `Weekly()`, `Monthly()`
- `EverySecond()`, `EveryFiveSeconds()`, `EveryTenSeconds()`, `EveryFifteenSeconds()`, `EveryThirtySeconds()`, `EverySeconds(n)`
- `Cron("...")`

Cron support is the built-in five-part format:

```csharp
scheduler.ScheduleAsync(() => SyncInvoices())
    .Cron("0 */1 * * *"); // top of every hour
```

You can also isolate scheduled work onto a dedicated worker:

```csharp
app.UseScheduler(scheduler =>
{
    scheduler.OnWorker("reports")
        .Schedule<GenerateReportJob>()
        .DailyAt(1, 0);
});
```

---

## Razor Components

`CosmoApiServer` includes a first-class implementation of Razor Components (similar to Blazor SSR). This provides the power of Razor syntax (C# + HTML) with the performance of a zero-dependency framework.

### Why Razor Components?
- **High Performance:** Renders directly to `CosmoApiServer`'s `HttpResponse` buffers using an optimized async state machine.
- **Routable:** Use `@page "/my-route"` directly in your `.razor` files.
- **Cascading Parameters:** Share state (like `ModelState` or `User`) down the component tree automatically.
- **Validation:** Support for `DataAnnotations` with built-in `<ValidationMessage>` and `<ValidationSummary />`.
- **Form Components:** `<EditForm>`, `<InputText>`, `<InputNumber>`, `<InputCheckbox>`, `<InputSelect>`, `<InputTextArea>`.
- **Change Detection:** Snapshot-based dirty tracking via `EditContext` — knows exactly which fields changed and can revert.
- **Dependency Injection:** `@inject` resolves services from the DI container into components.
- **EventCallback:** Type-safe parent↔child component communication.

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

## Forms & Change Detection

CosmoApiServer provides a complete form system with automatic change tracking — no `INotifyPropertyChanged` required.

### EditForm with Input Components

```razor
@page "/contact"
@inherits Microsoft.AspNetCore.Components.ComponentBase

<EditForm Model="@person" Action="/contact" Method="post" CssClass="form-group">
    <InputText Name="name" Value="@person.Name" Placeholder="Full name" Required="true" />
    <InputNumber Name="age" Value="@person.Age" Min="0" Max="150" />
    <InputCheckbox Name="subscribe" Value="@person.Subscribe" />
    <InputTextArea Name="bio" Value="@person.Bio" Rows="5" Placeholder="Tell us about yourself" />
    <InputSelect Name="country" Value="@person.Country">
        <option value="us">United States</option>
        <option value="uk">United Kingdom</option>
    </InputSelect>

    <ValidationMessage For="Name" />
    <ValidationSummary />

    <button type="submit">Submit</button>
</EditForm>

@code {
    private PersonModel person = new() { Name = "Alice", Age = 30 };

    public class PersonModel
    {
        [Required] public string? Name { get; set; }
        [Range(0, 150)] public int Age { get; set; }
        public bool Subscribe { get; set; }
        public string? Bio { get; set; }
        public string? Country { get; set; }
    }
}
```

### Change Detection with EditContext

`EditContext` takes a snapshot of your model at creation time and detects changes by comparing current property values against the original snapshot. This means:

- **No dirty flags** — actual value comparison, not "was touched"
- **Revert-aware** — changing `"Alice"` → `"Bob"` → `"Alice"` is correctly detected as unmodified
- **Zero boilerplate** — works with any POCO model via reflection

```csharp
var model = new PersonModel { Name = "Alice", Age = 30 };
var ctx = new EditContext(model);

// Initially clean
ctx.IsModified();            // false

// Change a field
model.Name = "Bob";
ctx.NotifyFieldChanged("Name");

// Query state
ctx.IsModified();            // true
ctx.IsModified("Name");      // true
ctx.IsModified("Age");       // false
ctx.GetModifiedFields();     // ["Name"]

// Per-field state
var state = ctx.GetFieldState("Name");
state.IsModified;            // true
state.OriginalValue;         // "Alice"
state.CurrentValue;          // "Bob"

// CSS classes for styling (combines: "modified", "valid", "invalid")
ctx.FieldCssClass("Name");   // "modified valid"

// Reset
ctx.MarkAsUnmodified();      // retakes snapshot, all fields clean
ctx.IsModified();            // false
```

### Events

```csharp
var ctx = new EditContext(model);

ctx.OnFieldChanged += (fieldIdentifier) =>
{
    Console.WriteLine($"{fieldIdentifier.FieldName} changed");
};

ctx.OnValidationRequested += () =>
{
    Console.WriteLine("Validation was triggered");
};

ctx.OnValidationStateChanged += () =>
{
    Console.WriteLine("Validation results updated");
};
```

### Field CSS Classes

The `FieldCssClassProvider` automatically generates CSS classes based on field state:

| State | CSS Class | When |
|-------|-----------|------|
| Modified | `modified` | Field value differs from snapshot |
| Valid | `valid` | Field passed validation |
| Invalid | `invalid` | Field has a validation error |

Classes combine: a modified field that failed validation gets `"modified invalid"`.

Override with a custom provider:

```csharp
public class BootstrapCssProvider : FieldCssClassProvider
{
    public override string GetFieldCssClass(EditContext editContext, FieldIdentifier fieldIdentifier)
    {
        var state = editContext.GetFieldState(fieldIdentifier);
        if (state is null) return string.Empty;
        if (state.IsInvalid) return "is-invalid";
        if (state.IsModified && state.IsValid) return "is-valid";
        return string.Empty;
    }
}

ctx.CssClassProvider = new BootstrapCssProvider();
```

---

## Dependency Injection in Components

Use `@inject` to resolve services from the DI container:

```razor
@page "/dashboard"
@inherits Microsoft.AspNetCore.Components.ComponentBase
@inject NavigationManager Nav
@inject ILogger<Dashboard> Logger

<h1>Dashboard</h1>
<p>Current URL: @Nav.Uri</p>
<a href="@Nav.ToAbsoluteUri("/settings")">Settings</a>

@code {
    protected override void OnInitialized()
    {
        Logger.LogInformation("Dashboard loaded at {Path}", Nav.Path);
    }
}
```

### NavigationManager

An injectable service for programmatic navigation and URI utilities:

```csharp
// Registered automatically — just @inject it
@inject NavigationManager Nav

// Properties
Nav.Uri          // "http://localhost:8080/products?page=2"
Nav.Path         // "/products"
Nav.QueryString  // "?page=2"
Nav.BaseUri      // "http://localhost:8080/"

// Conversion
Nav.ToAbsoluteUri("/foo/bar");       // "http://localhost:8080/foo/bar"
Nav.ToBaseRelativePath("http://localhost:8080/foo/bar"); // "foo/bar"

// Navigation (sets Location header + 302)
Nav.NavigateTo("/login");
Nav.NavigateTo("/login", forceLoad: true);
```

---

## EventCallback

Parent-child component communication using `EventCallback` and `EventCallback<T>`:

```razor
<!-- Parent.razor -->
<ChildComponent OnClick="HandleClick" OnSelect="HandleSelect" />

<p>Clicked: @clicked</p>
<p>Selected: @selected</p>

@code {
    private bool clicked;
    private string? selected;

    private void HandleClick() => clicked = true;
    private void HandleSelect(string item) => selected = item;
}
```

```razor
<!-- ChildComponent.razor -->
<button @onclick="OnClick">Click me</button>
<button @onclick="() => OnSelect.InvokeAsync("Item A")">Select A</button>

@code {
    [Parameter] public EventCallback OnClick { get; set; }
    [Parameter] public EventCallback<string> OnSelect { get; set; }
}
```

Programmatic usage:

```csharp
// Simple callback
var cb = new EventCallback(() => Console.WriteLine("Clicked!"));
await cb.InvokeAsync();

// Typed callback
var cb = new EventCallback<string>(value => Console.WriteLine($"Got: {value}"));
await cb.InvokeAsync("hello");

// Factory (used by Razor-generated code)
var cb = EventCallbackFactory.Create(this, () => { /* handler */ });
```

---

## CascadingValue

Share values with all descendant components without explicit parameter passing:

```razor
<!-- App.razor -->
<CascadingValue Value="@theme">
    <MainLayout />
</CascadingValue>

@code {
    private ThemeInfo theme = new() { PrimaryColor = "#007bff" };
}
```

```razor
<!-- Any descendant component -->
@code {
    [CascadingParameter] public ThemeInfo? Theme { get; set; }
}
```

---

## Projects in this repository

| Project | Description |
|---|---|
| `Core/` | Core framework library (`CosmoApiServer.Core`) |
| `samples/CosmoApiBenchHost` | HTTP/1.1 benchmark host (used by `run_benchmark.sh` and `tools/run_windows_benchmark.ps1`) |
| `samples/AspNetBenchHost` | ASP.NET Core benchmark host for head-to-head comparison |
| `samples/CosmoBlazorSample` | Blazor-style SSR sample with Razor components |
| `samples/BlazorSqlSample` | SQL streaming + components sample |
| `samples/CosmoKitchenSink` | Kitchen-sink sample covering most features |
| `samples/HelloWorldSample` | Minimal getting-started example |
| `samples/FeatureShowcase` | Feature showcase with auth, SignalR, gRPC, output cache, etc. |
| `samples/WeatherApp` | Full REST API: JWT auth, DI, streaming, CosmoSQLClient |
| `templates/CosmoRazorServerTemplate` | `dotnet new cosmorazor` project template |
| `tests/CosmoApiServer.Core.Tests` | 287 tests covering routing, middleware, HTTP/3, SignalR, gRPC, Razor, output cache, antiforgery, and more |
| `tests/ApiServer.Benchmark` | BenchmarkDotNet suite for HTTP/1.1 and HTTP/3 scenarios |
| `tools/H3Interop/` | .NET HTTP/3 interop validation tool (32-scenario end-to-end client) |
| `tools/run_windows_benchmark.ps1` | Windows benchmark script (builds, starts hosts, runs all scenarios) |
| `tools/run_windows_interop.ps1` | Windows HTTP/3 interop script (starts server, runs H3Interop) |

---

## Health Checks

```csharp
var builder = CosmoWebApplicationBuilder.Create()
    .AddHealthChecks()
        .AddCheck("db", () => HealthCheckResult.Healthy("Connected"))
        .AddCheck<MyDbHealthCheck>("database")
    .Builder;

var app = builder.Build();
app.MapHealthChecks("/health");
```

GET `/health` returns:

```json
{
  "status": "Healthy",
  "entries": {
    "db": { "status": "Healthy", "description": "Connected" }
  }
}
```

Returns `200 Healthy`, `200 Degraded`, or `503 Unhealthy` based on the worst check result.

---

## Problem Details (RFC 7807)

```csharp
var builder = CosmoWebApplicationBuilder.Create()
    .AddProblemDetails();
```

Inject `IProblemDetailsService` to write structured error responses:

```csharp
app.MapGet("/items/{id}", async ctx =>
{
    var svc = ctx.RequestServices.GetRequiredService<IProblemDetailsService>();
    await svc.WriteAsync(new ProblemDetailsContext
    {
        HttpContext = ctx,
        ProblemDetails = new ProblemDetails { Status = 404, Title = "Not Found", Detail = "Item does not exist." }
    });
});
```

`GlobalExceptionHandlerMiddleware` automatically writes RFC 7807 JSON for unhandled exceptions when `IProblemDetailsService` is registered.

---

## IExceptionHandler

Register one or more structured exception handlers, called in registration order:

```csharp
var builder = CosmoWebApplicationBuilder.Create()
    .AddExceptionHandler<ValidationExceptionHandler>()
    .AddExceptionHandler<DatabaseExceptionHandler>();
```

```csharp
public sealed class ValidationExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        if (ex is not ValidationException vex) return false;
        ctx.Response.StatusCode = 422;
        await ctx.Response.WriteJsonAsync(new { errors = vex.Errors }, ct);
        return true; // handled — stop chain
    }
}
```

The first handler that returns `true` short-circuits the chain. Unhandled exceptions fall through to the `GlobalExceptionHandlerMiddleware` fallback.

---

## TypedResults

Minimal-API result factory methods analogous to ASP.NET Core's `TypedResults`:

```csharp
app.MapGet("/items/{id}", ctx =>
{
    var id = ctx.Request.RouteValues["id"];
    return id is null
        ? TypedResults.NotFound()
        : TypedResults.Ok(new { id, name = "Widget" });
});

app.MapPost("/items", ctx => TypedResults.Created("/items/42", new { id = 42 }));

app.MapDelete("/items/{id}", ctx => TypedResults.NoContent());

// Problem Details shorthand
app.MapGet("/error", ctx => TypedResults.Problem("Something went wrong", statusCode: 500));

// File / binary response
app.MapGet("/download", ctx => TypedResults.Bytes(data, "application/octet-stream", "file.bin"));

// Plain text
app.MapGet("/ping", ctx => TypedResults.Text("pong"));
```

Available factories: `Ok`, `Created`, `Accepted`, `NoContent`, `Redirect`, `RedirectPermanent`, `BadRequest`, `Unauthorized`, `Forbid`, `NotFound`, `Conflict`, `UnprocessableEntity`, `TooManyRequests`, `InternalServerError`, `Text`, `Json`, `Bytes`, `Stream`, `Problem`.

---

## Output Caching

```csharp
var builder = CosmoWebApplicationBuilder.Create()
    .AddOutputCache();

var app = builder.Build();
app.UseOutputCaching();
```

Default behaviour caches all `GET` responses for 1 minute. Responses include `X-Output-Cache: HIT` or `X-Output-Cache: MISS`.

**Per-route policy:**

```csharp
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
```

**Tag-based invalidation** (e.g. after a write):

```csharp
var store = ctx.RequestServices.GetRequiredService<IOutputCacheStore>();
await store.EvictByTagAsync("products");
```

Bypass the cache for a specific request by sending `Cache-Control: no-cache`.

---

## Antiforgery

```csharp
var builder = CosmoWebApplicationBuilder.Create()
    .AddAntiforgery();

var app = builder.Build();
app.UseAntiforgery(); // blocks unsafe methods without a valid token
```

Generate a token set (e.g. for an HTML form or SPA):

```csharp
app.MapGet("/form", ctx =>
{
    var svc = ctx.RequestServices.GetRequiredService<IAntiforgeryService>();
    var tokens = svc.GetAndStoreTokens(ctx);
    // tokens.CookieToken — written to .cosmo.af cookie automatically
    // tokens.RequestToken — embed in the form or send as X-XSRF-TOKEN header
    return TypedResults.Text($"<input name='__RequestVerificationToken' value='{tokens.RequestToken}' />");
});
```

The middleware validates the cookie + header/form token pair using `CryptographicOperations.FixedTimeEquals` to prevent timing attacks. GET/HEAD/OPTIONS are always allowed through.

Attribute-based opt-out:

```csharp
[IgnoreAntiforgery]
public Task HandleWebhook(HttpContext ctx) { ... }
```

---

## Sessions

```csharp
var builder = CosmoWebApplicationBuilder.Create()
    .UseSession(new SessionOptions { IdleTimeout = TimeSpan.FromMinutes(20) });
```

Access the session from any handler:

```csharp
app.MapGet("/cart", ctx =>
{
    ctx.Session!.SetInt32("count", 5);
    ctx.Session.SetString("user", "alice");
    var count = ctx.Session.GetInt32("count"); // 5
});
```

Sessions are cookie-backed (`.cosmo.session`) and stored in-memory. The idle timeout is reset on each request.

---

## Request Timeouts

```csharp
var builder = CosmoWebApplicationBuilder.Create()
    .UseRequestTimeouts(new RequestTimeoutOptions { DefaultTimeout = TimeSpan.FromSeconds(30) });
```

When a handler exceeds the timeout, `context.RequestAborted` is cancelled and the middleware writes `504 Gateway Timeout`. The cancellation token is linked to the original client-disconnect token so that client aborts are still distinguished from server timeouts.

```csharp
app.MapGet("/slow", async ctx =>
{
    // ctx.RequestAborted is cancelled after 30 s
    await Task.Delay(60_000, ctx.RequestAborted);
});
```

---

## Response Caching

```csharp
var builder = CosmoWebApplicationBuilder.Create()
    .UseResponseCaching();
```

Adds ETag-based conditional caching. Handlers that set `ctx.Response.ETag` will automatically return `304 Not Modified` when the client sends a matching `If-None-Match` header.

---

## Distributed Tracing

```csharp
var builder = CosmoWebApplicationBuilder.Create()
    .UseTracing("MyService");
```

Propagates W3C `traceparent` / `tracestate` headers and creates an `Activity` per request via `ActivitySource`. Compatible with any OpenTelemetry exporter.

---

## Forwarded Headers

```csharp
var builder = CosmoWebApplicationBuilder.Create()
    .UseForwardedHeaders();
```

Rewrites `ctx.Request.RemoteIpAddress`, `ctx.Request.Host`, and `ctx.Request.IsHttps` from `X-Forwarded-For`, `X-Forwarded-Host`, and `X-Forwarded-Proto` when running behind a proxy or load balancer.

---

## Rate Limiting

Fixed-window per-IP rate limiter with atomic counters, `Retry-After` header, and optional proxy-trust mode.

```csharp
var builder = CosmoWebApplicationBuilder.Create()
    .UseRateLimiting(opts =>
    {
        opts.Limit      = 200;                        // max requests per window
        opts.Window     = TimeSpan.FromMinutes(1);    // window size
        opts.StatusCode = 429;                        // response code when exceeded
        opts.TrustProxy = true;                       // use X-Forwarded-For (rightmost value)
    });
```

When the limit is exceeded the response is:

```
HTTP/1.1 429 Too Many Requests
Retry-After: 42
{"error":"RateLimitExceeded","message":"Rate limit exceeded. Try again later."}
```

Expired entries are cleaned up automatically every 60 seconds. `TrustProxy = true` picks the rightmost `X-Forwarded-For` value (appended by the trusted proxy) to prevent IP spoofing via client-controlled headers.

---

## SignalR

```csharp
var builder = CosmoWebApplicationBuilder.Create()
    .AddSignalR();

var app = builder.Build();
app.MapHub<ChatHub>("/chat");
```

Define a hub:

```csharp
public sealed class ChatHub : Hub
{
    public async Task SendMessage(string user, string message)
    {
        await Clients.All.SendAsync("ReceiveMessage", user, message);
    }
}
```

**Server-side push** via `IHubContext<THub>`:

```csharp
app.MapPost("/broadcast", async ctx =>
{
    var registry = ctx.RequestServices.GetRequiredService<HubContextRegistry>();
    var hub = registry.Get<ChatHub>();
    await hub!.Clients.All.SendAsync("ReceiveMessage", "server", "Hello from server!");
});
```

Features: groups (`AddToGroupAsync` / `RemoveFromGroupAsync`), per-client proxy (`Clients.Client(id)`), `AllExcept`, `OthersInGroup`, and SignalR protocol framing for both JSON and MessagePack over WebSockets.

Current compatibility scope:

- Verified with the real `Microsoft.AspNetCore.SignalR.Client`
- Negotiate + WebSocket connect
- Invoke / return values
- Multi-argument invocation
- One-way `SendAsync(...)`
- JSON and MessagePack hub protocols
- Server-streaming hub methods
- Stream failure propagation
- Stream cancellation via raw SignalR cancel frames and `StreamAsChannelAsync(..., cancellationToken)`
- `IHubContext<THub>` sends to all, specific clients, and groups
- Group membership and `OthersInGroup`
- Disconnect callback and invocation error propagation
- Reconnect-enabled clients on the standard path, including reconnect after server restart

---

## gRPC

```csharp
var builder = CosmoWebApplicationBuilder.Create()
    .AddGrpc();

var app = builder.Build();
app.MapGrpcService<GreeterService>();
```

Implement a service:

```csharp
public sealed class GreeterService : GrpcServiceBase, IGrpcServiceDescriptor
{
    public IReadOnlyList<GrpcMethodDescriptor> Methods => [
        new GrpcMethodDescriptor("Greeter", "SayHello", GrpcMethodType.Unary, typeof(GreeterService),
            async (svc, ctx, ct) =>
            {
                var req = await ctx.ReadRequestAsync<HelloRequest>(ct);
                await ctx.WriteResponseAsync(new HelloReply { Message = $"Hello {req.Name}" }, ct);
            })
    ];
}
```

5-byte gRPC framing (compression flag + 4-byte length prefix) over HTTP/2 and HTTP/1.1. Unary and server-streaming method types supported.

---

## IHostedService

Run background work tied to the server lifecycle:

```csharp
var builder = CosmoWebApplicationBuilder.Create()
    .AddHostedService<MetricsCollector>()
    .AddHostedService<BackgroundIndexer>(sp => new BackgroundIndexer(sp.GetRequiredService<IRepository>()));
```

```csharp
public sealed class MetricsCollector : IHostedService
{
    public Task StartAsync(CancellationToken ct) { /* start timers */ return Task.CompletedTask; }
    public Task StopAsync(CancellationToken ct) { /* flush metrics */ return Task.CompletedTask; }
}
```

---

## Changelog

### v2.1.4
- **HTTP/3 production-ready** — interop validated (32/32 PASS via `tools/run_windows_interop.ps1`); all HTTP/3 roadmap items complete. 313 tests pass on macOS (Network.framework) and Windows (MsQuic).
- **SignalR** — JSON + MessagePack protocols now run through `IHubProtocol`, covering streaming, cancellation, `IHubContext` broadcasts, and reconnect-after-restart; the new client tests prove the path end-to-end.
- **HTTP/3 logging** — `COSMO_HTTP3_SUPPRESS_ABORT_LOGS=1` suppresses `QuicException` shutdown noise.
- Latest Windows HTTP/3 numbers (post-Phase-6): `/ping` 3,046 ops/s, `/query` 3,840 ops/s, `/headers` 5,774 ops/s.
- 313 tests.

### v2.1.2
- **Fix: Content-Type preservation for Problem Details** — `DefaultProblemDetailsService` and `TypedResults.Problem` now write raw UTF-8 bytes instead of calling `WriteJson`, which was silently overwriting `application/problem+json` with `application/json`.
- **Fix: Request timeout catch condition** — `RequestTimeoutMiddleware` now saves the original `RequestAborted` token before replacing it with the linked CTS token, so client disconnects are correctly distinguished from server-side timeouts.
- **Fix: SignalR target name casing** — `HubConnectionManager.BuildInvocation` now applies `JsonNamingPolicy.CamelCase` to the method name so clients receive `"sendMessage"` instead of `"SendMessage"`.
- **Fix: `ProblemDetails.TitleForStatus(500)`** — Changed from a non-standard phrase to the standard HTTP reason phrase `"Internal Server Error"`.
- 222 tests (up from 215).

### v2.1.1
- **Output Caching** — `AddOutputCache()` + `UseOutputCaching()`, `IOutputCacheStore`, `InMemoryOutputCacheStore`, vary-by-header/query, tag-based eviction, `X-Output-Cache: HIT/MISS`.
- **Antiforgery** — `AddAntiforgery()` + `UseAntiforgery()`, cookie + header/form token pattern, HMAC-SHA256 with `CryptographicOperations.FixedTimeEquals`, `IAntiforgeryService`, `[ValidateAntiforgery]` / `[IgnoreAntiforgery]`.
- **TypedResults** — `TypedResults.Ok/Created/Accepted/NoContent/Redirect/BadRequest/NotFound/Problem/Stream/Bytes/Text/Json/…` factory methods for minimal-API handlers.
- **IExceptionHandler** — `AddExceptionHandler<T>()`, structured exception handling chain called in registration order before the global fallback.
- **IHostedService** — `AddHostedService<T>()` / `AddHostedService<T>(factory)`, start/stop lifecycle tied to the server.
- **UseWebSockets()** — API-parity stub; `context.AcceptWebSocketAsync()` was already available.
- 215 unit tests (up from 165).

### v2.1.0
- **SignalR** — Hub base class (`Hub`), `MapHub<THub>(path)`, `HubConnectionManager`, `HubDispatcher<THub>`, `IHubContext<THub>` via `HubContextRegistry`, groups, all-except, and ASP.NET SignalR client-compatible JSON/WebSocket support with `\u001e` record separator.
- **gRPC** — 5-byte frame protocol, `GrpcServiceBase`, `IGrpcServiceDescriptor`, `MapGrpcService<T>()`, unary and server-streaming method types.
- **Sessions** — `UseSession()`, cookie-backed in-memory session with idle timeout, `SetString`/`GetString`/`SetInt32`/`GetInt32`/`Remove`/`Clear`.
- **Request Timeouts** — `UseRequestTimeouts()`, `RequestTimeoutOptions.DefaultTimeout`, `504 Gateway Timeout` on breach.
- **Response Caching** — `UseResponseCaching()`, ETag/304 conditional-GET support.
- **Forwarded Headers** — `UseForwardedHeaders()`, `X-Forwarded-For/Host/Proto`.
- **Request Decompression** — `UseRequestDecompression()`, GZip/Deflate/Brotli.
- **Distributed Tracing** — `UseTracing()`, W3C `traceparent`/`tracestate`, `ActivitySource` (OpenTelemetry-compatible).
- **Endpoint Filters** — `AddEndpointFilter()` on minimal-API routes.
- **IHttpContextAccessor** — `AddHttpContextAccessor()`, `AsyncLocal`-based ambient context.
- 165 unit tests (up from 108).

### v2.0.7
- **Stream flush coalescing** — `Http3DataFrameStream.CompleteAsync` double final-frame bug fixed. `hasStagedData` is now captured before clearing `_staging`, eliminating spurious empty DATA frames that caused `QuicException: Stream aborted by peer (268)` (H3_REQUEST_CANCELLED) under repeated large-response workloads.
- Stream disposal moved to `Task.Run()` to reduce blocking on the QUIC stream-accept loop. (Note: this was later reversed in Phase 6 — stream disposal is now awaited inline to eliminate the 4–6 ms P99 spikes the `Task.Run` scheduling introduced.)

### v2.0.6
- **HTTP/3** — QPACK dynamic table encode/decode, request and response trailers, server-initiated GOAWAY, graceful shutdown, feature-parity coverage for HEAD, ranges, forms, multipart, OpenAPI, Swagger UI, and auth/header propagation.
- HTTP/3 benchmark: `/large-json` +66%, `/stream` +33%, `/file` +57% vs pre-optimization baseline.

### v2.0.5
- **Health Checks** — `AddHealthChecks()`, `AddCheck<T>()` / `AddCheck(name, fn)`, `HealthCheckMiddleware`, `HealthCheckService`, `HealthStatus` (Healthy/Degraded/Unhealthy), `HealthReport`.
- **Problem Details** — RFC 7807 `ProblemDetails`, `IProblemDetailsService`, `DefaultProblemDetailsService`, `AddProblemDetails()`.
- **Policy-Based Authorization** — `AddAuthorization()`, `IAuthorizationService`, `IAuthorizationRequirement`, `[Authorize(Policy="...")]`.
- **OAuth/OIDC** — `UseOAuthAuthentication()` with JWKS discovery.
- **Memory Cache** — `AddMemoryCache()`, `IMemoryCache`.
- **Distributed Cache** — `AddDistributedMemoryCache()`, `IDistributedCache`.
- **IHttpClientFactory** — `AddHttpClient()`, named and typed clients.

### v2.0.1
- **Streaming performance** — `ChunkedBodyStream` now stages multiple `WriteAsync` calls into a single chunk per `FlushAsync`, eliminating one chunk-header per write. NDJSON streaming throughput improved from ~4,300 to ~8,200 ops/s.
- **Fixed `Flush()` blocking** — Sync `Flush()` on stream writers was calling `.GetAwaiter().GetResult()` on an async pipe flush, risking thread-pool starvation. Now a no-op; callers must use `FlushAsync`.
- **Fixed duplicate response headers** — `WriteStreamingResponseAsync` did not set `_headersWritten`, causing `EnsureHeadersWritten()` to append a second set of HTTP headers after the body.
- **Fixed connection lifecycle** — `Http11Connection` now shares a `CancellationTokenSource` between `FillPipeAsync` and `ProcessAsync`. When either side finishes, the other is cancelled, preventing `FillPipeAsync` from blocking indefinitely on `stream.ReadAsync` after a `Connection: close` response.
- **WebSocket masked frames** — Server now enforces RFC 6455 client-to-server masking requirement.
- **Component validation on all HTTP methods** — `ComponentScanner` previously only validated form parameters on `POST`; now runs on all methods.
- **`CascadingParameter` ModelState** — `FindCascadingValue` now cascades parent `ModelState` to child components that request `Dictionary<string, string>`.
- 108 unit tests (up from 102).

---

## Credits

**Razor Components** in CosmoApiServer is inspired by **Microsoft Blazor**, but implemented as a lightweight, SSR-only engine focused on raw performance and zero dependencies. The form system (`EditForm`, `InputText`, `EditContext`, change detection) follows Blazor's API surface while adding snapshot-based dirty tracking that Blazor SSR does not provide.

Portions of the templating engine are based on the excellent **[RazorSlices](https://github.com/DamianEdwards/RazorSlices)** project by **Damian Edwards**, licensed under the **MIT License**.
