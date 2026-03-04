using CosmoApiServer.Core.Hosting;

// Benchmark server matching CosmoApiServer-Swift bench routes:
//   GET  /ping           → "pong"                 (raw throughput)
//   GET  /json           → {"status":"ok",...}    (JSON serialization)
//   POST /echo           → body echoed back       (request parsing + body write)
//   GET  /route/{id}     → route param extraction (routing performance)
//   GET  /middleware     → full stack traversal   (all middleware)

var app = CosmoWebApplicationBuilder.Create()
    .ListenOn(19001)
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
Console.WriteLine("HTTP/1.1  → http://127.0.0.1:19001");
Console.WriteLine($"Threads: {Environment.ProcessorCount}");
app.Run();
