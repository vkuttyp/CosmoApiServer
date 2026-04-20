using System.Security.Claims;
using System.Text.Json.Serialization;
using CosmoApiServer.Core.Auth;
using CosmoApiServer.Core.Hosting;
using CosmoApiServer.Core.Http;
using Microsoft.Extensions.DependencyInjection;

var builder = CosmoWebApplicationBuilder.Create()
    .ListenOn(9091)
    .UseLogging()
    .UseCors(options =>
    {
        // Lock down allowed origins in production via COSMO_CORS_ORIGINS
        // (comma-separated). Falls back to wildcard when the variable is absent
        // so local dev works without extra config.
        // Example: COSMO_CORS_ORIGINS=https://app.example.com,https://staging.example.com
        var originsEnv = Environment.GetEnvironmentVariable("COSMO_CORS_ORIGINS");
        if (!string.IsNullOrWhiteSpace(originsEnv))
        {
            var origins = originsEnv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            options.AllowedOrigins = origins;
        }
        else
        {
            options.AllowAnyOrigin();
        }
        options.AllowAnyMethod();
        options.AllowAnyHeader();
    })
    .UseSession()
    .UseJwtAuthentication(opts =>
    {
        opts.Secret = Environment.GetEnvironmentVariable("JWT_SECRET") ?? "CosmoNuxtSample-DevOnly-Key-32chars!";
        opts.Issuer = "CosmoNuxtUiSample";
        opts.Audience = "CosmoNuxtUiSample";
        opts.ExpiryMinutes = 60;
    });

var app = builder.Build();

static string ToSlug(string value)
{
    return value
        .Trim()
        .ToLowerInvariant()
        .Replace(" ", "-", StringComparison.Ordinal);
}

var dashboard = new DashboardResponse(
    "Nuxt UI Control Room",
    "Operational",
    "Canary",
    "99.982%",
    [
        new MetricCard("Active sessions", "14,284", "+8.4% this week", "primary"),
        new MetricCard("Automations shipped", "126", "23 launched today", "secondary"),
        new MetricCard("Feedback SLA", "2h 14m", "Median first response", "warning")
    ],
    [
        new WorkspaceSummary("Northwind Retail", "Riyadh", 18, "Live"),
        new WorkspaceSummary("Atlas Health", "Jeddah", 11, "Review"),
        new WorkspaceSummary("Signal Foundry", "Dubai", 26, "Live")
    ],
    [
        new TimelineItem("Design system sync finished", "Shared color tokens updated across Nuxt UI surfaces.", "5 minutes ago"),
        new TimelineItem("Backend deploy completed", "Cosmo worker pool rolled without connection drops.", "42 minutes ago"),
        new TimelineItem("Enterprise workspace onboarded", "Atlas Health imported 3,200 records.", "Today, 08:30")
    ]);

var workspaceDetails = new WorkspaceDetailResponse(
    [
        new WorkspaceDetail(
            "Northwind Retail",
            "Riyadh",
            "Live",
            18,
            97,
            "Amina Rahman",
            ["Inventory", "Forecasting", "Executive"],
            [
                new DeploymentNote("Insights rollout", "Completed", "Deployed the new panel to all merchandisers."),
                new DeploymentNote("Support queue", "Watching", "Two low-priority UX comments remain open.")
            ]),
        new WorkspaceDetail(
            "Atlas Health",
            "Jeddah",
            "Review",
            11,
            83,
            "Mazen Alharbi",
            ["Compliance", "Approvals"],
            [
                new DeploymentNote("Security sign-off", "Pending", "Awaiting final audit attachment from procurement."),
                new DeploymentNote("Pilot cohort", "Ready", "Doctors and admins validated the import workflow.")
            ]),
        new WorkspaceDetail(
            "Signal Foundry",
            "Dubai",
            "Live",
            26,
            94,
            "Nora Haddad",
            ["Automation", "Analytics", "Export"],
            [
                new DeploymentNote("Batch jobs", "Completed", "Nightly export now finishes 18% faster."),
                new DeploymentNote("Executive review", "Scheduled", "Quarterly ops review set for tomorrow 10:00.")
            ])
    ]);
var workspaceMap = workspaceDetails.Items.ToDictionary(static item => ToSlug(item.Name), StringComparer.OrdinalIgnoreCase);

app.MapGet("/api/health", ctx =>
{
    ctx.Response.WriteJson(new
    {
        status = "ok",
        server = "CosmoApiServer",
        sample = "NuxtUiSample",
        time = DateTime.UtcNow
    });
    return ValueTask.CompletedTask;
});

app.MapGet("/api/dashboard", ctx =>
{
    ctx.Response.WriteJson(dashboard);
    return ValueTask.CompletedTask;
});

app.MapGet("/api/workspaces", ctx =>
{
    ctx.Response.WriteJson(workspaceDetails);
    return ValueTask.CompletedTask;
});

