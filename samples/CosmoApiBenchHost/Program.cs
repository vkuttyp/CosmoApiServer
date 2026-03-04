using System;
using System.Threading.Tasks;
using CosmoApiServer.Core.Hosting;
using CosmoApiServer.Core.Controllers;
using CosmoApiServer.Core.Controllers.Attributes;
using CosmoApiServer.Core.Controllers.Filters;

// Benchmark server matching CosmoApiServer-Swift bench routes:
//   GET  /ping           → "pong"                 (raw throughput)
//   GET  /json           → {"status":"ok",...}    (JSON serialization)
//   POST /echo           → body echoed back       (request parsing + body write)
//   GET  /route/{id}     → route param extraction (routing performance)
//   GET  /middleware     → full stack traversal   (all middleware)

var app = CosmoWebApplicationBuilder.Create()
    .ListenOn(9001)
    .UseExceptionHandler()
    .UseRateLimiting(opts => { opts.Limit = 1000000; }) // high limit for bench
    .UseCsrf()
    .UseHsts()
    .UseResponseCompression(opts => { opts.MinimumSize = 0; }) // compress everything for bench
    .UseOpenApi()
    .AddControllers()
    .Build();

app.MapGet("/ping", async ctx => ctx.Response.WriteText("pong"));

app.MapGet("/json", async ctx =>
    ctx.Response.WriteJson(new {
        status = "ok",
        timestamp = DateTime.UtcNow.ToString("o"),
        server = "CosmoApiServer-DotNet"
    }));

app.MapPost("/echo", async ctx =>
{
    var ct = ctx.Request.Headers.TryGetValue("content-type", out var v) ? v : "application/octet-stream";
    ctx.Response.Headers["Content-Type"] = ct;
    ctx.Response.Write(ctx.Request.Body);
});

app.MapGet("/route/{id}", async ctx =>
{
    var id = ctx.Request.RouteValues.TryGetValue("id", out var val) ? val?.ToString() ?? "unknown" : "unknown";
    ctx.Response.WriteJson(new { id });
});

app.MapGet("/middleware", async ctx =>
    ctx.Response.WriteJson(new { path = ctx.Request.Path, method = ctx.Request.Method }));

Console.WriteLine("=== CosmoApiServer-DotNet Benchmark ===");
Console.WriteLine("HTTP/1.1  → http://127.0.0.1:9001");
Console.WriteLine($"Threads: {Environment.ProcessorCount}");
app.Run();

public class SearchModel
{
    public int Page { get; set; }
    public string? Q { get; set; }
}

public class BenchFilter : ActionFilterAttribute
{
    public override Task OnActionExecutingAsync(ActionExecutingContext context) => Task.CompletedTask;
    public override Task OnActionExecutedAsync(ActionExecutedContext context) => Task.CompletedTask;
}

public class BenchController : ControllerBase
{
    [HttpGet("/complex")]
    public object Complex([FromQuery] SearchModel search)
    {
        return search;
    }

    [HttpGet("/filtered")]
    [BenchFilter]
    public string Filtered() => "filtered";
}
