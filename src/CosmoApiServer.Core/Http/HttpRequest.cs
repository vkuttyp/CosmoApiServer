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
}
