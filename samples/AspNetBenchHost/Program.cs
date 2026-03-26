using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

// GET /large-json → 1000-item JSON array
app.MapGet("/large-json", () => Results.Json(
    Enumerable.Range(1, 1000).Select(i => new { id = i, name = $"Item {i}", active = i % 2 == 0 }).ToList()
));

// GET /query → echo query params
app.MapGet("/query", (HttpContext ctx) => Results.Json(new {
    name = ctx.Request.Query.TryGetValue("name", out var n) ? (string?)n : "none",
    id   = ctx.Request.Query.TryGetValue("id",   out var id) ? (string?)id : "0"
}));

// POST /form → echo "test" form field
app.MapPost("/form", async (HttpContext ctx) => {
    var form = await ctx.Request.ReadFormAsync();
    var val = form.TryGetValue("test", out var v) ? (string?)v : "none";
    return Results.Text(val ?? "none");
});

// GET /headers → echo Host header
app.MapGet("/headers", (HttpContext ctx) => {
    var host = ctx.Request.Headers.TryGetValue("Host", out var h) ? (string?)h : "none";
    return Results.Text(host ?? "none");
});

// GET /stream → NDJSON stream of 10 items
app.MapGet("/stream", async (HttpContext ctx) => {
    ctx.Response.ContentType = "application/x-ndjson";
    foreach (var i in Enumerable.Range(1, 10))
    {
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { id = i }) + "\n");
        await ctx.Response.Body.FlushAsync();
    }
});

// GET /file → serve a 64KB file
var benchFilePath = Path.Combine(Path.GetTempPath(), "aspnet-bench-file.txt");
File.WriteAllText(benchFilePath, new string('A', 1024 * 64));
app.MapGet("/file", async (HttpContext ctx) => {
    ctx.Response.ContentType = "application/octet-stream";
    await ctx.Response.SendFileAsync(benchFilePath);
});

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
Console.WriteLine("HTTP/1.1  → http://127.0.0.1:9103");
Console.WriteLine($"Threads: {Environment.ProcessorCount}");
app.MapControllers();

app.Run("http://127.0.0.1:9103");

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
