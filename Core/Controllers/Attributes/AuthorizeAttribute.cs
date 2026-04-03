namespace CosmoApiServer.Core.Controllers.Attributes;

/// <summary>
/// Marks a controller or action as requiring authentication.
/// Optionally enforces a named authorization policy or role(s).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class AuthorizeAttribute : Attribute
{
    public AuthorizeAttribute() { }
    public AuthorizeAttribute(string policy) => Policy = policy;

    /// <summary>Named policy to evaluate via IAuthorizationService.</summary>
    public string? Policy { get; set; }

    /// <summary>Comma-separated roles — shorthand for a roles-based policy.</summary>
    public string? Roles { get; set; }
}

/// <summary>
/// Overrides [Authorize] on a controller — allows anonymous access to a specific action.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AllowAnonymousAttribute : Attribute;
