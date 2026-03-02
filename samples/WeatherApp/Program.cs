using CosmoApiServer.Core.Hosting;
using Microsoft.Extensions.DependencyInjection;
using WeatherApp.Services;

var builder = CosmoWebApplicationBuilder.Create()
    .ListenOn(8080)
    .UseLogging()
    .UseCors()
    .UseJwtAuthentication(opts =>
    {
        opts.Secret = "super-secret-key-change-in-production-32chars!";
        opts.Issuer = "WeatherApp";
        opts.Audience = "WeatherApp";
        opts.ExpiryMinutes = 60;
    })
    .AddControllers();

builder.Services.AddSingleton<IWeatherService, WeatherService>();

var app = builder.Build();

app.MapGet("/health", async ctx =>
    ctx.Response.WriteJson(new { status = "healthy", app = "WeatherApp", time = DateTime.UtcNow }));

Console.WriteLine("WeatherApp running on http://localhost:8080");
Console.WriteLine();
Console.WriteLine("  POST   /auth/login    { username, password }  -> JWT token");
Console.WriteLine("  GET    /auth/me       [Authorize]             -> current user");
Console.WriteLine("  GET    /weather       [Authorize]             -> all forecasts");
Console.WriteLine("  GET    /weather/{id}  [Authorize]             -> single forecast");
Console.WriteLine("  POST   /weather       [Authorize]             -> create forecast");
Console.WriteLine("  DELETE /weather/{id}  [Authorize]             -> delete forecast");
Console.WriteLine("  GET    /health                                -> health check");
Console.WriteLine();
Console.WriteLine("  Test users:  admin / admin123   |   viewer / viewer123");
app.Run();
