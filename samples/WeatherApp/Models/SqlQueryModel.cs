namespace WeatherApp.Models;

public class SqlQueryModel
{
    public string Sql { get; set; } = "SELECT TOP 100 * FROM sys.objects";
    public string? Error { get; set; }
    public List<string> Columns { get; set; } = [];
    public List<List<string?>> Rows { get; set; } = [];
    public double ElapsedSeconds { get; set; }
}
