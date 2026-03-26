using System.Reflection;
using CosmoApiServer.Core.Controllers.Attributes;

namespace CosmoApiServer.Core.Controllers;

public sealed class OpenApiInfo
{
    public string Title { get; set; } = "Cosmo API";
    public string Version { get; set; } = "1.0.0";
    public string Description { get; set; } = "A high-performance Cosmo API";
}

/// <summary>
/// Generates a minimal OpenAPI 3.0.0 specification from registered controllers.
/// </summary>
public static class OpenApiGenerator
{
    public static Dictionary<string, object> Generate(IEnumerable<Type> controllerTypes, OpenApiInfo info)
    {
        var paths = new Dictionary<string, object>();
        var schemas = new Dictionary<string, object>();

        foreach (var type in controllerTypes)
        {
            var controllerName = type.Name.Replace("Controller", "");
            var routePrefix = type.GetCustomAttribute<RouteAttribute>()?.Template ?? string.Empty;
            routePrefix = routePrefix.Replace("[controller]", controllerName, StringComparison.OrdinalIgnoreCase);
            
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                var verbAttr = method.GetCustomAttribute<HttpMethodAttribute>();
                if (verbAttr is null) continue;

                var verb = GetHttpVerb(verbAttr);
                var actionName = method.Name;
                var actionTemplate = verbAttr.Template ?? string.Empty;

                var template = CombineTemplates(routePrefix, actionTemplate);
                template = template.Replace("[action]", actionName, StringComparison.OrdinalIgnoreCase)
                                   .Replace("[controller]", controllerName, StringComparison.OrdinalIgnoreCase);
                
                if (!paths.ContainsKey(template)) paths[template] = new Dictionary<string, object>();
                var pathItem = (Dictionary<string, object>)paths[template];

                var operation = new Dictionary<string, object>
                {
                    ["tags"] = new[] { controllerName },
                    ["summary"] = method.Name,
                    ["responses"] = new Dictionary<string, object>
                    {
                        ["200"] = new Dictionary<string, object> { ["description"] = "Success" }
                    }
                };

                // Parameters
                var parameters = new List<object>();
                foreach (var param in method.GetParameters())
                {
                    var paramIn = GetBindingSource(param, template);
                    if (paramIn == "body")
                    {
                        operation["requestBody"] = new Dictionary<string, object>
                        {
                            ["content"] = new Dictionary<string, object>
                            {
                                ["application/json"] = new Dictionary<string, object> { ["schema"] = MapType(param.ParameterType, schemas) }
                            }
                        };
                    }
                    else if (paramIn != null)
                    {
                        parameters.Add(new Dictionary<string, object>
                        {
                            ["name"] = param.Name!,
                            ["in"] = paramIn,
                            ["required"] = paramIn == "path" || !IsNullable(param.ParameterType),
                            ["schema"] = MapType(param.ParameterType, schemas)
                        });
                    }
                }

                if (parameters.Count > 0) operation["parameters"] = parameters;

                pathItem[verb] = operation;
            }
        }

        var result = new Dictionary<string, object>
        {
            ["openapi"] = "3.0.0",
            ["info"] = info,
            ["paths"] = paths
        };

        if (schemas.Count > 0)
        {
            result["components"] = new Dictionary<string, object> { ["schemas"] = schemas };
        }

        return result;
    }

    private static string GetHttpVerb(HttpMethodAttribute attr) => attr switch
    {
        HttpGetAttribute    => "get",
        HttpPostAttribute   => "post",
        HttpPutAttribute    => "put",
        HttpDeleteAttribute => "delete",
        HttpPatchAttribute  => "patch",
        _                   => "get"
    };

    private static string? GetBindingSource(ParameterInfo param, string template)
    {
        if (param.GetCustomAttribute<FromBodyAttribute>() != null) return "body";
        if (param.GetCustomAttribute<FromQueryAttribute>() != null) return "query";
        if (param.GetCustomAttribute<FromRouteAttribute>() != null) return "path";
        if (param.GetCustomAttribute<FromHeaderAttribute>() != null) return "header";
        
        // If the parameter name is in the template, it's a path parameter
        if (template.Contains($"{{{param.Name}}}", StringComparison.OrdinalIgnoreCase)) return "path";

        // Simple heuristic for implicit binding
        if (param.ParameterType.IsPrimitive || param.ParameterType == typeof(string)) return "query";
        return null;
    }

    private static Dictionary<string, object> MapType(Type type, Dictionary<string, object> schemas)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying == typeof(int) || underlying == typeof(long)) return new() { ["type"] = "integer" };
        if (underlying == typeof(bool)) return new() { ["type"] = "boolean" };
        if (underlying == typeof(double) || underlying == typeof(float) || underlying == typeof(decimal)) return new() { ["type"] = "number" };
        if (underlying == typeof(string)) return new() { ["type"] = "string" };

        // Complex type -> Schema + Ref
        var typeName = underlying.Name;
        if (!schemas.ContainsKey(typeName))
        {
            var properties = new Dictionary<string, object>();
            schemas[typeName] = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = properties
            };

            foreach (var prop in underlying.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                properties[prop.Name] = MapType(prop.PropertyType, schemas);
            }
        }

        return new() { ["$ref"] = $"#/components/schemas/{typeName}" };
    }

    private static bool IsNullable(Type type) => !type.IsValueType || Nullable.GetUnderlyingType(type) != null;

    private static string CombineTemplates(string prefix, string template)
    {
        prefix = prefix.Trim('/');
        template = template.Trim('/');
        return prefix.Length > 0 && template.Length > 0 ? $"/{prefix}/{template}" : prefix.Length > 0 ? $"/{prefix}" : $"/{template}";
    }
}
