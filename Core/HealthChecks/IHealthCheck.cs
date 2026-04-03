namespace CosmoApiServer.Core.HealthChecks;

public interface IHealthCheck
{
    Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default);
}

public sealed class HealthCheckContext
{
    public string Name { get; init; } = string.Empty;
}

public enum HealthStatus { Healthy = 2, Degraded = 1, Unhealthy = 0 }

public sealed class HealthCheckResult
{
    public HealthStatus Status { get; init; }
    public string? Description { get; init; }
    public Exception? Exception { get; init; }
    public IReadOnlyDictionary<string, object>? Data { get; init; }

    public static HealthCheckResult Healthy(string? description = null, IReadOnlyDictionary<string, object>? data = null)
        => new() { Status = HealthStatus.Healthy, Description = description, Data = data };

    public static HealthCheckResult Degraded(string? description = null, Exception? exception = null, IReadOnlyDictionary<string, object>? data = null)
        => new() { Status = HealthStatus.Degraded, Description = description, Exception = exception, Data = data };

    public static HealthCheckResult Unhealthy(string? description = null, Exception? exception = null, IReadOnlyDictionary<string, object>? data = null)
        => new() { Status = HealthStatus.Unhealthy, Description = description, Exception = exception, Data = data };
}
