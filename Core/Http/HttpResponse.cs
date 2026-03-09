using System.Text;
using System.Text.Json;

namespace CosmoApiServer.Core.Http;

public sealed class HttpResponse
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    public int StatusCode { get; set; } = 200;
    public string ReasonPhrase { get; set; } = "OK";
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HttpContext HttpContext { get; internal set; } = null!;

    private byte[]? _body;

    public byte[] Body => _body ?? [];

    public void Write(byte[] data)
    {
        _body = data;
        if (!Headers.ContainsKey("Content-Length"))
            Headers["Content-Length"] = data.Length.ToString();
    }

    public void WriteText(string text, string contentType = "text/plain; charset=utf-8")
    {
        Headers["Content-Type"] = contentType;
        Write(Encoding.UTF8.GetBytes(text));
    }

    public void WriteJson<T>(T value)
    {
        Headers["Content-Type"] = "application/json; charset=utf-8";
        Write(JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions));
    }

    public async Task WriteJsonAsync<T>(T value, CancellationToken ct = default)
    {
        Headers["Content-Type"] = "application/json; charset=utf-8";
        using var ms = new MemoryStream();
        await JsonSerializer.SerializeAsync(ms, value, JsonOptions, ct);
        Write(ms.ToArray());
    }

    public bool IsStarted => _body is not null;
}
