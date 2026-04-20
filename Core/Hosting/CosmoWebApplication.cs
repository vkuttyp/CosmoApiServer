using System.Reflection;
using CosmoApiServer.Core.Controllers;
using CosmoApiServer.Core.Grpc;
using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;
using CosmoApiServer.Core.Routing;
using CosmoApiServer.Core.SignalR;
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

    public RouteHandlerBuilder MapGet(string template, RequestDelegate handler)
    {
        _routeTable.Add(Http.HttpMethod.GET, template, handler);
        return new RouteHandlerBuilder(_routeTable, Http.HttpMethod.GET, template, handler);
    }

    public RouteHandlerBuilder MapPost(string template, RequestDelegate handler)
    {
        _routeTable.Add(Http.HttpMethod.POST, template, handler);
        return new RouteHandlerBuilder(_routeTable, Http.HttpMethod.POST, template, handler);
    }

    public RouteHandlerBuilder MapPut(string template, RequestDelegate handler)
    {
        _routeTable.Add(Http.HttpMethod.PUT, template, handler);
        return new RouteHandlerBuilder(_routeTable, Http.HttpMethod.PUT, template, handler);
    }

    public RouteHandlerBuilder MapDelete(string template, RequestDelegate handler)
    {
        _routeTable.Add(Http.HttpMethod.DELETE, template, handler);
        return new RouteHandlerBuilder(_routeTable, Http.HttpMethod.DELETE, template, handler);
    }

    public RouteHandlerBuilder MapPatch(string template, RequestDelegate handler)
    {
        _routeTable.Add(Http.HttpMethod.PATCH, template, handler);
        return new RouteHandlerBuilder(_routeTable, Http.HttpMethod.PATCH, template, handler);
    }

    /// <summary>
    /// Maps a Server-Sent Events endpoint. Sets the required SSE headers and delegates
    /// to <paramref name="handler"/> which should call
    /// <see cref="Http.ServerSentEventsExtensions.BeginSseAsync"/> then loop
    /// <see cref="Http.ServerSentEventsExtensions.WriteSseAsync"/>.
    /// </summary>
    public RouteHandlerBuilder MapSse(string template, RequestDelegate handler)
    {
        RequestDelegate wrapped = async ctx =>
        {
            await ctx.Response.BeginSseAsync(ctx.RequestAborted);
            await handler(ctx);
        };
        _routeTable.Add(Http.HttpMethod.GET, template, wrapped);
        return new RouteHandlerBuilder(_routeTable, Http.HttpMethod.GET, template, wrapped);
    }

    // ── gRPC ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers all gRPC methods from <typeparamref name="TService"/> into the gRPC route registry.
    /// <typeparamref name="TService"/> must implement <see cref="IGrpcServiceDescriptor"/> via a
    /// static factory — or use the descriptor pattern. Call <c>builder.AddGrpc()</c> first.
    /// </summary>
    public CosmoWebApplication MapGrpcService<TService>()
        where TService : GrpcServiceBase, IGrpcServiceDescriptor
    {
        var registry = _services.GetRequiredService<Middleware.GrpcRouteRegistry>();
        // Create a temporary instance to enumerate method descriptors.
        var descriptor = (IGrpcServiceDescriptor)ActivatorUtilities.CreateInstance(_services, typeof(TService));
        foreach (var method in descriptor.Methods)
            registry.Register(method.Route, method.ServiceType, method.Handler);
        return this;
    }

    // ── SignalR ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Maps a SignalR hub at the given path (e.g., "/chathub").
    /// Registers negotiate + WebSocket routes and <see cref="IHubContext{THub}"/> in DI.
    /// Call <c>builder.AddSignalR()</c> before calling this.
    /// </summary>
    public CosmoWebApplication MapHub<THub>(string path)
        where THub : Hub
    {
        var manager = new HubConnectionManager();
        var dispatcher = new HubDispatcher<THub>(manager);

        // Register in the hub context registry so server-side code can push messages
        var registry = _services.GetService<HubContextRegistry>();
        registry?.Register<THub>(manager);

        // Negotiate endpoint (SignalR clients POST to /hub/negotiate)
        var negotiatePath = path.TrimEnd('/') + "/negotiate";
        _routeTable.Add(Http.HttpMethod.POST, negotiatePath, ctx => new ValueTask(dispatcher.HandleAsync(ctx)));
        _routeTable.Add(Http.HttpMethod.GET,  negotiatePath, ctx => new ValueTask(dispatcher.HandleAsync(ctx)));

        // WebSocket upgrade endpoint (GET on hub path itself)
        _routeTable.Add(Http.HttpMethod.GET, path, ctx => new ValueTask(dispatcher.HandleAsync(ctx)));

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
            _options.EnableHttp3,
            _options.ConnectionTimeoutSeconds,
            _options.Http3MaxRequestsPerConnection,
            _options.Http3MaxConcurrentStreams,
            _options.Http3IdleTimeoutSeconds,
            _options.Http3MaxUnidirectionalStreams,
            _options.Http3MaxFieldSectionSize,
            cancellationToken,
            _options.CertificateSelector,
            _options.HttpsPort);

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
