namespace CosmoApiServer.Core.HealthChecks;

/// <summary>
/// Runs all registered health checks and aggregates results.
/// Registered as a singleton via AddHealthChecks().
/// </summary>
public sealed class HealthCheckService
{
    private readonly List<(string Name, IHealthCheck Check)> _checks = [];

    internal void Register(string name, IHealthCheck check) => _checks.Add((name, check));

    public async Task<HealthReport> RunAsync(CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;
        var entries = new Dictionary<string, HealthReportEntry>(_checks.Count);

        await Parallel.ForEachAsync(_checks, ct, async (item, token) =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            HealthCheckResult result;
            try
            {
                result = await item.Check.CheckHealthAsync(new HealthCheckContext { Name = item.Name }, token);
            }
            catch (Exception ex)
            {
                result = HealthCheckResult.Unhealthy("Health check threw an exception.", ex);
            }
            sw.Stop();

            lock (entries)
            {
                entries[item.Name] = new HealthReportEntry(result, sw.Elapsed);
            }
        });

        var worst = entries.Values.Count == 0
            ? HealthStatus.Healthy
            : entries.Values.Min(e => e.Status);

        return new HealthReport(worst, DateTime.UtcNow - started, entries);
    }
}

public sealed class HealthReportEntry(HealthCheckResult result, TimeSpan duration)
{
    public HealthStatus Status => result.Status;
    public string? Description => result.Description;
    public TimeSpan Duration => duration;
    public Exception? Exception => result.Exception;
    public IReadOnlyDictionary<string, object>? Data => result.Data;
}

public sealed class HealthReport(HealthStatus status, TimeSpan totalDuration, IReadOnlyDictionary<string, HealthReportEntry> entries)
{
    public HealthStatus Status => status;
    public TimeSpan TotalDuration => totalDuration;
    public IReadOnlyDictionary<string, HealthReportEntry> Entries => entries;
}
