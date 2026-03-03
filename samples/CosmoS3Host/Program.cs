using CosmoS3;
using CosmoS3.Settings;

var settings = new SettingsBase
{
    HeaderApiKey  = "x-api-key",
    AdminApiKey   = "changeme-admin-key",
    RegionString  = "us-east-1",
    ValidateSignatures = false,

    Storage = new StorageSettings
    {
        StorageType   = CosmoS3.Storage.StorageDriverType.Disk,
        DiskDirectory = "./data/objects"
    },

    Database = new DatabaseSettings
    {
        Hostname     = "localhost",
        Port         = 1433,
        DatabaseName = "MurshiDb",
        Username     = "sa",
        Password     = "aBCD111"
    },

    // Optional: enable CORS for browser-based S3 clients
    Cors = new CorsSettings
    {
        Enabled        = true,
        AllowedOrigins = ["*"]   // restrict in production, e.g. ["https://myapp.example.com"]
    }

    // Optional: enable TLS/HTTPS
    // CertificatePath     = "./certs/server.pfx",
    // CertificatePassword = "changeme",

    // Optional: enable HTTP/2 cleartext (h2c)
    // EnableHttp2 = true,
};

// CosmoS3Application.Create() automatically wires TLS, HTTP/2, CORS,
// request logging, and the S3Middleware from the settings above.
var app = CosmoS3Application.Create(settings, port: 8100);

Console.WriteLine("CosmoS3 listening on http://localhost:8100");
Console.WriteLine("Press Ctrl+C to stop.");

app.Run();
