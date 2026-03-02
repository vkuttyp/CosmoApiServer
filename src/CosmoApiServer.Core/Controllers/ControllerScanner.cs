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

    private static void RegisterController(Type controllerType, RouteTable routeTable, IServiceProvider services)
    {
        // Controller-level route prefix (optional)
        var routePrefix = controllerType.GetCustomAttribute<RouteAttribute>()?.Template ?? string.Empty;

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

            // Combine prefix + verb template
            var template = CombineTemplates(routePrefix, verbAttr.Template ?? string.Empty);

            var capturedMethod = method;
            RequestDelegate handler = async ctx =>
            {
                // Authorization check: [Authorize] on controller or method, unless [AllowAnonymous] on method
                bool requiresAuth = controllerType.GetCustomAttribute<AuthorizeAttribute>() is not null
                                    || capturedMethod.GetCustomAttribute<AuthorizeAttribute>() is not null;
                bool allowAnonymous = capturedMethod.GetCustomAttribute<AllowAnonymousAttribute>() is not null;

                if (requiresAuth && !allowAnonymous && ctx.User is null)
                {
                    ctx.Response.StatusCode = 401;
                    ctx.Response.WriteJson(new { error = "Unauthorized", message = "A valid Bearer token is required." });
                    return;
                }

                // Resolve controller from DI scope
                var controller = (ControllerBase)ActivatorUtilities.CreateInstance(ctx.RequestServices, controllerType);
                controller.HttpContext = ctx;

                // Bind parameters
                var parameters = BindParameters(capturedMethod, ctx);

                // Invoke action
                var result = capturedMethod.Invoke(controller, parameters);

                // Await if Task/Task<T>
                if (result is Task task)
                    await task;

                // Execute IActionResult if returned
                var returnedResult = result;
                if (result is Task<IActionResult> taskOfResult)
                    returnedResult = await taskOfResult;

                if (returnedResult is IActionResult actionResult)
                    await actionResult.ExecuteAsync(ctx.Response);
            };

            routeTable.Add(httpMethod, template, handler);
        }
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

    private static object?[] BindParameters(MethodInfo method, HttpContext ctx)
    {
        var parameters = method.GetParameters();
        var values = new object?[parameters.Length];

        for (int i = 0; i < parameters.Length; i++)
        {
            var param = parameters[i];
            var paramType = param.ParameterType;

            if (param.GetCustomAttribute<FromBodyAttribute>() is not null)
            {
                values[i] = ctx.Request.ReadJson(paramType);
            }
            else if (param.GetCustomAttribute<FromRouteAttribute>() is { } fromRoute)
            {
                var key = fromRoute.Name ?? param.Name!;
                values[i] = ctx.Request.RouteValues.TryGetValue(key, out var rv) ? Convert(rv, paramType) : null;
            }
            else if (param.GetCustomAttribute<FromQueryAttribute>() is { } fromQuery)
            {
                var key = fromQuery.Name ?? param.Name!;
                values[i] = ctx.Request.Query.TryGetValue(key, out var qv) ? Convert(qv, paramType) : null;
            }
            else if (paramType == typeof(HttpContext))
            {
                values[i] = ctx;
            }
            else if (ctx.Request.RouteValues.TryGetValue(param.Name!, out var routeVal))
            {
                // Implicit route binding by parameter name
                values[i] = Convert(routeVal, paramType);
            }
            else if (ctx.Request.Query.TryGetValue(param.Name!, out var queryVal))
            {
                // Implicit query binding by parameter name
                values[i] = Convert(queryVal, paramType);
            }
            else
            {
                // Try DI
                values[i] = ctx.RequestServices.GetService(paramType);
            }
        }

        return values;
    }

    private static object? Convert(string value, Type targetType)
    {
        if (targetType == typeof(string)) return value;
        if (targetType == typeof(int) || targetType == typeof(int?)) return int.Parse(value);
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
