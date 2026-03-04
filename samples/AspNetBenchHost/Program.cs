using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders(); // match CosmoApiServer's low-overhead behavior

builder.Services.AddControllers();
builder.Services.AddResponseCompression(opts => { opts.EnableForHttps = true; });
builder.Services.AddHsts(opts => { opts.MaxAge = TimeSpan.FromDays(365); });

var app = builder.Build();

app.UseExceptionHandler("/error");
app.UseHsts();
app.UseResponseCompression();

// GET /ping → "pong"
app.MapGet("/ping", () => "pong");

// GET /json → dynamic JSON object
app.MapGet("/json", () => Results.Json(new {
    status = "ok",
    timestamp = DateTime.UtcNow.ToString("o"),
    server = "AspNetCore-DotNet"
}));

// POST /echo → raw body echo
app.MapPost("/echo", async (HttpContext ctx) =>
{
    ctx.Response.ContentType = ctx.Request.ContentType ?? "application/octet-stream";
    await ctx.Request.Body.CopyToAsync(ctx.Response.Body);
});

// GET /route/{id} → route param extraction
app.MapGet("/route/{id}", (string id) => Results.Json(new { id }));

// GET /middleware → middleware traversal test
app.Use(async (context, next) => {
    if (context.Request.Path == "/middleware") {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { 
            path = context.Request.Path.Value, 
            method = context.Request.Method 
        }));
        return;
    }
    await next();
});

Console.WriteLine("=== AspNetCore-DotNet Benchmark ===");
Console.WriteLine("HTTP/1.1  → http://127.0.0.1:9002");
Console.WriteLine($"Threads: {Environment.ProcessorCount}");
app.MapControllers();

app.Run("http://127.0.0.1:9002");

public class SearchModel
{
    public int Page { get; set; }
    public string? Q { get; set; }
}

public class BenchFilter : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context) { }
    public override void OnActionExecuted(ActionExecutedContext context) { }
}

[ApiController]
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
