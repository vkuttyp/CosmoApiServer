using System.Linq.Expressions;
using System.Reflection;
using CosmoApiServer.Core.Controllers.Attributes;
using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;
using CosmoApiServer.Core.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CosmoApiServer.Core.Controllers;

public static class ControllerScanner
{
    /// <summary>
    /// Scans all types in <paramref name="assemblies"/> that inherit <see cref="ControllerBase"/>,
    /// and registers their action methods into the <see cref="RouteTable"/>.
    /// </summary>
    public static void RegisterControllers(
        IEnumerable<Assembly> assemblies,
        RouteTable routeTable,
        IServiceProvider services)
    {
        foreach (var assembly in assemblies)
        {
            var controllerTypes = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(ControllerBase)));

            foreach (var controllerType in controllerTypes)
                RegisterController(controllerType, routeTable, services);
        }
    }

    // ── Per-action precomputed descriptor (built once at startup) ────────────

    private sealed class ActionDescriptor
    {
        /// <summary>Compiled delegate: avoids MethodInfo.Invoke per request.</summary>
        public required Func<object, object?[], object?> Invoker { get; init; }

        /// <summary>Per-parameter resolvers built from binding attributes — no reflection at request time.</summary>
        public required Func<HttpContext, object?>[] Resolvers { get; init; }

        /// <summary>Compiled Task&lt;T&gt;.Result extractor, or null for non-generic Task / sync methods.</summary>
        public required Func<Task, object?>? TaskResultExtractor { get; init; }

        /// <summary>Precomputed auth requirements.</summary>
        public required bool RequiresAuth { get; init; }
        public required bool AllowAnonymous { get; init; }
    }

    // ── Registration ─────────────────────────────────────────────────────────

    private static void RegisterController(Type controllerType, RouteTable routeTable, IServiceProvider services)
    {
        var routePrefix = controllerType.GetCustomAttribute<RouteAttribute>()?.Template ?? string.Empty;
        bool controllerRequiresAuth = controllerType.GetCustomAttribute<AuthorizeAttribute>() is not null;

        foreach (var method in controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            var verbAttr = method.GetCustomAttribute<HttpMethodAttribute>();
            if (verbAttr is null) continue;

            var httpMethod = verbAttr switch
            {
                HttpGetAttribute    => Http.HttpMethod.GET,
                HttpPostAttribute   => Http.HttpMethod.POST,
                HttpPutAttribute    => Http.HttpMethod.PUT,
                HttpDeleteAttribute => Http.HttpMethod.DELETE,
                HttpPatchAttribute  => Http.HttpMethod.PATCH,
                _                   => Http.HttpMethod.GET
            };

            var template = CombineTemplates(routePrefix, verbAttr.Template ?? string.Empty);

            // ── Build descriptor ONCE at startup (all reflection happens here) ──
            var desc = new ActionDescriptor
            {
                Invoker             = CompileInvoker(method),
                Resolvers           = method.GetParameters().Select(BuildResolver).ToArray(),
                TaskResultExtractor = BuildTaskResultExtractor(method.ReturnType),
                RequiresAuth        = controllerRequiresAuth || method.GetCustomAttribute<AuthorizeAttribute>() is not null,
                AllowAnonymous      = method.GetCustomAttribute<AllowAnonymousAttribute>() is not null,
            };

            RequestDelegate handler = async ctx =>
            {
                // Auth check — no reflection, flags precomputed
                if (desc.RequiresAuth && !desc.AllowAnonymous && ctx.User is null)
                {
                    ctx.Response.StatusCode = 401;
                    ctx.Response.WriteJson(new { error = "Unauthorized", message = "A valid Bearer token is required." });
                    return;
                }

                // Resolve controller from DI scope
                var controller = (ControllerBase)ActivatorUtilities.CreateInstance(ctx.RequestServices, controllerType);
                controller.HttpContext = ctx;

                // Bind parameters — precomputed resolvers, no attribute reflection
                var args = new object?[desc.Resolvers.Length];
                for (int i = 0; i < desc.Resolvers.Length; i++)
                    args[i] = desc.Resolvers[i](ctx);

                // Invoke action — compiled delegate, no MethodInfo.Invoke
                var result = desc.Invoker(controller, args);

                // Unwrap Task / Task<T> — compiled extractor, no GetProperty reflection
                object? returnedResult = result;
                if (result is Task task)
                {
                    await task;
                    returnedResult = desc.TaskResultExtractor?.Invoke(task);
                }

                // IAsyncEnumerable<T> → streaming response via transport-agnostic Stream
                var streamWriter = Transport.StreamingBodyWriter.TryCreate(
                    returnedResult, ctx.Response.StatusCode);
                if (streamWriter is not null)
                {
                    ctx.StreamingBodyWriter = streamWriter;
                    return;
                }

                // IActionResult → buffered response
                if (returnedResult is IActionResult actionResult)
                    await actionResult.ExecuteAsync(ctx.Response);
            };

            routeTable.Add(httpMethod, template, handler);
        }
    }

    // ── Startup-time compilation helpers ─────────────────────────────────────

    /// <summary>
    /// Compiles a strongly-typed delegate for the action method.
    /// ~50x faster than MethodInfo.Invoke at request time.
    /// </summary>
    private static Func<object, object?[], object?> CompileInvoker(MethodInfo method)
    {
        var instanceParam = Expression.Parameter(typeof(object), "instance");
        var argsParam     = Expression.Parameter(typeof(object?[]), "args");

        var argExprs = method.GetParameters().Select((p, i) =>
            (Expression)Expression.Convert(
                Expression.ArrayIndex(argsParam, Expression.Constant(i)),
                p.ParameterType)).ToArray();

        var call = Expression.Call(
            Expression.Convert(instanceParam, method.DeclaringType!),
            method,
            argExprs);

        // Void methods need a null return so the delegate signature is uniform
        Expression body = method.ReturnType == typeof(void)
            ? Expression.Block(call, Expression.Constant(null, typeof(object)))
            : Expression.Convert(call, typeof(object));

        return Expression.Lambda<Func<object, object?[], object?>>(body, instanceParam, argsParam).Compile();
    }

    /// <summary>
    /// Compiles a Task&lt;T&gt;.Result extractor so we never call GetProperty/GetValue per request.
    /// Returns null for non-generic Task or synchronous return types.
    /// </summary>
    private static Func<Task, object?>? BuildTaskResultExtractor(Type returnType)
    {
        if (!returnType.IsGenericType) return null;
        if (returnType.GetGenericTypeDefinition() != typeof(Task<>)) return null;

        var taskParam  = Expression.Parameter(typeof(Task), "task");
        var resultProp = returnType.GetProperty("Result")!;
        var access     = Expression.Property(Expression.Convert(taskParam, returnType), resultProp);

        return Expression.Lambda<Func<Task, object?>>(
            Expression.Convert(access, typeof(object)), taskParam).Compile();
    }

    /// <summary>
    /// Reads binding attributes once at startup and returns a resolver lambda
    /// that performs zero reflection at request time.
    /// </summary>
    private static Func<HttpContext, object?> BuildResolver(ParameterInfo param)
    {
        var type = param.ParameterType;
        var name = param.Name!;

        // [FromBody]
        if (param.GetCustomAttribute<FromBodyAttribute>() is not null)
            return ctx => ctx.Request.ReadJson(type);

        // [FromRoute]
        if (param.GetCustomAttribute<FromRouteAttribute>() is { } fromRoute)
        {
            var key = fromRoute.Name ?? name;
            return ctx => ctx.Request.RouteValues.TryGetValue(key, out var v) ? Convert(v, type) : null;
        }

        // [FromQuery]
        if (param.GetCustomAttribute<FromQueryAttribute>() is { } fromQuery)
        {
            var key = fromQuery.Name ?? name;
            return ctx => ctx.Request.Query.TryGetValue(key, out var v) ? Convert(v, type) : null;
        }

        // HttpContext injection
        if (type == typeof(HttpContext))
            return ctx => ctx;

        // Implicit: route by name → query by name → DI (runtime dictionary lookup only, no reflection)
        return ctx =>
        {
            if (ctx.Request.RouteValues.TryGetValue(name, out var rv)) return Convert(rv, type);
            if (ctx.Request.Query.TryGetValue(name, out var qv))       return Convert(qv, type);
            return ctx.RequestServices.GetService(type);
        };
    }

    private static string CombineTemplates(string prefix, string template)
    {
        prefix = prefix.Trim('/');
        template = template.Trim('/');
        return prefix.Length > 0 && template.Length > 0
            ? $"/{prefix}/{template}"
            : prefix.Length > 0
                ? $"/{prefix}"
                : $"/{template}";
    }

    private static object? Convert(string value, Type targetType)
    {
        if (targetType == typeof(string)) return value;
        if (targetType == typeof(int)  || targetType == typeof(int?))  return int.Parse(value);
        if (targetType == typeof(long) || targetType == typeof(long?)) return long.Parse(value);
        if (targetType == typeof(bool) || targetType == typeof(bool?)) return bool.Parse(value);
        if (targetType == typeof(Guid) || targetType == typeof(Guid?)) return Guid.Parse(value);
        return System.Convert.ChangeType(value, targetType);
    }
}

// Extension on HttpRequest for deserializing to a runtime type
internal static class HttpRequestExtensions
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static object? ReadJson(this HttpRequest req, Type type) =>
        req.Body.Length > 0
            ? System.Text.Json.JsonSerializer.Deserialize(req.Body, type, JsonOptions)
            : null;
}
