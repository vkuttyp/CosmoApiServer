using System.Text.Json;
using System.Net;
using System.Text;

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
    public IReadOnlyDictionary<string, string> Trailers { get; internal set; } = new Dictionary<string, string>();
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
        ReadOnlySpan<byte> body = Body;
        
        if (body.Length == 0 && BodyStream != Stream.Null)
        {
            // For now, if it's a stream, we still buffer it. 
            // In a deep-dive, we might want a streaming form parser.
            using var ms = new MemoryStream();
            BodyStream.CopyTo(ms);
            body = ms.ToArray();
        }

        if (body.Length == 0) return new MultipartForm { Fields = fields };

        var remaining = body;
        while (!remaining.IsEmpty)
        {
            int amp = remaining.IndexOf((byte)'&');
            var pair = amp < 0 ? remaining : remaining[..amp];
            remaining = amp < 0 ? ReadOnlySpan<byte>.Empty : remaining[(amp + 1)..];

            if (pair.IsEmpty) continue;

            int eq = pair.IndexOf((byte)'=');
            if (eq < 0)
            {
                var key = WebUtility.UrlDecode(Encoding.UTF8.GetString(pair));
                fields[key] = string.Empty;
            }
            else
            {
                var key = WebUtility.UrlDecode(Encoding.UTF8.GetString(pair[..eq]));
                var val = WebUtility.UrlDecode(Encoding.UTF8.GetString(pair[(eq + 1)..]));
                fields[key] = val;
            }
        }
        
        return new MultipartForm { Fields = fields };
    }

    public Task<MultipartForm> ReadFormAsync() => Task.FromResult(ReadForm());

    internal void Reset()
    {
        Method = HttpMethod.GET;
        Path = "/";
        QueryString = string.Empty;
        
        // Try to reuse dictionaries if they are the mutable implementation
        if (Headers is Dictionary<string, string> hd) hd.Clear();
        else Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (Query is Dictionary<string, string> qd) qd.Clear();
        else Query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (Trailers is Dictionary<string, string> td) td.Clear();
        else Trailers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (RouteValues is Dictionary<string, string> rd) rd.Clear();
        else RouteValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        Body = [];
        BodyStream = Stream.Null;
        BodyReader = null;
        ContentLength = 0;
        ContentType = null;
        Host = null;
        Authorization = null;
    }
}
