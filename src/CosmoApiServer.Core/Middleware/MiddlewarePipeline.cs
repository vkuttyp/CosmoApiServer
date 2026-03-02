using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Middleware;

public sealed class MiddlewarePipeline
{
    private readonly List<Func<RequestDelegate, RequestDelegate>> _components = [];

    public void Use(Func<RequestDelegate, RequestDelegate> middleware) =>
        _components.Add(middleware);

    public void UseMiddleware<T>(IServiceProvider services) where T : IMiddleware
    {
        _components.Add(next =>
            ctx =>
            {
                var instance = (T)services.GetService(typeof(T))!
                               ?? ActivatorUtilities_Create<T>(services);
                return instance.InvokeAsync(ctx, next);
            });
    }

    // Use a direct factory for middleware not registered in DI
    public void UseInstance(IMiddleware middleware) =>
        _components.Add(next => ctx => middleware.InvokeAsync(ctx, next));

    public RequestDelegate Build(RequestDelegate terminal)
    {
        var pipeline = terminal;
        for (int i = _components.Count - 1; i >= 0; i--)
            pipeline = _components[i](pipeline);
        return pipeline;
    }

    // Fallback: activate with constructor injection
    private static T ActivatorUtilities_Create<T>(IServiceProvider sp) =>
        (T)Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance(sp, typeof(T));
}
