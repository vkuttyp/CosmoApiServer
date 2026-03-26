using CosmoApiServer.Core.Hosting;
using CosmoApiServer.Core.Http;
using CosmoKitchenSink.Services;
using Microsoft.Extensions.DependencyInjection;

var builder = CosmoWebApplicationBuilder.Create(args);

// Add Services
builder.Services.AddSingleton<DataService>();

// Configure Server
builder.ListenOn(9102);
builder.UseStaticFiles(Path.Combine(AppContext.BaseDirectory, "../../../wwwroot"));

// Add automatic Razor Component discovery
builder.AddRazorComponents();

var app = builder.Build();

// Static Health Check
app.MapGet("/api/health", ctx => {
    ctx.Response.WriteJson(new { status = "ok", time = DateTime.UtcNow });
    return ValueTask.CompletedTask;
});

Console.WriteLine("CosmoKitchenSink running on http://localhost:9102");
app.Run();
