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
| `tests/CosmoApiServer.Core.Tests` | 102 unit tests for routing, middleware, components, forms, change detection |

---

## Credits

**Razor Components** in CosmoApiServer is inspired by **Microsoft Blazor**, but implemented as a lightweight, SSR-only engine focused on raw performance and zero dependencies. The form system (`EditForm`, `InputText`, `EditContext`, change detection) follows Blazor's API surface while adding snapshot-based dirty tracking that Blazor SSR does not provide.

Portions of the templating engine are based on the excellent **[RazorSlices](https://github.com/DamianEdwards/RazorSlices)** project by **Damian Edwards**, licensed under the **MIT License**.
