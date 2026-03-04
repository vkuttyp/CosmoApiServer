using CosmoS3;
using CosmoS3.Settings;
using Microsoft.Extensions.Configuration;

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .Build();

var settings = config.Get<SettingsBase>()
    ?? throw new InvalidOperationException("appsettings.json is empty or invalid.");

var port = config.GetValue<int>("Port", 8100);

var app = CosmoS3Application.Create(settings, port: port);

Console.WriteLine($"CosmoS3 listening on http://localhost:{port}");
Console.WriteLine("Press Ctrl+C to stop.");

app.Run();