app.MapGet("/api/workspaces/{slug}", ctx =>
{
    if (!ctx.Request.RouteValues.TryGetValue("slug", out var slug) || string.IsNullOrWhiteSpace(slug))
    {
        ctx.Response.StatusCode = 400;
        ctx.Response.WriteJson(new { error = "Workspace slug is required." });
        return ValueTask.CompletedTask;
    }

    if (!workspaceMap.TryGetValue(slug, out var workspace))
    {
        ctx.Response.StatusCode = 404;
        ctx.Response.WriteJson(new { error = "Workspace not found." });
        return ValueTask.CompletedTask;
    }

    ctx.Response.WriteJson(workspace);
    return ValueTask.CompletedTask;
});

app.MapPost("/api/feedback", ctx =>
{
    var request = ctx.Request.ReadJson<FeedbackRequest>() ?? new FeedbackRequest(null, null);
    var workspace = string.IsNullOrWhiteSpace(request.Workspace) ? "General" : request.Workspace.Trim();
    var message = string.IsNullOrWhiteSpace(request.Message)
        ? "No note provided."
        : request.Message.Trim();

    ctx.Response.WriteJson(new FeedbackResponse(
        "queued",
        $"Feedback for {workspace} queued for review.",
        message.Length > 120 ? message[..120] + "..." : message));
    return ValueTask.CompletedTask;
});

// ── Auth endpoints ───────────────────────────────────────────────────────

// Demo users — replace with a real user store in production
var users = new Dictionary<string, (string Password, string Role)>(StringComparer.OrdinalIgnoreCase)
{
    ["admin"]  = ("admin123",  "Admin"),
    ["viewer"] = ("viewer123", "Viewer")
};

app.MapPost("/api/auth/login", ctx =>
{
    var body = ctx.Request.ReadJson<LoginRequest>();
    if (body is null || string.IsNullOrWhiteSpace(body.Username) || string.IsNullOrWhiteSpace(body.Password))
    {
        ctx.Response.StatusCode = 400;
        ctx.Response.WriteJson(new { error = "Username and password are required." });
        return ValueTask.CompletedTask;
    }

    if (!users.TryGetValue(body.Username, out var user) || user.Password != body.Password)
    {
        ctx.Response.StatusCode = 401;
        ctx.Response.WriteJson(new { error = "Invalid username or password." });
        return ValueTask.CompletedTask;
    }

    // Stamp the session so the backend knows who this session belongs to
    ctx.Session!.SetString("username", body.Username);
    ctx.Session!.SetString("role", user.Role);

    // Also return a JWT so the frontend can use Bearer auth for API calls
    var jwtService = ctx.RequestServices.GetRequiredService<JwtService>();
    var claims = new[]
    {
        new Claim(ClaimTypes.Name, body.Username),
        new Claim(ClaimTypes.Role, user.Role),
        new Claim("sub", body.Username)
    };
    var token = jwtService.GenerateToken(claims);

    ctx.Response.WriteJson(new
    {
        token,
        tokenType = "Bearer",
        expiresIn = 3600,
        user = new { username = body.Username, role = user.Role }
    });
    return ValueTask.CompletedTask;
});

app.MapGet("/api/auth/session", ctx =>
{
    var username = ctx.Session?.GetString("username");
    if (string.IsNullOrEmpty(username))
    {
        ctx.Response.StatusCode = 401;
        ctx.Response.WriteJson(new { authenticated = false });
        return ValueTask.CompletedTask;
    }

    var role = ctx.Session!.GetString("role") ?? "Viewer";
    ctx.Response.WriteJson(new { authenticated = true, user = new { username, role } });
    return ValueTask.CompletedTask;
});

app.MapPost("/api/auth/logout", ctx =>
{
    ctx.Session?.Clear();
    ctx.Response.WriteJson(new { success = true });
    return ValueTask.CompletedTask;
});

Console.WriteLine("NuxtUiSample backend running on http://127.0.0.1:9091");
app.Run();

internal sealed record DashboardResponse(
    string ProductName,
    string Status,
    string ReleaseChannel,
    string Uptime,
    MetricCard[] Metrics,
    WorkspaceSummary[] Workspaces,
    TimelineItem[] Timeline);

internal sealed record MetricCard(string Title, string Value, string Caption, string Tone);

internal sealed record WorkspaceSummary(string Name, string Region, int Members, string Status);

internal sealed record WorkspaceDetailResponse(WorkspaceDetail[] Items);

internal sealed record WorkspaceDetail(
    string Name,
    string Region,
    string Status,
    int Members,
    int HealthScore,
    string Owner,
    string[] Modules,
    DeploymentNote[] Notes);

internal sealed record DeploymentNote(string Title, string State, string Description);

internal sealed record TimelineItem(string Title, string Description, string At);

internal sealed record FeedbackRequest(
    [property: JsonPropertyName("workspace")] string? Workspace,
    [property: JsonPropertyName("message")] string? Message);

internal sealed record FeedbackResponse(string Status, string Message, string Preview);

internal sealed record LoginRequest(
    [property: JsonPropertyName("username")] string? Username,
    [property: JsonPropertyName("password")] string? Password);
