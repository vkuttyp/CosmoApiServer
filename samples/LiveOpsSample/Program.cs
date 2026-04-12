using System.Text.Json.Serialization;
using CosmoApiServer.Core.Hosting;
using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;

// ── Detect environment ────────────────────────────────────────────────────────

var isDev = (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development")
    .Equals("Development", StringComparison.OrdinalIgnoreCase);

// ── Builder ───────────────────────────────────────────────────────────────────

var builder = CosmoWebApplicationBuilder.Create()
    .ListenOn(9092)
    .UseLogging()
    .UseExceptionHandler()

    // Lock down allowed origins in production.
    // In dev, CORS is needed because the Nuxt dev server runs on a different port.
    .UseCors(o =>
    {
        if (isDev) o.AllowAnyOrigin();
        else o.AllowedOrigins = ["https://liveops.example.com"];
        o.AllowAnyMethod();
        o.AllowAnyHeader();
    })

    // ── Feature 5: Content Security Policy ───────────────────────────────────
    // CspMiddleware generates a per-request nonce and injects it into every
    // inline <script> tag that ViteFrontendMiddleware emits.
    .UseCsp(o =>
    {
        o.DefaultSrc = ["'self'"];
        o.ScriptSrc  = ["'self'", "'nonce-{nonce}'"];
        o.StyleSrc   = ["'self'", "'unsafe-inline'"];  // Nuxt UI injects inline styles
        o.ConnectSrc = isDev
            ? ["'self'", "ws://localhost:*", "http://localhost:*"]  // HMR needs broad connect-src in dev
            : ["'self'"];
        o.ImgSrc     = ["'self'", "data:"];
        o.FontSrc    = ["'self'"];
    });

if (isDev)
{
    // ── Feature 2: Dev process manager ───────────────────────────────────────
    // Spawns `npm run dev` inside /frontend and waits until it prints "Local:"
    // before the backend starts accepting connections. Replaces run-dev.sh.
    builder.UseViteDevServer(o =>
    {
        o.WorkingDirectory = "frontend";
        o.Command          = "npm";
        o.Arguments        = "run dev";
        o.ReadyPattern     = "Local:";
        o.ReadyTimeout     = TimeSpan.FromSeconds(45);
        o.LogPrefix        = "[nuxt]";
    });

    // ── Feature 1: Vite dev proxy ─────────────────────────────────────────────
    // Forwards /@vite, /_nuxt, /@fs etc. to the Nuxt dev server on port 3000,
    // enabling the browser to load the Vue app from the same origin as the API.
    builder.UseViteDevProxy(o =>
    {
        o.DevServerUrl    = "http://127.0.0.1:3000";
        o.ProxiedPrefixes = ["/@vite", "/@fs", "/@id", "/_nuxt", "/__nuxt", "/__vite_ping"];
    });

    // In dev, ViteFrontendMiddleware renders the app shell pointing at the dev server
    // for HMR. The dev server handles module reloading; Cosmo handles the API.
    builder.UseViteFrontend(o =>
    {
        o.DevServerUrl      = "http://127.0.0.1:3000";
        o.HtmlTemplatePath  = "frontend/app/app.html";
        o.ExcludedPrefixes  = ["/api", "/health", "/ping"];
    });
}
else
{
    // ── Feature 4: Nuxt integrated production deployment ──────────────────────
    // Serves the built Nuxt SPA (from `npm run build:integrated`) — static assets
    // with correct cache headers, response compression, and SPA fallback.
    builder.UseNuxtIntegrated(
        outputPath: "frontend/.output/public",
        configureFallback: o => o.ExcludedPrefixes = ["/api", "/health", "/ping"]);

    // Production SSR alternative (not used here but shown for reference):
    // builder.UseReverseProxy(o =>
    //     o.Routes.Add(new ProxyRoute
    //     {
    //         PathPrefix       = "/",
    //         Destination      = "http://127.0.0.1:3000",   // nuxt start port
    //         ExcludedPrefixes = ["/api", "/health"]
    //     }));
}

var app = builder.Build();

// ── Static data ───────────────────────────────────────────────────────────────

var services = new[]
{
    new ServiceStatus("API Gateway",           "healthy",  "99.98%", "Riyadh",   18),
    new ServiceStatus("Order Processor",       "healthy",  "99.95%", "Jeddah",   7),
    new ServiceStatus("Notification Service",  "degraded", "98.12%", "Dubai",    4),
    new ServiceStatus("Analytics Pipeline",    "healthy",  "99.91%", "Riyadh",   12),
    new ServiceStatus("Auth Service",          "healthy",  "100%",   "Jeddah",   3),
};

var logPool = new[]
{
    ("info",  "order-processor",        "Processed batch of {n} orders in {ms}ms"),
    ("info",  "api-gateway",            "Rate limit window reset for tenant {t}"),
    ("warn",  "notification-service",   "Retry {r}/3 for delivery to {t}"),
    ("info",  "analytics-pipeline",     "Ingested {n} events from {t}"),
    ("error", "notification-service",   "SMTP connection timeout after {ms}ms"),
    ("info",  "auth-service",           "Issued {n} tokens in the last minute"),
    ("info",  "order-processor",        "Queue depth is {n} — within SLA"),
    ("warn",  "api-gateway",            "P99 latency at {ms}ms — above 150ms threshold"),
    ("info",  "analytics-pipeline",     "Compaction run finished, freed {n}MB"),
};

// ── API: ping (used by benchmark runner) ─────────────────────────────────────

app.MapGet("/ping", ctx =>
{
    ctx.Response.WriteText("pong");
    return ValueTask.CompletedTask;
});

// ── API: status snapshot ──────────────────────────────────────────────────────

app.MapGet("/api/status", ctx =>
{
    ctx.Response.WriteJson(new StatusResponse(
        Environment: isDev ? "development" : "production",
        Version: "3.1.0",
        Region: "ME-Central",
        Services: services));
    return ValueTask.CompletedTask;
});

app.MapGet("/health", ctx =>
{
    ctx.Response.WriteJson(new { status = "ok", server = "LiveOpsSample", time = DateTime.UtcNow });
    return ValueTask.CompletedTask;
});

// ── Feature 3: SSE — live metric stream ───────────────────────────────────────
// MapSse sets the text/event-stream headers and calls BeginSseAsync automatically.
// The handler loops until the client disconnects (RequestAborted is triggered).

app.MapSse("/api/live/metrics", async ctx =>
{
    var rng = new Random();

    // Seed realistic baseline values
    double cpu    = rng.NextDouble() * 30 + 25;
    double memory = rng.NextDouble() * 20 + 55;
    long   reqs   = rng.NextInt64(800, 1400);

    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
    while (!ctx.RequestAborted.IsCancellationRequested &&
           await timer.WaitForNextTickAsync(ctx.RequestAborted).ConfigureAwait(false))
    {
        // Drift values gradually for a realistic feel
        cpu    = Math.Clamp(cpu    + (rng.NextDouble() - 0.48) * 4,  2,  98);
        memory = Math.Clamp(memory + (rng.NextDouble() - 0.49) * 2, 40,  95);
        reqs   = Math.Clamp(reqs   + rng.NextInt64(-40, 55),        100, 5000);

        var snapshot = new MetricSnapshot(
            Cpu:       Math.Round(cpu,    1),
            Memory:    Math.Round(memory, 1),
            Requests:  reqs,
            LatencyMs: rng.Next(18, 220),
            Timestamp: DateTime.UtcNow);

        await ctx.Response.WriteSseAsync(
            System.Text.Json.JsonSerializer.Serialize(snapshot, JsonOptions.Default),
            eventName: "metric",
            cancellationToken: ctx.RequestAborted);
    }
});

// ── Feature 3: SSE — live log stream ─────────────────────────────────────────

app.MapSse("/api/live/logs", async ctx =>
{
    var rng = new Random();

    // Interleave heartbeat pings so proxies don't close idle connections.
    var heartbeat = ctx.Response.SendSseHeartbeatsAsync(
        TimeSpan.FromSeconds(15), ctx.RequestAborted);

    var tenants = new[] { "Northwind Retail", "Atlas Health", "Signal Foundry" };

    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(rng.Next(800, 2500)));
    while (!ctx.RequestAborted.IsCancellationRequested &&
           await timer.WaitForNextTickAsync(ctx.RequestAborted).ConfigureAwait(false))
    {
        var (level, service, template) = logPool[rng.Next(logPool.Length)];
        var message = template
            .Replace("{n}",  rng.Next(10, 999).ToString())
            .Replace("{ms}", rng.Next(12, 800).ToString())
            .Replace("{r}",  rng.Next(1, 4).ToString())
            .Replace("{t}",  tenants[rng.Next(tenants.Length)]);

        var entry = new LogEntry(level, service, message, DateTime.UtcNow);
        await ctx.Response.WriteSseAsync(
            System.Text.Json.JsonSerializer.Serialize(entry, JsonOptions.Default),
            eventName: "log",
            cancellationToken: ctx.RequestAborted);
    }

    await heartbeat;
});

