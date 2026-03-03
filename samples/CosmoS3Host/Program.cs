using CosmoApiServer.Core.Hosting;
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
    }
};

var app = CosmoWebApplicationBuilder.Create()
    .ListenOn(8100)
    .UseLogging()
    .UseMiddleware(new S3Middleware(settings))
    .Build();

Console.WriteLine("CosmoS3 listening on http://localhost:8100");
Console.WriteLine("Press Ctrl+C to stop.");

app.Run();
