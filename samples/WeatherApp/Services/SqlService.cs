using CosmoSQLClient.Core;
using CosmoSQLClient.MsSql;

namespace WeatherApp.Services;

public sealed class SqlService(string connectionString) : IAsyncDisposable
{
    public string ConnectionString { get; } = connectionString;

    /// <summary>Opens a fresh connection — caller must dispose.</summary>
    public Task<MsSqlConnection> OpenAsync(CancellationToken ct = default) =>
        MsSqlConnection.OpenAsync(ConnectionString, ct);

    public async ValueTask DisposeAsync() { }
}
