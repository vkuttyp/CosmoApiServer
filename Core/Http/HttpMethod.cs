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
    public static HttpMethod Parse(string value) => value.ToUpperInvariant() switch
    {
        "GET"     => HttpMethod.GET,
        "POST"    => HttpMethod.POST,
        "PUT"     => HttpMethod.PUT,
        "DELETE"  => HttpMethod.DELETE,
        "PATCH"   => HttpMethod.PATCH,
        "HEAD"    => HttpMethod.HEAD,
        "OPTIONS" => HttpMethod.OPTIONS,
        _         => throw new ArgumentException($"Unknown HTTP method: {value}")
    };
}
