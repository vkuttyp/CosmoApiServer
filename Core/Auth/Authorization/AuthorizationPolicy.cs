using System.Security.Claims;

namespace CosmoApiServer.Core.Auth.Authorization;

public sealed class AuthorizationPolicy(IReadOnlyList<IAuthorizationRequirement> requirements)
{
    public IReadOnlyList<IAuthorizationRequirement> Requirements => requirements;
}

public sealed class AuthorizationPolicyBuilder
{
    private readonly List<IAuthorizationRequirement> _requirements = [];

    public AuthorizationPolicyBuilder RequireAuthenticatedUser()
    {
        _requirements.Add(new DenyAnonymousRequirement());
        return this;
    }

    public AuthorizationPolicyBuilder RequireRole(params string[] roles)
    {
        _requirements.Add(new RolesRequirement(roles));
        return this;
    }

    public AuthorizationPolicyBuilder RequireClaim(string claimType, params string[] allowedValues)
    {
        _requirements.Add(new ClaimsRequirement(claimType, allowedValues.Length > 0 ? allowedValues : null));
        return this;
    }

    public AuthorizationPolicyBuilder RequireAssertion(Func<ClaimsPrincipal, bool> predicate)
    {
        _requirements.Add(new CustomRequirement(predicate));
        return this;
    }

    public AuthorizationPolicyBuilder AddRequirements(params IAuthorizationRequirement[] requirements)
    {
        _requirements.AddRange(requirements);
        return this;
    }

    public AuthorizationPolicy Build() => new(_requirements.AsReadOnly());
}
