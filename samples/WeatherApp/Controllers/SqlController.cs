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
    // ── GET /sql/query?sql=SELECT TOP 10 * FROM sys.objects ───────────────
    /// <summary>
    /// Executes any SELECT and returns results as a JSON array of objects.
    /// Mirrors Query.razor — uses QueryStreamAsync row-by-row.
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

    // ── GET /sql/json-stream?sql=SELECT TOP 200 * FROM AccountTrans ──────
    /// <summary>
    /// Executes a SELECT and serializes each row to a JSON object (client-side).
    /// Mirrors JsonStream.razor — plain SELECT, no FOR JSON needed.
    /// </summary>
    [HttpGet("json-stream")]
    public async Task<IActionResult> JsonStream([FromQuery] string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return BadRequest("Query parameter 'sql' is required.");

        await using var conn = await sqlService.OpenAsync();

        var rows = new List<Dictionary<string, object?>>();
        bool columnsWritten = false;
        string[]? columns = null;

        await foreach (var row in conn.QueryStreamAsync(sql))
        {
            if (!columnsWritten)
            {
                columns = row.Columns.Select(c => c.Name).ToArray();
                columnsWritten = true;
            }

            var obj = new Dictionary<string, object?>(row.ColumnCount);
            for (int i = 0; i < row.ColumnCount; i++)
                obj[columns![i]] = SqlValueToClr(row.Values[i]);
            rows.Add(obj);
        }

        return Ok(new { rowCount = rows.Count, rows });
    }

    // ── GET /sql/for-json?sql=SELECT TOP 200 * FROM Products FOR JSON PATH ─
    /// <summary>
    /// Executes a FOR JSON query and streams pre-serialized JSON objects.
    /// Mirrors JsonStreamForjson.razor — SQL must include FOR JSON PATH/AUTO.
    /// </summary>
    [HttpGet("for-json")]
    public async Task<IActionResult> ForJson([FromQuery] string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return BadRequest("Query parameter 'sql' is required. Include FOR JSON PATH or FOR JSON AUTO.");

        await using var conn = await sqlService.OpenAsync();

        var elements = new List<JsonElement>();
        await foreach (var element in conn.QueryJsonStreamAsync(sql))
            elements.Add(element.Clone());

        return Ok(new { rowCount = elements.Count, rows = elements });
    }

    // ── Helpers ────────────────────────────────────────────────────────────

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
