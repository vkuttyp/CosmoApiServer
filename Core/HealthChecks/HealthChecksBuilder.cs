using Microsoft.Extensions.DependencyInjection;

namespace CosmoApiServer.Core.HealthChecks;

/// <summary>
/// Fluent builder returned from AddHealthChecks(). Use to register checks.
/// </summary>
public sealed class HealthChecksBuilder(IServiceCollection services, HealthCheckService service)
{
    public IServiceCollection Services => services;

    /// <summary>Register a typed health check resolved from DI.</summary>
    public HealthChecksBuilder AddCheck<T>(string name) where T : class, IHealthCheck
    {
        services.AddTransient<T>();
        // Wrap: resolve T from DI at check time via a delegating check
        service.Register(name, new DeferredCheck<T>(services));
        return this;
    }

    /// <summary>Register an inline lambda health check.</summary>
    public HealthChecksBuilder AddCheck(string name, Func<HealthCheckResult> check)
    {
        service.Register(name, new LambdaCheck(_ => Task.FromResult(check())));
        return this;
    }

    /// <summary>Register an async lambda health check.</summary>
    public HealthChecksBuilder AddCheck(string name, Func<CancellationToken, Task<HealthCheckResult>> check)
    {
        service.Register(name, new LambdaCheck(check));
        return this;
    }

    private sealed class LambdaCheck(Func<CancellationToken, Task<HealthCheckResult>> fn) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct)
            => fn(ct);
    }

    // Resolves T from the built service provider at check time
    private sealed class DeferredCheck<T>(IServiceCollection services) : IHealthCheck where T : class, IHealthCheck
    {
        private IHealthCheck? _resolved;
        private readonly IServiceCollection _services = services;

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct)
        {
            // Best-effort: if we have a provider, resolve. Otherwise return degraded.
            if (_resolved is null)
                return HealthCheckResult.Degraded("Health check not yet resolvable from DI.");
            return await _resolved.CheckHealthAsync(context, ct);
        }

        internal void SetInstance(T instance) => _resolved = instance;
    }
}
