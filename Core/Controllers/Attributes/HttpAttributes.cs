namespace CosmoApiServer.Core.Controllers.Attributes;

/// <summary>Base for all HTTP verb attributes.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public abstract class HttpMethodAttribute : Attribute
{
    public string? Template { get; }
    protected HttpMethodAttribute(string? template) => Template = template;
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class HttpGetAttribute(string? template = null) : HttpMethodAttribute(template);

[AttributeUsage(AttributeTargets.Method)]
public sealed class HttpPostAttribute(string? template = null) : HttpMethodAttribute(template);

[AttributeUsage(AttributeTargets.Method)]
public sealed class HttpPutAttribute(string? template = null) : HttpMethodAttribute(template);

[AttributeUsage(AttributeTargets.Method)]
public sealed class HttpDeleteAttribute(string? template = null) : HttpMethodAttribute(template);

[AttributeUsage(AttributeTargets.Method)]
public sealed class HttpPatchAttribute(string? template = null) : HttpMethodAttribute(template);

/// <summary>Route prefix on a controller class.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RouteAttribute(string template) : Attribute
{
    public string Template { get; } = template;
}

/// <summary>Bind parameter from the JSON request body.</summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class FromBodyAttribute : Attribute;

/// <summary>Bind parameter from the query string.</summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class FromQueryAttribute(string? name = null) : Attribute
{
    public string? Name { get; } = name;
}

/// <summary>Bind parameter from the route template.</summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class FromRouteAttribute(string? name = null) : Attribute
{
    public string? Name { get; } = name;
}

/// <summary>Bind parameter from an HTTP header.</summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class FromHeaderAttribute(string? name = null) : Attribute
{
    public string? Name { get; } = name;
}

/// <summary>Bind parameter from a multipart form field.</summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class FromFormAttribute(string? name = null) : Attribute
{
    public string? Name { get; } = name;
}

/// <summary>Bind parameter from the dependency injection container.</summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class FromServicesAttribute : Attribute;
