namespace CosmoApiServer.Core.Http;

public enum HttpMethod
{
    GET,
    POST,
    PUT,
    DELETE,
    PATCH,
    HEAD,
    OPTIONS
}

public static class HttpMethodExtensions
{
    public static HttpMethod Parse(string value)
    {
        // Case-insensitive comparison without allocating a new uppercase string
        if (string.Equals(value, "GET", StringComparison.OrdinalIgnoreCase)) return HttpMethod.GET;
        if (string.Equals(value, "POST", StringComparison.OrdinalIgnoreCase)) return HttpMethod.POST;
        if (string.Equals(value, "PUT", StringComparison.OrdinalIgnoreCase)) return HttpMethod.PUT;
        if (string.Equals(value, "DELETE", StringComparison.OrdinalIgnoreCase)) return HttpMethod.DELETE;
        if (string.Equals(value, "PATCH", StringComparison.OrdinalIgnoreCase)) return HttpMethod.PATCH;
        if (string.Equals(value, "HEAD", StringComparison.OrdinalIgnoreCase)) return HttpMethod.HEAD;
        if (string.Equals(value, "OPTIONS", StringComparison.OrdinalIgnoreCase)) return HttpMethod.OPTIONS;
        throw new ArgumentException($"Unknown HTTP method: {value}");
    }
}
