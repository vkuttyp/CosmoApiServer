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

app.MapGet("/ping", ctx => {
    ctx.Response.WriteText("pong");
    return ValueTask.CompletedTask;
});

app.MapGet("/json", ctx => {
    ctx.Response.WriteJson(new {
        status = "ok",
        timestamp = DateTime.UtcNow.ToString("o"),
        server = "CosmoApiServer-DotNet"
    });
    return ValueTask.CompletedTask;
});

app.MapPost("/echo", ctx =>
{
    var ct = ctx.Request.Headers.TryGetValue("content-type", out var v) ? v : "application/octet-stream";
    ctx.Response.Headers["Content-Type"] = ct;
    ctx.Response.Write(ctx.Request.Body);
    return ValueTask.CompletedTask;
});

app.MapGet("/route/{id}", ctx =>
{
    var id = ctx.Request.RouteValues.TryGetValue("id", out var val) ? val?.ToString() ?? "unknown" : "unknown";
    ctx.Response.WriteJson(new { id });
    return ValueTask.CompletedTask;
});

app.MapGet("/middleware", ctx => {
    ctx.Response.WriteJson(new { path = ctx.Request.Path, method = ctx.Request.Method });
    return ValueTask.CompletedTask;
});

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
    public override ValueTask OnActionExecutingAsync(ActionExecutingContext context) => ValueTask.CompletedTask;
    public override ValueTask OnActionExecutedAsync(ActionExecutedContext context) => ValueTask.CompletedTask;
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
