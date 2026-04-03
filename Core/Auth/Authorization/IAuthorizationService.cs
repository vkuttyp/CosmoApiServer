using System.Security.Claims;

namespace CosmoApiServer.Core.Auth.Authorization;

public interface IAuthorizationService
{
    Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal? user, string policyName);
    Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal? user, AuthorizationPolicy policy);
}

public sealed class AuthorizationResult
{
    public bool Succeeded { get; private init; }
    public string? FailureReason { get; private init; }

    public static AuthorizationResult Success() => new() { Succeeded = true };
    public static AuthorizationResult Fail(string? reason = null) => new() { Succeeded = false, FailureReason = reason };
}

public sealed class DefaultAuthorizationService(AuthorizationOptions options) : IAuthorizationService
{
    public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal? user, string policyName)
    {
        var policy = options.GetPolicy(policyName)
            ?? throw new InvalidOperationException($"Authorization policy '{policyName}' not found.");
        return AuthorizeAsync(user, policy);
    }

    public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal? user, AuthorizationPolicy policy)
    {
        foreach (var requirement in policy.Requirements)
        {
            var result = Evaluate(user, requirement);
            if (!result.Succeeded) return Task.FromResult(result);
        }
        return Task.FromResult(AuthorizationResult.Success());
    }

    private static AuthorizationResult Evaluate(ClaimsPrincipal? user, IAuthorizationRequirement requirement)
    {
        return requirement switch
        {
            DenyAnonymousRequirement => user?.Identity?.IsAuthenticated == true
                ? AuthorizationResult.Success()
                : AuthorizationResult.Fail("User is not authenticated."),

            RolesRequirement r => r.Roles.Any(role => user?.IsInRole(role) == true)
                ? AuthorizationResult.Success()
                : AuthorizationResult.Fail($"User does not have a required role ({string.Join(", ", r.Roles)})."),

            ClaimsRequirement c => user?.Claims.Any(claim =>
                    claim.Type.Equals(c.ClaimType, StringComparison.OrdinalIgnoreCase) &&
                    (c.AllowedValues is null || c.AllowedValues.Contains(claim.Value))) == true
                ? AuthorizationResult.Success()
                : AuthorizationResult.Fail($"User does not have required claim '{c.ClaimType}'."),

            CustomRequirement custom => custom.IsSatisfied(user ?? new ClaimsPrincipal())
                ? AuthorizationResult.Success()
                : AuthorizationResult.Fail("Custom requirement not satisfied."),

            _ => AuthorizationResult.Fail($"Unknown requirement type '{requirement.GetType().Name}'.")
        };
    }
}
