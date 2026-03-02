namespace CosmoApiServer.Core.Controllers.Attributes;

/// <summary>
/// Marks a controller or action as requiring JWT authentication.
/// Can be applied at class level (protects all actions) or method level.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AuthorizeAttribute : Attribute;

/// <summary>
/// Overrides [Authorize] on a controller — allows anonymous access to a specific action.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AllowAnonymousAttribute : Attribute;
