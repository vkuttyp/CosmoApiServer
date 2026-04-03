namespace CosmoApiServer.Core.Auth.Authorization;

public sealed class AuthorizationOptions
{
    private readonly Dictionary<string, AuthorizationPolicy> _policies = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Default policy applied when [Authorize] is used without a Policy name.</summary>
    public AuthorizationPolicy DefaultPolicy { get; set; } =
        new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

    public void AddPolicy(string name, Action<AuthorizationPolicyBuilder> configure)
    {
        var builder = new AuthorizationPolicyBuilder();
        configure(builder);
        _policies[name] = builder.Build();
    }

    public void AddPolicy(string name, AuthorizationPolicy policy) => _policies[name] = policy;

    public AuthorizationPolicy? GetPolicy(string name) =>
        _policies.TryGetValue(name, out var p) ? p : null;
}
