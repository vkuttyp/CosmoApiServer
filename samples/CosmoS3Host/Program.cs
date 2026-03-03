using System.Text.Json;
using CosmoS3;
using CosmoS3.Settings;

var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var settings    = JsonSerializer.Deserialize<SettingsBase>(
                      File.ReadAllText("system.json"), jsonOptions)
                  ?? throw new InvalidOperationException("system.json is empty or invalid.");

// CosmoS3Application.Create() automatically wires TLS, HTTP/2, CORS,
// request logging, and the S3Middleware from the settings above.
var app = CosmoS3Application.Create(settings, port: 8100);

Console.WriteLine("CosmoS3 listening on http://localhost:8100");
Console.WriteLine("Press Ctrl+C to stop.");

app.Run();
