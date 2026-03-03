using CosmoSQLClient.Core;
using CosmoSQLClient.MsSql;
using System.Data;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CosmoS3;

/// <summary>
/// Adapter over CosmoSQLClient.MsSql that preserves the synchronous call pattern
/// used throughout DataAccess.cs.
/// <para>
/// <b>Key design note:</b> all stored procedures are called via
/// <see cref="MsSqlCommand.ExecuteProcAsync"/> (TDS RPC), which correctly passes
/// parameters. <see cref="MsSqlCommand.ExecuteReaderAsync"/> runs raw SQL text and
/// cannot forward named parameters to a stored procedure.
/// </para>
/// </summary>
public static class MyCommand
{
    static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Opens a connection and creates a stored-procedure command.</summary>
    public static S3SqlCommand CmdProc(string proc, string conString)
    {
        var con = MsSqlConnection.OpenAsync(conString, CancellationToken.None).GetAwaiter().GetResult();
        var cmd = con.CreateCommand(proc);          // sets CommandText = proc
        return new S3SqlCommand(con, cmd);
    }

    /// <summary>
    /// Reads all rows of the first result set and concatenates the first column —
    /// used to reassemble JSON produced by SQL Server's FOR JSON PATH.
    /// </summary>
    public static string GetJson(SqlProcResult result)
    {
        if (result.Rows.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        foreach (var row in result.Rows)
            sb.Append(row[0].AsString() ?? string.Empty);
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

/// <summary>
/// Drop-in replacement for <c>SqlCommand</c> backed by <see cref="MsSqlCommand"/>.
/// <para>
/// <b>ExecuteReader returns <see cref="SqlProcResult"/>.</b>  DataAccess.cs uses
/// <c>var reader = cmd.ExecuteReader()</c> so type inference keeps all call sites
/// compiling without changes; only <see cref="MyCommand.GetJson"/> needed its
/// parameter type updated.
/// </para>
/// </summary>
public sealed class S3SqlCommand : IDisposable
{
    private readonly MsSqlConnection _connection;
    private readonly MsSqlCommand _inner;

    internal S3SqlCommand(MsSqlConnection connection, MsSqlCommand inner)
    {
        _connection = connection;
        _inner = inner;
        Connection = new S3SqlConnection();
        Parameters = new S3SqlParameterCollection(_inner.Parameters);
    }

    public S3SqlConnection Connection { get; }
    public S3SqlParameterCollection Parameters { get; }

    /// <summary>
    /// Executes the stored procedure via TDS RPC (parameters are properly forwarded).
    /// Returns <see cref="SqlProcResult"/>; pass to <see cref="MyCommand.GetJson"/>.
    /// </summary>
    public SqlProcResult ExecuteReader() =>
        _inner.ExecuteProcAsync(CancellationToken.None).GetAwaiter().GetResult();

    public object? ExecuteScalar()
    {
        var result = _inner.ExecuteProcAsync(CancellationToken.None).GetAwaiter().GetResult();
        if (result.Rows.Count == 0 || result.Rows[0].ColumnCount == 0) return null;
        return SqlValueToObject(result.Rows[0][0]);
    }

    public int ExecuteNonQuery()
    {
        var result = _inner.ExecuteProcAsync(CancellationToken.None).GetAwaiter().GetResult();
        return result.ReturnStatus;
    }

    public void Dispose() => _connection.Dispose();

    /// <summary>Converts a <see cref="SqlValue"/> to its natural .NET representation.</summary>
    private static object? SqlValueToObject(SqlValue v) => v switch
    {
        SqlValue.Null    => null,
        SqlValue.Bool b  => b.Value,
        SqlValue.Int8 b  => b.Value,
        SqlValue.Int16 s => s.Value,
        SqlValue.Int32 i => i.Value,
        SqlValue.Int64 l => l.Value,
        SqlValue.Float f => f.Value,
        SqlValue.Double d => d.Value,
        SqlValue.Decimal d => d.Value,
        SqlValue.Text t  => t.Value,
        SqlValue.Bytes b => b.Value,
        SqlValue.Uuid u  => u.Value,
        SqlValue.Date d  => d.Value,
        _                => null,
    };
}

/// <summary>
/// No-op connection wrapper — keeps DataAccess.cs's <c>using (var con = cmd.Connection) { con.Open(); }</c>
/// pattern compiling without changes. Lifetime is owned by the enclosing <see cref="S3SqlCommand"/>.
/// </summary>
public sealed class S3SqlConnection : IDisposable
{
    public void Open() { }
    public void Dispose() { }
}

/// <summary>
/// Forwards <see cref="AddWithValue"/> to the underlying <see cref="MsSqlParameterCollection"/>.
/// MsSqlParameterCollection is mutable; AddWithValue returns <c>this</c> (fluent), so the
/// same instance is always used.
/// </summary>
public sealed class S3SqlParameterCollection
{
    private readonly MsSqlParameterCollection _inner;
    internal S3SqlParameterCollection(MsSqlParameterCollection inner) => _inner = inner;

    public void AddWithValue(string name, object? value) =>
        _inner.AddWithValue(name, value is null or DBNull ? null : value);
}