Console.WriteLine($"LiveOpsSample running on http://127.0.0.1:9092 [{(isDev ? "dev" : "production")}]");
app.Run();

// ── Records ───────────────────────────────────────────────────────────────────

internal static class JsonOptions
{
    internal static readonly System.Text.Json.JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };
    public static string Serialize<T>(T value) =>
        System.Text.Json.JsonSerializer.Serialize(value, Default);
}

internal sealed record MetricSnapshot(
    [property: JsonPropertyName("cpu")]       double Cpu,
    [property: JsonPropertyName("memory")]    double Memory,
    [property: JsonPropertyName("requests")]  long   Requests,
    [property: JsonPropertyName("latencyMs")] int    LatencyMs,
    [property: JsonPropertyName("timestamp")] DateTime Timestamp);

internal sealed record LogEntry(
    [property: JsonPropertyName("level")]     string   Level,
    [property: JsonPropertyName("service")]   string   Service,
    [property: JsonPropertyName("message")]   string   Message,
    [property: JsonPropertyName("timestamp")] DateTime Timestamp);

internal sealed record ServiceStatus(
    [property: JsonPropertyName("name")]     string Name,
    [property: JsonPropertyName("status")]   string Status,
    [property: JsonPropertyName("uptime")]   string Uptime,
    [property: JsonPropertyName("region")]   string Region,
    [property: JsonPropertyName("replicas")] int    Replicas);

internal sealed record StatusResponse(
    [property: JsonPropertyName("environment")] string          Environment,
    [property: JsonPropertyName("version")]     string          Version,
    [property: JsonPropertyName("region")]      string          Region,
    [property: JsonPropertyName("services")]    ServiceStatus[] Services);
