using Api.Services;
using MurshisoftData;
using MurshisoftData.Models;
using Serilog;
using Serilog.Templates;
using Serilog.Templates.Themes;
using System.Threading.Channels;
using CosmoApiServer.Core.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(new ExpressionTemplate(
        "[{@t:HH:mm:ss} {@l:u3}] {@m}\n{@x}", theme: TemplateTheme.Code))
    .CreateLogger();

Log.Information("Starting Application with CosmoApiServer");

var builder = CosmoWebApplicationBuilder.Create()
    .ListenOn(5001);

// Add Logging
builder.Services.AddLogging(lb => lb.AddSerilog());

// Add Configuration
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.Development.json", optional: true)
    .Build();
builder.Services.AddSingleton<IConfiguration>(configuration);

// Add Services
builder.Services.AddScoped<SqlServerDb>();

// Transaction Sync Channel
builder.Services.AddSingleton(_ =>
{
    var channel = Channel.CreateBounded<SyncTransJob>(new BoundedChannelOptions(1000)
    {
        FullMode = BoundedChannelFullMode.Wait
    });
    return channel;
});
builder.Services.AddHostedService<SyncRemoteBackground>();

// Stock Sync Channel
builder.Services.AddSingleton(_ =>
{
    var channel = Channel.CreateBounded<SyncStockJob>(new BoundedChannelOptions(1000)
    {
        FullMode = BoundedChannelFullMode.Wait
    });
    return channel;
});
builder.Services.AddHostedService<SyncRemoteBackground>();

// Build and Configure Pipeline
builder.UseExceptionHandler()
       .UseLogging()
       .UseOpenApi()
       .UseSwaggerUI()
       .AddControllers();

var app = builder.Build();

app.Run();
