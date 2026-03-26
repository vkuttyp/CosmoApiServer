using CosmoApiServer.Core.Hosting;
using CosmoSQLClient.MsSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

var port = 9101; // For benchmark
/* 
var connectionString = config.GetConnectionString("MsSql")
    ?? throw new InvalidOperationException("ConnectionStrings:MsSql is missing from appsettings.json");

var poolConfig = MsSqlConfiguration.Parse(connectionString);
var pool = new MsSqlConnectionPool(poolConfig, maxConnections: 10, minIdle: 2);
await pool.WarmUpAsync();
*/

var builder = CosmoWebApplicationBuilder.Create()
    .ListenOn(port)
    .UseLogging()
    .UseStaticFiles()
    .UseCors()
    .AddControllers()
    .AddRazorComponents();

// builder.Services.AddSingleton(pool);

var app = builder.Build();

app.MapGet("/health", async ctx =>
    ctx.Response.WriteJson(new { status = "healthy", app = "BlazorSqlSample", time = DateTime.UtcNow }));

app.MapGet("/ping", ctx => {
    ctx.Response.WriteText("pong");
    return ValueTask.CompletedTask;
});

app.MapGet("/bench", async ctx => {
    var component = new BlazorSqlSample.Components.Benchmark();
    component.HttpContext = ctx;
    await component.RenderToResponseAsync(ctx.Response);
});

Console.WriteLine($"BlazorSqlSample running on http://localhost:{port}");
Console.WriteLine("  GET  /query        -> SQL Query Tool (Razor Components)");
Console.WriteLine("  GET  /health       -> Health Check");

app.Run();
