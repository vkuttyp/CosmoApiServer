using CosmoSQLClient.Core;
using CosmoSQLClient.MsSql;
using CosmoSQLClient.Postgres;
using CosmoSQLClient.MySql;
using CosmoSQLClient.Sqlite;
using CosmoS3.Settings;

namespace CosmoS3;

/// <summary>
/// Creates an <see cref="ISqlDatabase"/> pool and an <see cref="IS3Repository"/> from
/// <see cref="DatabaseSettings"/>. Supports MsSql, Postgres, MySQL, and SQLite.
/// </summary>
public static class DatabaseFactory
{
    /// <summary>
    /// Creates an <see cref="IS3Repository"/> from the settings provided.
    /// The <see cref="DatabaseSettings.ConnectionString"/> is used directly when set;
    /// otherwise a connection string is built from the individual host/port/user/password fields.
    /// </summary>
    public static IS3Repository Create(DatabaseSettings settings)
    {
        var type = (settings.DatabaseType ?? "mssql").ToLowerInvariant().Trim();
        var cs   = settings.ConnectionString ?? BuildConnectionString(settings, type);

        ISqlDatabase db;
        string tablePrefix;

        switch (type)
        {
            case "mssql":
                db          = new MsSqlConnectionPool(MsSqlConfiguration.Parse(cs), maxConnections: 50, minIdle: 5);
                tablePrefix = "s3.";
                break;

            case "postgres":
                db          = new PostgresConnectionPool(PostgresConfiguration.Parse(cs), maxConnections: 50);
                tablePrefix = "s3.";
                break;

            case "mysql":
                db          = new MySqlConnectionPool(MySqlConfiguration.Parse(cs), maxConnections: 50);
                tablePrefix = "s3_";
                break;

            case "sqlite":
                db          = new SqliteConnectionPool(SqliteConfiguration.Parse(cs), maxConnections: 10);
                tablePrefix = "s3_";
                break;

            default:
                throw new ArgumentException(
                    $"Unknown DatabaseType '{settings.DatabaseType}'. Valid values: mssql, postgres, mysql, sqlite.");
        }

        return new S3Repository(db, tablePrefix);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    static string BuildConnectionString(DatabaseSettings db, string type) => type switch
    {
        "mssql"    => $"Server={db.Hostname},{db.Port};Database={db.DatabaseName};User Id={db.Username};Password={db.Password};TrustServerCertificate=True;Connect Timeout=10;",
        "postgres" => $"Host={db.Hostname};Port={db.Port};Database={db.DatabaseName};Username={db.Username};Password={db.Password};",
        "mysql"    => $"Server={db.Hostname};Port={db.Port};Database={db.DatabaseName};User={db.Username};Password={db.Password};",
        "sqlite"   => $"Data Source={db.DatabaseName ?? "cosmos3.db"};",
        _          => throw new ArgumentException($"Unknown DatabaseType '{type}'.")
    };
}
