using CosmoSQLClient.Core;
using CosmoSQLClient.MsSql;
using System.Data;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CosmoS3;

/// <summary>
/// Drop-in replacement for the original Microsoft.Data.SqlClient-based MyCommand,
/// now backed by CosmoSQLClient's <see cref="MsSqlConnectionPool"/>.
///
/// DataAccess.cs uses the same call pattern unchanged:
/// <code>
/// using (var cmd = MyCommand.CmdProc("s3.Proc", conString))
/// using (var con = cmd.Connection)
/// {
///     cmd.Parameters.AddWithValue("@p", value);
///     con.Open();
///     var reader = cmd.ExecuteReader();
/// }
/// </code>
/// </summary>
public static class MyCommand
{
    static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    // Shared pool, lazily initialised from the first connection string seen.
    static MsSqlConnectionPool? _pool;
    static string? _lastConString;
    static readonly object _poolLock = new();

    static MsSqlConnectionPool GetPool(string conString)
    {
        if (_pool != null && _lastConString == conString) return _pool;
        lock (_poolLock)
        {
            if (_pool != null && _lastConString == conString) return _pool;
            // Intentionally not disposing old pool — it's a long-lived singleton;
            // connection string changes are not expected at runtime.
            var cfg = MsSqlConfiguration.Parse(conString);
            _pool = new MsSqlConnectionPool(cfg, maxConnections: 50, minIdle: 5);
            _lastConString = conString;
            return _pool;
        }
    }

    /// <summary>
    /// Creates a <see cref="CosmoSqlCommand"/> for <paramref name="proc"/> backed by the
    /// shared CosmoSQLClient pool.  The returned object has the same <c>using</c> lifetime
    /// as the original <c>SqlCommand</c>.
    /// </summary>
    public static CosmoSqlCommand CmdProc(string proc, string conString)
        => new CosmoSqlCommand(GetPool(conString), proc);

    /// <summary>
    /// Reads all rows of the first result set and concatenates column 0 —
    /// used to reassemble JSON produced by SQL Server's FOR JSON PATH.
    /// </summary>
    public static string GetJson(CosmoSqlReader reader)
    {
        var sb = new StringBuilder();
        while (reader.Read())
            sb.Append(reader.GetValue(0));
        return sb.ToString();
    }

    public static Task<string> GetJsonAsync(CosmoSqlReader reader)
        => Task.FromResult(GetJson(reader));

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
/// Drop-in replacement for <c>SqlCommand</c> (stored-procedure variant).
/// Accumulates parameters and executes the procedure via <see cref="MsSqlConnectionPool"/>.
/// </summary>
public sealed class CosmoSqlCommand : IDisposable
{
    readonly MsSqlConnectionPool _pool;
    readonly string _proc;
    readonly List<SqlParameter> _params = new();

    // Expose a self-reference as "Connection" so the DataAccess pattern
    //   using (var con = cmd.Connection) { con.Open(); ... }
    // continues to compile without changes.
    public CosmoSqlCommand Connection => this;
    public CosmoSqlParameterCollection Parameters { get; }

    internal CosmoSqlCommand(MsSqlConnectionPool pool, string proc)
    {
        _pool   = pool;
        _proc   = proc;
        Parameters = new CosmoSqlParameterCollection(_params);
    }

    /// <summary>No-op — pool connections are acquired per-execute.</summary>
    public void Open() { }

    public object? ExecuteScalar()
    {
        var result = _pool.ExecuteProcAsync(_proc, _params).GetAwaiter().GetResult();
        if (result.Rows.Count == 0 || result.Rows[0].ColumnCount == 0) return null;
        var v = result.Rows[0][0];
        return v.IsNull ? null : v.ToClrObject();
    }

    public int ExecuteNonQuery()
    {
        _pool.ExecuteProcAsync(_proc, _params).GetAwaiter().GetResult();
        return 0;
    }

    public CosmoSqlReader ExecuteReader()
    {
        var result = _pool.ExecuteProcAsync(_proc, _params).GetAwaiter().GetResult();
        return new CosmoSqlReader(result.Rows);
    }

    public void Dispose() { /* pool connections are released after each execute */ }
}

/// <summary>
/// Thin parameter collection matching the <c>cmd.Parameters.AddWithValue(name, value)</c> pattern.
/// </summary>
public sealed class CosmoSqlParameterCollection
{
    readonly List<SqlParameter> _list;
    internal CosmoSqlParameterCollection(List<SqlParameter> list) => _list = list;

    public void AddWithValue(string name, object? value)
    {
        var sv = value switch
        {
            null          => SqlValue.Null_,
            bool b        => SqlValue.From(b),
            byte by       => SqlValue.From((int)by),
            short s       => SqlValue.From((int)s),
            int i         => SqlValue.From(i),
            long l        => SqlValue.From(l),
            float f       => SqlValue.From((double)f),
            double d      => SqlValue.From(d),
            decimal m     => SqlValue.From(m),
            Guid g        => SqlValue.From(g),
            DateTime dt   => SqlValue.From(dt),
            string str    => SqlValue.From(str),
            byte[] bytes  => SqlValue.From(bytes),
            _             => SqlValue.From(value.ToString()!),
        };
        _list.Add(SqlParameter.Named(name, sv));
    }
}

/// <summary>
/// Drop-in replacement for <c>SqlDataReader</c> — wraps <see cref="SqlRow"/> list.
/// </summary>
public sealed class CosmoSqlReader
{
    readonly IReadOnlyList<SqlRow> _rows;
    int _index = -1;

    internal CosmoSqlReader(IReadOnlyList<SqlRow> rows) => _rows = rows;

    public bool Read() => ++_index < _rows.Count;

    public object GetValue(int i)
    {
        var v = _rows[_index][i];
        return v.IsNull ? DBNull.Value : v.ToClrObject()!;
    }

    public bool IsDBNull(int i) => _rows[_index][i].IsNull;
    public int    GetInt32(int i)  => (int)_rows[_index][i].ToClrObject()!;
    public long   GetInt64(int i)  => (long)_rows[_index][i].ToClrObject()!;
    public string GetString(int i) => (string)_rows[_index][i].ToClrObject()!;
    public bool   GetBoolean(int i)=> (bool)_rows[_index][i].ToClrObject()!;
    public Guid   GetGuid(int i)   => (Guid)_rows[_index][i].ToClrObject()!;
}
