using System.Reflection;
using CosmoApiServer.Core.Controllers;
using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;
using CosmoApiServer.Core.Routing;
using CosmoApiServer.Core.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CosmoApiServer.Core.Hosting;

/// <summary>
/// The running web application. Maps convention routes and starts the server.
/// Analogous to WebApplication in ASP.NET.
/// </summary>
public sealed class CosmoWebApplication
{
    private readonly IServiceProvider _services;
    public IServiceProvider Services => _services;
    private readonly MiddlewarePipeline _middlewarePipeline;
    private readonly RouteTable _routeTable;
    private readonly List<Assembly> _controllerAssemblies;
    private readonly List<Assembly> _componentAssemblies;
    private readonly ServerOptions _options;
    private readonly PipelineHttpServer _server;

    internal CosmoWebApplication(
        IServiceProvider services,
        MiddlewarePipeline middlewarePipeline,
        RouteTable routeTable,
        List<Assembly> controllerAssemblies,
        List<Assembly> componentAssemblies,
        ServerOptions options)
    {
        _services = services;
        _middlewarePipeline = middlewarePipeline;
        _routeTable = routeTable;
        _controllerAssemblies = controllerAssemblies;
        _componentAssemblies = componentAssemblies;
        _options = options;
        _server = new PipelineHttpServer();
    }

    // ── Convention routing ─────────────────────────────────────────────────

    public CosmoWebApplication MapGet(string template, RequestDelegate handler)
    {
        _routeTable.Add(Http.HttpMethod.GET, template, handler);
        return this;
    }

    public CosmoWebApplication MapPost(string template, RequestDelegate handler)
    {
        _routeTable.Add(Http.HttpMethod.POST, template, handler);
        return this;
    }

    public CosmoWebApplication MapPut(string template, RequestDelegate handler)
    {
        _routeTable.Add(Http.HttpMethod.PUT, template, handler);
        return this;
    }

    public CosmoWebApplication MapDelete(string template, RequestDelegate handler)
    {
        _routeTable.Add(Http.HttpMethod.DELETE, template, handler);
        return this;
    }

    public CosmoWebApplication MapPatch(string template, RequestDelegate handler)
    {
        _routeTable.Add(Http.HttpMethod.PATCH, template, handler);
        return this;
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        // Scan + register attribute-based controllers
        ControllerScanner.RegisterControllers(_controllerAssemblies, _routeTable, _services);

        // Scan + register attribute-based components
        ComponentScanner.RegisterComponents(_componentAssemblies, _routeTable, _services);

        // Build the full pipeline: middleware chain → router (terminal)
        var router = new RouterMiddleware(_routeTable);
        _middlewarePipeline.UseInstance(router);
        var pipeline = _middlewarePipeline.Build(ctx =>
        {
            ctx.Response.StatusCode = 404;
            ctx.Response.WriteText("Not Found");
            return ValueTask.CompletedTask;
        });

        // ── Start IHostedServices ──────────────────────────────────────────
        var hostedServices = _services.GetServices<IHostedService>().ToArray();
        foreach (var service in hostedServices)
        {
            await service.StartAsync(cancellationToken);
        }

        await _server.StartAsync(
            _options.Port, pipeline, _services, _options.MaxRequestBodySize,
            _options.CertificatePath, _options.CertificatePassword, _options.EnableHttp2,
            _options.ConnectionTimeoutSeconds,
            cancellationToken);

        // Wait until cancelled
        var tcs = new TaskCompletionSource();
        using var registration = cancellationToken.Register(() => tcs.TrySetResult());

        if (!cancellationToken.CanBeCanceled)
        {
            // Block until Ctrl+C
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                tcs.TrySetResult();
            };
        }

        await tcs.Task;

        // ── Stop IHostedServices ───────────────────────────────────────────
        foreach (var service in hostedServices.Reverse())
        {
            try { await service.StopAsync(CancellationToken.None); }
            catch (Exception ex) { Console.Error.WriteLine($"Error stopping hosted service {service.GetType().Name}: {ex.Message}"); }
        }

        await _server.DisposeAsync();
    }

    public void Run(CancellationToken cancellationToken = default) =>
        RunAsync(cancellationToken).GetAwaiter().GetResult();
}
