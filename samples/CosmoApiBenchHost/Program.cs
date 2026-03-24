using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CosmoApiServer.Core.Hosting;
using CosmoApiServer.Core.Controllers;
using CosmoApiBenchHost;

var builder = CosmoWebApplicationBuilder.Create()
    .ListenOn(9001);

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

Console.WriteLine("=== CosmoApiServer-DotNet Benchmark ===");
Console.WriteLine("HTTP/1.1  → http://127.0.0.1:9001");
app.Run();
