using System.Text.Json;
using CosmoSQLClient.Core;
using CosmoApiServer.Core.Controllers;
using CosmoApiServer.Core.Controllers.Attributes;
using WeatherApp.Services;

namespace WeatherApp.Controllers;

[Route("sql")]
[Authorize]
public class SqlController(SqlService sqlService) : ControllerBase
{
    // -- GET /sql/query?sql=SELECT TOP 10 * FROM sys.objects --
    /// <summary>
    /// Executes any SELECT and returns all rows buffered as a JSON array.
    /// Suitable for small result sets that need a row count.
    /// </summary>
    [HttpGet("query")]
    public async Task<IActionResult> Query([FromQuery] string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return BadRequest("Query parameter 'sql' is required.");

        await using var conn = await sqlService.OpenAsync();

        var rows = new List<Dictionary<string, object?>>();
        await foreach (var row in conn.QueryStreamAsync(sql))
        {
            var obj = new Dictionary<string, object?>(row.ColumnCount);
            for (int i = 0; i < row.ColumnCount; i++)
                obj[row.Columns[i].Name] = SqlValueToClr(row.Values[i]);
            rows.Add(obj);
        }

        return Ok(new { rowCount = rows.Count, rows });
    }

    // -- GET /sql/stream?sql=SELECT * FROM AccountTrans --
    /// <summary>
    /// Streams each row as a JSON chunk the instant it arrives from SQL Server.
    /// HTTP chunked transfer encoding -- zero buffering. Ideal for large result sets.
    /// </summary>
    [HttpGet("stream")]
    public IAsyncEnumerable<Dictionary<string, object?>> Stream([FromQuery] string sql)
        => StreamRowsAsync(sql);

    private async IAsyncEnumerable<Dictionary<string, object?>> StreamRowsAsync(string sql)
    {
        await using var conn = await sqlService.OpenAsync();
        await foreach (var row in conn.QueryStreamAsync(sql))
        {
            var obj = new Dictionary<string, object?>(row.ColumnCount);
            for (int i = 0; i < row.ColumnCount; i++)
                obj[row.Columns[i].Name] = SqlValueToClr(row.Values[i]);
            yield return obj;
        }
    }

    // -- GET /sql/for-json?sql=SELECT * FROM Products FOR JSON PATH --
    /// <summary>
    /// Executes a FOR JSON query and streams pre-serialized JSON objects as they arrive.
    /// HTTP chunked transfer encoding. SQL must include FOR JSON PATH or FOR JSON AUTO.
    /// </summary>
    [HttpGet("for-json")]
    public IAsyncEnumerable<JsonElement> ForJson([FromQuery] string sql)
        => ForJsonStreamAsync(sql);

    private async IAsyncEnumerable<JsonElement> ForJsonStreamAsync(string sql)
    {
        await using var conn = await sqlService.OpenAsync();
        await foreach (var element in conn.QueryJsonStreamAsync(sql))
            yield return element;
    }

    // -- Helpers --

    private static object? SqlValueToClr(SqlValue v) => v switch
    {
        SqlValue.Null    => null,
        SqlValue.Bool b  => b.Value,
        SqlValue.Int8 i  => i.Value,
        SqlValue.Int16 i => i.Value,
        SqlValue.Int32 i => i.Value,
        SqlValue.Int64 i => i.Value,
        SqlValue.Float f => f.Value,
        SqlValue.Double d => d.Value,
        SqlValue.Decimal d => d.Value,
        SqlValue.Text t  => t.Value,
        SqlValue.Uuid u  => u.Value,
        SqlValue.Date d  => d.Value,
        SqlValue.Bytes b => Convert.ToBase64String(b.Value),
        _                => v.ToString()
    };
}
