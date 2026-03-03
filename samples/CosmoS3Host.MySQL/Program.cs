using CosmoS3;
using CosmoS3.Settings;
using CosmoS3.Storage;

// CosmoS3 — MySQL backend
// Port: 8103
// DB:   MySQL (Docker: cosmo_mysql, port 3306)
//       Update the ConnectionString below to match your environment.

var settings = new SettingsBase
{
    ValidateSignatures = false,
    HeaderApiKey       = "x-api-key",
    AdminApiKey        = "changeme-admin-key",
    RegionString       = "us-east-1",

    Database = new DatabaseSettings
    {
        DatabaseType     = "mysql",
        ConnectionString = "Server=127.0.0.1;Port=3306;Database=cosmos3;User=cosmo;Password=cosmo123;"
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

const int port = 8103;

// Ensure schema and seed data exist (idempotent)
await DatabaseFactory.EnsureSchemaAsync(settings.Database);

var app = CosmoS3Application.Create(settings, port: port);

Console.WriteLine($"CosmoS3 [MySQL] listening on http://localhost:{port}");
Console.WriteLine("Press Ctrl+C to stop.");

app.Run();
