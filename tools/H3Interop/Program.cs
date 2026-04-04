// HTTP/3 interop validation tool — mirrors test_http3_interop.sh
// Usage: H3Interop <baseUrl> [repeatCount]
//   baseUrl     e.g. https://localhost:9443
//   repeatCount number of repeat requests for regression checks (default 10)
//
// The tool assumes the server is already running.
// Exit code 0 = all pass, 1 = one or more failures.

using System.Net;
using System.Net.Http;
using System.Net.Security;

var baseUrl     = args.Length > 0 ? args[0].TrimEnd('/') : "https://localhost:9443";
var repeatCount = args.Length > 1 && int.TryParse(args[1], out var rc) ? rc : 10;

var handler = new SocketsHttpHandler
{
    SslOptions = new SslClientAuthenticationOptions
    {
        ApplicationProtocols = [new("h3")],
        RemoteCertificateValidationCallback = static (_, _, _, _) => true
    },
    EnableMultipleHttp2Connections = false,
    PooledConnectionIdleTimeout    = TimeSpan.FromSeconds(30),
};

using var client = new HttpClient(handler)
{
    DefaultRequestVersion = HttpVersion.Version30,
    DefaultVersionPolicy  = HttpVersionPolicy.RequestVersionExact,
    Timeout               = TimeSpan.FromSeconds(15),
};

int pass = 0, fail = 0;

async Task Check(string name, string url, int expectedStatus = 200,
    HttpMethod? method = null, string body = "", string contentType = "",
    string? bodyPattern = null)
{
    method ??= HttpMethod.Get;
    try
    {
        HttpResponseMessage resp;
        if (method == HttpMethod.Post)
        {
            using var content = new StringContent(body, System.Text.Encoding.UTF8, contentType);
            resp = await client.PostAsync(url, content);
        }
        else
        {
            resp = await client.GetAsync(url);
        }

        int status = (int)resp.StatusCode;
        string ver = resp.Version.ToString();

        if (status != expectedStatus)
        {
            Console.WriteLine($"FAIL [{name}]: expected {expectedStatus} got {status} (HTTP/{ver})");
            fail++;
            return;
        }
        if (bodyPattern is not null)
        {
            var respBody = await resp.Content.ReadAsStringAsync();
            if (!respBody.Contains(bodyPattern, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"FAIL [{name}]: body missing '{bodyPattern}' (got: {respBody[..Math.Min(200, respBody.Length)]})");
                fail++;
                return;
            }
        }
        Console.WriteLine($"PASS [{name}]: HTTP {status} (HTTP/{ver})");
        pass++;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAIL [{name}]: {ex.GetType().Name}: {ex.Message}");
        fail++;
    }
}

Console.WriteLine($"=== HTTP/3 Interop Tests: {baseUrl} ===");
Console.WriteLine();

Console.WriteLine("--- Core transport ---");
await Check("GET /ping",               $"{baseUrl}/ping");
await Check("GET /ping body",          $"{baseUrl}/ping",              bodyPattern: "pong");
await Check("GET /json",               $"{baseUrl}/json");
await Check("GET /route/42",           $"{baseUrl}/route/42");
await Check("GET /not-found -> 404",   $"{baseUrl}/this-does-not-exist", expectedStatus: 404);
await Check("POST /echo",              $"{baseUrl}/echo",              method: HttpMethod.Post,
    body: "{\"msg\":\"hello\"}", contentType: "application/json");
await Check("GET /query",              $"{baseUrl}/query?name=test&id=1");
await Check("GET /headers",            $"{baseUrl}/headers");
await Check("POST /form",              $"{baseUrl}/form",              method: HttpMethod.Post,
    body: "test=interop", contentType: "application/x-www-form-urlencoded");

Console.WriteLine();
Console.WriteLine("--- Streaming / large responses ---");
await Check("GET /large-json",         $"{baseUrl}/large-json");
await Check("GET /stream",             $"{baseUrl}/stream");
await Check("GET /file",               $"{baseUrl}/file");

Console.WriteLine();
Console.WriteLine($"--- Regression: repeated large responses ({repeatCount}x) ---");
for (int i = 1; i <= repeatCount; i++)
    await Check($"GET /large-json #{i}", $"{baseUrl}/large-json");
for (int i = 1; i <= repeatCount; i++)
    await Check($"GET /stream #{i}",     $"{baseUrl}/stream");

Console.WriteLine();
Console.WriteLine($"=== Results: PASS={pass} FAIL={fail} ===");
return fail > 0 ? 1 : 0;
