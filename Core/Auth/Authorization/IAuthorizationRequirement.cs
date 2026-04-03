namespace CosmoApiServer.Core.Auth.Authorization;

public interface IAuthorizationRequirement { }

// ── Built-in requirements ────────────────────────────────────────────────────

public sealed class DenyAnonymousRequirement : IAuthorizationRequirement { }

public sealed class RolesRequirement(string[] roles) : IAuthorizationRequirement
{
    public string[] Roles => roles;
}

public sealed class ClaimsRequirement(string claimType, string[]? allowedValues) : IAuthorizationRequirement
{
    public string ClaimType => claimType;
    public string[]? AllowedValues => allowedValues;
}

public sealed class CustomRequirement(Func<System.Security.Claims.ClaimsPrincipal, bool> predicate) : IAuthorizationRequirement
{
    public bool IsSatisfied(System.Security.Claims.ClaimsPrincipal user) => predicate(user);
}
