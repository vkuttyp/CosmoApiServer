using CosmoApiServer.Core.Hosting;
using CosmoSQLClient.MsSql;
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

// Connection pool: reuses TLS connections, no handshake overhead per request
var poolConfig = MsSqlConfiguration.Parse(connectionString);
var pool = new MsSqlConnectionPool(poolConfig, maxConnections: 10, minIdle: 2);
await pool.WarmUpAsync();

var builder = CosmoWebApplicationBuilder.Create()
    .ListenOn(port)
    .UseLogging()
    .UseCors()
    .UseJwtAuthentication(opts => config.GetSection("Jwt").Bind(opts))
    .AddControllers();

builder.Services.AddSingleton<IWeatherService, WeatherService>();
builder.Services.AddSingleton(pool);

var app = builder.Build();

app.MapGet("/health", async ctx =>
    ctx.Response.WriteJson(new { status = "healthy", app = "WeatherApp", time = DateTime.UtcNow }));

Console.WriteLine($"WeatherApp running on http://localhost:{port}");
Console.WriteLine();
Console.WriteLine("  POST /auth/login                      { username, password } -> JWT token");
Console.WriteLine("  GET  /auth/me              [Authorize] -> current user");
Console.WriteLine();
Console.WriteLine("  GET  /weather              [Authorize] -> all forecasts");
Console.WriteLine("  GET  /weather/{id}         [Authorize] -> single forecast");
Console.WriteLine("  POST /weather              [Authorize] -> create forecast");
Console.WriteLine("  DELETE /weather/{id}       [Authorize] -> delete forecast");
Console.WriteLine();
Console.WriteLine("  GET  /products             [Authorize] -> stream all products");
Console.WriteLine("  GET  /products/category/{id} [Authorize] -> stream by category");
Console.WriteLine("  GET  /products/enriched    [Authorize] -> stream with 10% discount applied");
Console.WriteLine();
Console.WriteLine("  GET  /account-trans              [Authorize] -> stream all transactions");
Console.WriteLine("  GET  /account-trans/account/{no} [Authorize] -> stream by account");
Console.WriteLine("  GET  /account-trans/enriched     [Authorize] -> stream with running total");
Console.WriteLine();
Console.WriteLine("  GET  /weather/view                   -> weather list (HTML)");
Console.WriteLine("  GET  /sql                            -> SQL Query Tool (HTML)");
Console.WriteLine();
Console.WriteLine("  GET  /health                         -> health check");
Console.WriteLine();
Console.WriteLine("  Test users:  admin / admin123   |   viewer / viewer123");

app.Run();
