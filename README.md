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
| GET /ping | **7,605 ops/s** | 6,154 ops/s | 0.13 ms | **+24%** |
| GET /json | **7,530 ops/s** | 6,238 ops/s | 0.13 ms | **+21%** |
| GET /route/{id} | **7,663 ops/s** | 6,390 ops/s | 0.13 ms | **+20%** |
| POST /echo | **7,184 ops/s** | 6,274 ops/s | 0.14 ms | **+15%** |
| GET /large-json (1000 items) | **1,337 ops/s** | 1,142 ops/s | 0.75 ms | **+17%** |
| GET /query | **8,231 ops/s** | 6,897 ops/s | 0.12 ms | **+19%** |
| POST /form | **7,337 ops/s** | 6,120 ops/s | 0.14 ms | **+20%** |
| GET /headers | **7,337 ops/s** | 6,204 ops/s | 0.14 ms | **+18%** |
| GET /stream (NDJSON, 10 items) | **8,177 ops/s** | 6,892 ops/s | 0.12 ms | **+19%** |
| GET /file (64 KB) | **3,422 ops/s** | 2,150 ops/s | 0.29 ms | **+59%** |

**10 of 10 scenarios win** on Windows. The `/stream` result flipped from −20% (pre-2.0.7) to +19% after the flush-coalescing fix.

### Windows HTTP/3

Experimental HTTP/3 is benchmarkable on the Windows 11 VM and serves real traffic, but it is **not stable enough for production** yet. The latest run (post Phase 6 optimization) showed significant improvements across all scenarios with 1000/1000 successful requests:

| Scenario | CosmoApiServer HTTP/3 | P50 |
|---|---|---|
| GET /ping | 2,481 ops/s | 0.40 ms |
| GET /json | 2,605 ops/s | 0.38 ms |
| GET /route/{id} | 2,494 ops/s | 0.40 ms |
| POST /echo | 2,218 ops/s | 0.45 ms |
| GET /large-json (1000 items) | 1,081 ops/s | 0.93 ms |
| GET /query | 3,276 ops/s | 0.31 ms |
| POST /form | 2,701 ops/s | 0.37 ms |
| GET /headers | 6,116 ops/s | 0.16 ms |
| GET /stream (NDJSON, 10 items) | 2,581 ops/s | 0.39 ms |
| GET /file (64 KB) | 1,464 ops/s | 0.68 ms |

Notable improvements vs the pre-optimization baseline: `/large-json` +66%, `/stream` +33%, `/file` +57%. The remaining work is stream-reuse stability under repeated larger responses.

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
- Experimental HTTP/3 over QUIC (`UseHttp3()`) with request trailers, response trailers, streamed request bodies, NDJSON streaming responses, dynamic QPACK decode, and graceful GOAWAY handling
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
- **SignalR** — Hub base class, `MapHub<THub>(path)`, `IHubContext<THub>`, groups, all-except, JSON protocol
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
- **Zero-Copy File Serving** — `HttpResponse.SendFileAsync()` streams directly from disk to socket

---

## HTTP/3

`CosmoApiServer` now includes experimental HTTP/3 support over QUIC.

Current scope:

- Buffered request/response handling over HTTP/3
- NDJSON streaming responses over HTTP/3 DATA frames
- Streamed request bodies across multiple DATA frames
- Request trailers and response trailers
- Dynamic QPACK request decoding with blocked-stream handling
- Basic graceful shutdown via GOAWAY
- Feature-parity coverage for HEAD, static files, ranges, forms, multipart uploads, OpenAPI, Swagger UI, and auth/header propagation

Enable it on a TLS listener:

```csharp
var builder = CosmoWebApplicationBuilder.Create()
    .UseHttps("cert.pfx", "password")
    .UseHttp3();
```

Notes:

- HTTP/3 requires TLS and runtime QUIC support on the host platform.
- `UseHttp3()` runs alongside the existing HTTP/1.1 and HTTP/2 support on the same port.
- This is still experimental. The main remaining gap is stream-reuse stability under repeated larger responses and broader external interop hardening.
- The remaining implementation plan is tracked in [`HTTP3_ROADMAP.md`](HTTP3_ROADMAP.md).

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
| `src/CosmoApiServer.Core` | Core framework library |
| `samples/BlazorSqlSample` | Replicated Blazor structure with SQL streaming and components |
| `samples/WeatherApp` | Full REST API: JWT auth, DI, streaming, CosmoSQLClient |
| `templates/CosmoRazorServerTemplate` | `dotnet new cosmorazor` template |
| `tests/CosmoApiServer.Core.Tests` | 108 unit tests for routing, middleware, components, forms, change detection |

---

## Changelog

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
