using CosmoApiServer.Core.Hosting;
using CosmoApiServer.Core.Http;
using CosmoBlazorSample.Services;
using Microsoft.Extensions.DependencyInjection;

var builder = CosmoWebApplicationBuilder.Create(args);

// Add Services
builder.Services.AddSingleton<WeatherService>();

// Configure Server
builder.ListenOn(9101);
builder.UseStaticFiles(Path.Combine(AppContext.BaseDirectory, "../../../wwwroot"));

// Add automatic Razor Component discovery from this assembly
// This enables @page routing and @layout support automatically
builder.AddRazorComponents();

var app = builder.Build();

// Static Health Check
app.MapGet("/api/health", ctx => {
    ctx.Response.WriteJson(new { status = "ok", time = DateTime.UtcNow });
    return ValueTask.CompletedTask;
});

app.MapGet("/api/env", ctx => {
    var baseDir = AppContext.BaseDirectory;
    var wwwroot = Path.GetFullPath(Path.Combine(baseDir, "../../../wwwroot"));
    var exists = Directory.Exists(wwwroot);
    ctx.Response.WriteJson(new { baseDir, wwwroot, exists });
    return ValueTask.CompletedTask;
});

Console.WriteLine("CosmoBlazorSample running on http://localhost:9101");
app.Run();
