using CosmoApiServer.Core.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WeatherApp.Services;

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .Build();

var port = config.GetValue<int>("Server:Port", 8080);
var connectionString = config.GetConnectionString("MsSql")
    ?? throw new InvalidOperationException("ConnectionStrings:MsSql is missing from appsettings.json");

var builder = CosmoWebApplicationBuilder.Create()
    .ListenOn(port)
    .UseLogging()
    .UseCors()
    .UseJwtAuthentication(opts => config.GetSection("Jwt").Bind(opts))
    .AddControllers();

builder.Services.AddSingleton<IWeatherService, WeatherService>();
builder.Services.AddSingleton(new SqlService(connectionString));

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
Console.WriteLine("  GET    /sql/query?sql=...      [Authorize]    -> stream rows as JSON");
Console.WriteLine("  GET    /sql/json-stream?sql=.. [Authorize]    -> rows serialized to JSON objects");
Console.WriteLine("  GET    /sql/for-json?sql=..    [Authorize]    -> FOR JSON PATH results");
Console.WriteLine("  GET    /health                               -> health check");
Console.WriteLine();
Console.WriteLine("  Test users:  admin / admin123   |   viewer / viewer123");
app.Run();
