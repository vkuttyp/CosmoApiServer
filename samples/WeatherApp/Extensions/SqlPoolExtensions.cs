using System.Runtime.CompilerServices;
using System.Text.Json;
using CosmoSQLClient.Core;
using CosmoSQLClient.MsSql;

namespace WeatherApp.Extensions;

/// <summary>
/// Adds QueryJsonStreamAsync<T> to MsSqlConnectionPool — streams FOR JSON PATH results
/// as typed objects without buffering the full result set.
/// </summary>
public static class SqlPoolExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async IAsyncEnumerable<T> QueryJsonStreamAsync<T>(
        this MsSqlConnectionPool pool,
        string sql,
        IReadOnlyList<SqlParameter>? parameters = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var element in pool.QueryJsonStreamAsync(sql, parameters, 0, ct))
            yield return element.Deserialize<T>(JsonOptions)!;
    }

    public static IAsyncEnumerable<T> QueryJsonStreamAsync<T>(
        this MsSqlConnectionPool pool,
        string sql,
        params SqlParameter[] parameters)
        => pool.QueryJsonStreamAsync<T>(sql, parameters);
}
