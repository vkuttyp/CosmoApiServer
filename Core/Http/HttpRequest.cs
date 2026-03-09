using System.Text.Json;

namespace CosmoApiServer.Core.Http;

public sealed class HttpRequest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    public HttpMethod Method { get; init; }
    public string Path { get; init; } = "/";
    public string QueryString { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Query { get; init; } = new Dictionary<string, string>();
    public byte[] Body { get; init; } = [];

    // Populated by router after route match
    public IReadOnlyDictionary<string, string> RouteValues { get; set; } = new Dictionary<string, string>();

    public T? ReadJson<T>() =>
        Body.Length > 0
            ? System.Text.Json.JsonSerializer.Deserialize<T>(Body, JsonOptions)
            : default;

    /// <summary>Parse a multipart/form-data body. Throws if Content-Type is not multipart/form-data.</summary>
    public MultipartForm ReadMultipart() => MultipartParser.Parse(this);

    /// <summary>Parse an application/x-www-form-urlencoded body.</summary>
    public MultipartForm ReadForm()
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Body.Length == 0) return new MultipartForm { Fields = fields };

        var content = System.Text.Encoding.UTF8.GetString(Body);
        var pairs = content.Split('&');
        foreach (var pair in pairs)
        {
            var eq = pair.IndexOf('=');
            if (eq < 0)
                fields[Uri.UnescapeDataString(pair)] = string.Empty;
            else
                fields[Uri.UnescapeDataString(pair[..eq])] = Uri.UnescapeDataString(pair[(eq + 1)..]);
        }
        return new MultipartForm { Fields = fields };
    }
}
