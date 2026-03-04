using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders(); // match CosmoApiServer's low-overhead behavior
var app = builder.Build();

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
app.Run("http://127.0.0.1:9002");
