using CosmoApiServer.Core.Hosting;
using Microsoft.Extensions.DependencyInjection;
using WeatherApp.Services;

var builder = CosmoWebApplicationBuilder.Create()
    .ListenOn(8080)
    .UseLogging()
    .UseCors()
    .AddControllers();

// Register services
builder.Services.AddSingleton<IWeatherService, WeatherService>();

var app = builder.Build();

app.MapGet("/health", async ctx =>
    ctx.Response.WriteJson(new { status = "healthy", app = "WeatherApp", time = DateTime.UtcNow }));

Console.WriteLine("WeatherApp running on http://localhost:8080");
Console.WriteLine("  GET    /weather");
Console.WriteLine("  GET    /weather/{id}");
Console.WriteLine("  POST   /weather");
Console.WriteLine("  DELETE /weather/{id}");
Console.WriteLine("  GET    /health");
app.Run();
