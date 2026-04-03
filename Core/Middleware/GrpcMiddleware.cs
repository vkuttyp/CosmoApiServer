using CosmoApiServer.Core.Grpc;
using CosmoApiServer.Core.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CosmoApiServer.Core.Middleware;

/// <summary>
/// Routes incoming gRPC requests (Content-Type: application/grpc) to registered service handlers.
/// Must be placed after any authentication middleware.
/// </summary>
public sealed class GrpcMiddleware(GrpcRouteRegistry registry) : IMiddleware
{
    public async ValueTask InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (!IsGrpcRequest(context.Request))
        {
            await next(context);
            return;
        }

        var handler = registry.Find(context.Request.Path);
        if (handler is null)
        {
            context.Response.StatusCode = 404;
            GrpcFraming.WriteTrailers(context.Response, GrpcStatusCode.Unimplemented,
                $"No gRPC method registered for {context.Request.Path}");
            return;
        }

        try
        {
            var service = (GrpcServiceBase)ActivatorUtilities.CreateInstance(context.RequestServices, handler.ServiceType);
            service.HttpContext = context;
            await handler.Invoke(service, context, context.RequestAborted);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[gRPC] {context.Request.Path}: {ex}");
            GrpcFraming.WriteTrailers(context.Response, GrpcStatusCode.Internal, "Internal server error");
        }
    }

    private static bool IsGrpcRequest(HttpRequest request) =>
        request.Headers.TryGetValue("Content-Type", out var ct) &&
        ct.StartsWith("application/grpc", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Registry of gRPC service handlers keyed by path (/ServiceName/MethodName).</summary>
public sealed class GrpcRouteRegistry
{
    private readonly Dictionary<string, GrpcHandlerEntry> _routes = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string path, Type serviceType,
        Func<GrpcServiceBase, HttpContext, CancellationToken, Task> invoke)
        => _routes[path] = new GrpcHandlerEntry(serviceType, invoke);

    public GrpcHandlerEntry? Find(string path) =>
        _routes.TryGetValue(path, out var e) ? e : null;

    public sealed class GrpcHandlerEntry(Type serviceType,
        Func<GrpcServiceBase, HttpContext, CancellationToken, Task> invoke)
    {
        public Type ServiceType => serviceType;
        public Func<GrpcServiceBase, HttpContext, CancellationToken, Task> Invoke => invoke;
    }
}
