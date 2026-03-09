using System.ComponentModel.DataAnnotations;

namespace BlazorSqlSample.Models;

public class SqlQueryModel
{
    [Required(ErrorMessage = "The SQL query field is required.")]
    [MinLength(5, ErrorMessage = "SQL query must be at least 5 characters.")]
    public string Sql { get; set; } = "SELECT TOP 100 * FROM sys.objects";
    public List<string> Columns { get; set; } = new();
    public List<List<string?>> Rows { get; set; } = new();
    public string? Error { get; set; }
    public double ElapsedSeconds { get; set; }
}
