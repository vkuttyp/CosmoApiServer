using CosmoS3;
using CosmoS3.Settings;
using CosmoS3.Storage;

// CosmoS3 — SQL Server backend
// Port: 8104
// DB:   Microsoft SQL Server
//       Update the ConnectionString below to match your environment.

var settings = new SettingsBase
{
    ValidateSignatures = false,
    HeaderApiKey       = "x-api-key",
    AdminApiKey        = "changeme-admin-key",
    RegionString       = "us-east-1",

    Database = new DatabaseSettings
    {
        DatabaseType     = "mssql",
        ConnectionString = "Server=127.0.0.1,1433;Database=cosmos3;User Id=sa;Password=YourPassword123!;TrustServerCertificate=True;"
    },

    Storage = new StorageSettings
    {
        StorageType   = StorageDriverType.Disk,
        DiskDirectory = "./data/objects",
        TempDirectory = "./temp/"
    },

    Cors    = new CorsSettings    { Enabled = true, AllowedOrigins = ["*"] },
    Logging = new LoggingSettings { ConsoleLogging = true, DiskLogging = false },
    Debug   = new DebugSettings   { Authentication = false, S3Requests = false, Exceptions = false }
};

const int port = 8104;

// Ensure schema and seed data exist (idempotent)
await DatabaseFactory.EnsureSchemaAsync(settings.Database);

var app = CosmoS3Application.Create(settings, port: port);

Console.WriteLine($"CosmoS3 [SQL Server] listening on http://localhost:{port}");
Console.WriteLine("Press Ctrl+C to stop.");

app.Run();
