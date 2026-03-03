using CosmoS3;
using CosmoS3.Settings;
using CosmoS3.Storage;

// CosmoS3 — SQLite backend
// Port: 8101
// DB:   SQLite at ./data/cosmo_s3.db

var settings = new SettingsBase
{
    ValidateSignatures = false,
    HeaderApiKey       = "x-api-key",
    AdminApiKey        = "changeme-admin-key",
    RegionString       = "us-east-1",

    Database = new DatabaseSettings
    {
        DatabaseType     = "sqlite",
        ConnectionString = "Data Source=./data/cosmo_s3.db"
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

const int port = 8101;

// Ensure tables and seed data exist (idempotent)
Directory.CreateDirectory("./data");
await DatabaseFactory.EnsureSchemaAsync(settings.Database);

var app = CosmoS3Application.Create(settings, port: port);

Console.WriteLine($"CosmoS3 [SQLite] listening on http://localhost:{port}");
Console.WriteLine("Press Ctrl+C to stop.");

app.Run();
