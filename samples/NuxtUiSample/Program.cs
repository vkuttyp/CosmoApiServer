using System.Text.Json.Serialization;
using CosmoApiServer.Core.Hosting;

var builder = CosmoWebApplicationBuilder.Create()
    .ListenOn(9091)
    .UseLogging()
    .UseCors(options =>
    {
        options.AllowAnyOrigin();
        options.AllowAnyMethod();
        options.AllowAnyHeader();
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
