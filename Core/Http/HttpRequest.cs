using System.Text.Json;
using System.Net;

namespace CosmoApiServer.Core.Http;

public sealed class HttpRequest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    public HttpMethod Method { get; set; }
    public string Path { get; set; } = "/";
    public string QueryString { get; set; } = string.Empty;
    public IReadOnlyDictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Query { get; set; } = new Dictionary<string, string>();
    public byte[] Body { get; set; } = [];
    public Stream BodyStream { get; set; } = Stream.Null;
    public System.IO.Pipelines.PipeReader? BodyReader { get; set; }

    // Pre-parsed "well-known" headers for zero-dictionary access
    public long ContentLength { get; internal set; }
    public string? ContentType { get; internal set; }
    public string? Host { get; internal set; }
    public string? Authorization { get; internal set; }

    // Populated by router after route match
    public IReadOnlyDictionary<string, string> RouteValues { get; set; } = new Dictionary<string, string>();

    public T? ReadJson<T>()
    {
        if (Body.Length > 0)
            return JsonSerializer.Deserialize<T>(Body, JsonOptions);

        if (BodyStream != Stream.Null)
        {
            // For now, if we have a stream, we still buffer it for JSON
            // In the future, we can use JsonSerializer.DeserializeAsync(BodyStream)
            using var ms = new MemoryStream();
            BodyStream.CopyTo(ms);
            return JsonSerializer.Deserialize<T>(ms.ToArray(), JsonOptions);
        }

        return default;
    }

    /// <summary>Parse a multipart/form-data body. Throws if Content-Type is not multipart/form-data.</summary>
    public MultipartForm ReadMultipart() => MultipartParser.Parse(this);

    /// <summary>Parse an application/x-www-form-urlencoded body.</summary>
    public MultipartForm ReadForm()
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        byte[] body = Body;
        if (body.Length == 0 && BodyStream != Stream.Null)
        {
            using var ms = new MemoryStream();
            BodyStream.CopyTo(ms);
            body = ms.ToArray();
        }

        if (body.Length == 0) return new MultipartForm { Fields = fields };

        var content = System.Text.Encoding.UTF8.GetString(body);
        var pairs = content.Split('&');
        foreach (var pair in pairs)
        {
            var eq = pair.IndexOf('=');
            if (eq < 0)
                fields[WebUtility.UrlDecode(pair)] = string.Empty;
            else
                fields[WebUtility.UrlDecode(pair[..eq])] = WebUtility.UrlDecode(pair[(eq + 1)..]);
        }
        return new MultipartForm { Fields = fields };
    }

    internal void Reset()
    {
        Method = HttpMethod.GET;
        Path = "/";
        QueryString = string.Empty;
        Headers = new Dictionary<string, string>();
        Query = new Dictionary<string, string>();
        Body = [];
        BodyStream = Stream.Null;
        BodyReader = null;
        RouteValues = new Dictionary<string, string>();
        ContentLength = 0;
        ContentType = null;
        Host = null;
        Authorization = null;
    }

}
