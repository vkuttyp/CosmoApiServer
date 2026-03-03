using Microsoft.Data.SqlClient;
using System.Data;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CosmoS3;

/// <summary>
/// Thin wrapper around Microsoft.Data.SqlClient that provides the same call pattern
/// as DataAccess.cs expects, while using ADO.NET connection pooling for performance.
/// Each CmdProc call draws a connection from the pool (~0.1ms) instead of opening
/// a fresh TCP connection (~10ms) as the prior CosmoSQLClient approach did.
/// </summary>
public static class MyCommand
{
    static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Creates a stored-procedure SqlCommand with a pooled connection.
    /// Caller owns the command (and thus the connection) via <c>using</c>.
    /// </summary>
    public static SqlCommand CmdProc(string proc, string conString)
    {
        var con = new SqlConnection(conString);   // drawn from ADO.NET pool
        return new SqlCommand
        {
            CommandType = CommandType.StoredProcedure,
            CommandText = proc,
            Connection  = con,
        };
    }

    /// <summary>
    /// Reads all rows of the first result set and concatenates column 0 —
    /// used to reassemble JSON produced by SQL Server's FOR JSON PATH.
    /// </summary>
    public static string GetJson(SqlDataReader reader)
    {
        var sb = new StringBuilder();
        while (reader.Read())
            sb.Append(reader.GetValue(0));
        return sb.ToString();
    }

    public static async Task<string> GetJsonAsync(SqlDataReader reader)
    {
        var sb = new StringBuilder();
        while (await reader.ReadAsync())
            sb.Append(reader.GetValue(0));
        return sb.ToString();
    }

    public static string ToJson(object source) =>
        JsonSerializer.Serialize(source, _jsonOptions);

    public static string DataTableJson(DataTable dataTable)
    {
        if (dataTable == null || dataTable.Rows.Count == 0)
            return string.Empty;

        var sb = new StringBuilder("[");
        for (int i = 0; i < dataTable.Rows.Count; i++)
        {
            sb.Append('{');
            for (int j = 0; j < dataTable.Columns.Count; j++)
                sb.AppendFormat("\"{0}\":\"{1}\"{2}",
                    dataTable.Columns[j].ColumnName,
                    dataTable.Rows[i][j],
                    j < dataTable.Columns.Count - 1 ? "," : string.Empty);
            sb.Append(i == dataTable.Rows.Count - 1 ? "}" : "},");
        }
        return sb.Append(']').ToString();
    }
}
