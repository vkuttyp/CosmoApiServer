using System.Diagnostics;
using CosmoApiServer.Core.Controllers;
using CosmoApiServer.Core.Controllers.Attributes;
using CosmoSQLClient.MsSql;
using WeatherApp.Models;
using WeatherApp.Views.Sql;

namespace WeatherApp.Controllers;

[Route("sql")]
public class SqlQueryController(MsSqlConnectionPool pool) : ControllerBase
{
    [HttpGet]
    public IActionResult Index()
    {
        return View(SqlQuery.Create(new SqlQueryModel()));
    }

    [HttpPost("run")]
    public async Task<IActionResult> Run([FromForm] string sql)
    {
        var model = new SqlQueryModel { Sql = sql };
        var sw = Stopwatch.StartNew();

        Console.WriteLine($"[SQL] Executing: {sql}");

        try
        {
            if (string.IsNullOrWhiteSpace(sql))
                throw new ArgumentException("SQL query cannot be empty.");

            int rowCount = 0;
            await foreach (var row in pool.QueryStreamAsync(sql))
            {
                if (model.Columns.Count == 0)
                {
                    foreach (var col in row.Columns)
                        model.Columns.Add(col.Name);
                    Console.WriteLine($"[SQL] Columns: {string.Join(", ", model.Columns)}");
                }

                var cells = new List<string?>(row.ColumnCount);
                for (int i = 0; i < row.ColumnCount; i++)
                {
                    cells.Add(row[i].IsNull ? null : row[i].ToString());
                }
                model.Rows.Add(cells);
                rowCount++;
            }
            Console.WriteLine($"[SQL] Success: {rowCount} rows returned.");
        }
        catch (Exception ex)
        {
            model.Error = ex.Message;
            Console.WriteLine($"[SQL] ERROR: {ex.Message}");
            if (ex.InnerException != null)
                Console.WriteLine($"[SQL] INNER ERROR: {ex.InnerException.Message}");
        }
        finally
        {
            sw.Stop();
            model.ElapsedSeconds = sw.Elapsed.TotalSeconds;
            Console.WriteLine($"[SQL] Elapsed: {model.ElapsedSeconds}s");
        }

        // Return the partial view for HTMX
        return View(SqlQueryResults.Create(model));
    }
}
