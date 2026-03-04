using System.Threading.Channels;
using CosmoApiServer.Core.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = CosmoWebApplicationBuilder.Create()
    .ListenOn(5005)
    // 1. Global Exception Handling
    .UseExceptionHandler()
    // 2. Logging
    .UseLogging()
    // 3. HTTPS Redirection & HSTS (Disabled for local sample ease of use, but available)
    // .UseHttpsRedirection()
    // .UseHsts()
    // 4. Rate Limiting (100 requests per minute per IP)
    .UseRateLimiting(opts => 
    {
        opts.Limit = 100;
        opts.Window = TimeSpan.FromMinutes(1);
    })
    // 5. CSRF Protection (Requires X-XSRF-TOKEN header for POST/PUT/DELETE)
    .UseCsrf()
    // 6. Response Compression (GZip)
    .UseResponseCompression()
    // 7. Static Files (from wwwroot)
    .UseStaticFiles()
    // 8. OpenAPI/Swagger (Available at /openapi.json)
    .UseOpenApi("/openapi.json", info => 
    {
        info.Title = "Cosmo Feature Showcase API";
        info.Description = "Demonstrating high-performance features of CosmoApiServer.";
        info.Version = "v1.0.0";
    })
    // 9. Controllers (Scans for [HttpGet], etc.)
    .AddControllers();

// Dependency Injection example
builder.Services.AddSingleton<IMyService, MyService>();
builder.Services.AddHostedService<MyBackgroundTask>();

// ── Channels + IAsyncEnumerable Showcase ──────────────────────────────────
var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(100) 
{ 
    FullMode = BoundedChannelFullMode.DropOldest 
});
builder.Services.AddSingleton(channel.Reader);
builder.Services.AddSingleton(channel.Writer);
builder.Services.AddHostedService<MessageProducerService>();

var app = builder.Build();

Console.WriteLine("🚀 Cosmo Feature Showcase started!");
Console.WriteLine("🌐 Static Files: http://localhost:5005/");
Console.WriteLine("📄 OpenAPI Spec: http://localhost:5005/openapi.json");
Console.WriteLine("⚡ WebSockets:   ws://localhost:5005/ws");
Console.WriteLine("📡 Streaming:    http://localhost:5005/api/stream/events");

app.Run();

// Dummy service for DI demonstration
public interface IMyService { string GetData(); }
public class MyService : IMyService { public string GetData() => "Data from DI Service"; }

// Background task example using IHostedService
public class MyBackgroundTask : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Console.WriteLine($"[Background] Task is running at {DateTime.Now}");
            await Task.Delay(10000, stoppingToken);
        }
    }
}

// Service that produces messages into a channel
public class MessageProducerService(ChannelWriter<string> writer) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var count = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            await writer.WriteAsync($"Message #{++count} at {DateTime.Now:T}", stoppingToken);
            await Task.Delay(2000, stoppingToken); // Produce every 2s
        }
    }
}
