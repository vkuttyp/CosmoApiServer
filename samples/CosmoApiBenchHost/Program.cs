using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using CosmoApiServer.Core.Hosting;
using CosmoApiServer.Core.Controllers;
using CosmoApiServer.Core.Http;
using CosmoApiBenchHost;

var builder = CosmoWebApplicationBuilder.Create()
    .ListenOn(9102);

var benchItems = Enumerable.Range(1, 100).Select(i => 
    new BenchItem(i, $"Item {i}", i * 1.23, DateTime.UtcNow.AddDays(i).ToString("o"))
).ToList();

var app = builder.Build();

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

app.MapGet("/bench", ctx => {
    ctx.Response.Headers["Content-Type"] = "text/html; charset=utf-8";
    var sb = new StringBuilder();
    sb.Append("<table><thead><tr><th>ID</th><th>Name</th><th>Value</th><th>Date</th></tr></thead><tbody>");
    foreach (var item in benchItems) {
        sb.Append("<tr><td>").Append(item.id).Append("</td><td>").Append(item.name).Append("</td><td>").Append(item.value).Append("</td><td>").Append(item.date).Append("</td></tr>");
    }
    sb.Append("</tbody></table>");
    var html = sb.ToString();
    var bytes = Encoding.UTF8.GetBytes(html);
    ctx.Response.Headers["Content-Length"] = bytes.Length.ToString();
    ctx.Response.Write(bytes);
    return ValueTask.CompletedTask;
});

app.MapGet("/route/{id}", ctx => {
    var id = ctx.Request.RouteValues.TryGetValue("id", out var v) ? v : "0";
    ctx.Response.WriteJson(new { id, found = true });
    return ValueTask.CompletedTask;
});

app.MapPost("/echo", ctx => {
    ctx.Response.Headers["Content-Type"] = ctx.Request.ContentType ?? "application/json";
    ctx.Response.Write(ctx.Request.Body);
    return ValueTask.CompletedTask;
});

app.MapGet("/large-json", ctx => {
    var items = Enumerable.Range(1, 1000).Select(i => new { id = i, name = $"Item {i}", active = i % 2 == 0 }).ToList();
    ctx.Response.WriteJson(items);
    return ValueTask.CompletedTask;
});

app.MapGet("/query", ctx => {
    var name = ctx.Request.Query.TryGetValue("name", out var n) ? n : "none";
    var id = ctx.Request.Query.TryGetValue("id", out var i) ? i : "0";
    ctx.Response.WriteJson(new { name, id });
    return ValueTask.CompletedTask;
});

app.MapPost("/form", async ctx => {
    var form = await ctx.Request.ReadFormAsync();
    var val = form.Fields.TryGetValue("test", out var v) ? v : "none";
    ctx.Response.WriteText(val);
});

app.MapGet("/headers", ctx => {
    var host = ctx.Request.Headers.TryGetValue("Host", out var h) ? h : "none";
    ctx.Response.WriteText(host);
    return ValueTask.CompletedTask;
});

app.MapGet("/stream", async ctx => {
    var items = Enumerable.Range(1, 10).Select(i => new { id = i });
    await ctx.Response.WriteStreamingResponseAsync(200, async stream => {
        foreach (var item in items) {
            await System.Text.Json.JsonSerializer.SerializeAsync(stream, item);
            stream.WriteByte((byte)'\n');
            await stream.FlushAsync();
        }
    });
});

var benchFilePath = Path.Combine(Path.GetTempPath(), "cosmo-bench-file.txt");
File.WriteAllText(benchFilePath, new string('A', 1024 * 64)); // 64KB file

app.MapGet("/file", async ctx => {
    await ctx.Response.SendFileAsync(benchFilePath);
});

Console.WriteLine("=== CosmoApiServer-DotNet Benchmark ===");
Console.WriteLine("Endpoints: /ping, /json, /bench, /route/{id}, /echo, /large-json, /query, /form, /headers, /stream, /file");
app.Run();
