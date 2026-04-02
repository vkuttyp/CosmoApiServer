using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Text.Json;

var url = args.Length > 0 ? args[0] : "https://localhost:9443/large-json";
var count = args.Length > 1 && int.TryParse(args[1], out var parsed) ? parsed : 20;

var handler = new SocketsHttpHandler
{
    SslOptions = new SslClientAuthenticationOptions
    {
        ApplicationProtocols = new List<SslApplicationProtocol> { new("h3") },
        RemoteCertificateValidationCallback = static (_, _, _, _) => true
    }
};

using var client = new HttpClient(handler)
{
    DefaultRequestVersion = HttpVersion.Version30,
    DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
};

var results = new List<object>();

for (var i = 0; i < count; i++)
{
    try
    {
        using var response = await client.GetAsync(url);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        results.Add(new
        {
            Index = i,
            Status = (int)response.StatusCode,
            Version = response.Version.ToString(),
            Bytes = bytes.Length,
            Error = ""
        });
    }
    catch (Exception ex)
    {
        results.Add(new
        {
            Index = i,
            Status = -1,
            Version = "",
            Bytes = 0,
            Error = ex.GetType().FullName + ": " + ex.Message
        });
        break;
    }
}

Console.WriteLine(JsonSerializer.Serialize(results));
