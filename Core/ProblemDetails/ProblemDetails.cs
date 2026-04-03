using System.Text.Json.Serialization;

namespace CosmoApiServer.Core.ProblemDetails;

/// <summary>
/// RFC 7807 Problem Details for HTTP APIs.
/// </summary>
public class ProblemDetails
{
    /// <summary>A URI reference that identifies the problem type.</summary>
    [JsonPropertyName("type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Type { get; set; }

    /// <summary>A short, human-readable summary of the problem type.</summary>
    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; set; }

    /// <summary>The HTTP status code.</summary>
    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Status { get; set; }

    /// <summary>A human-readable explanation specific to this occurrence of the problem.</summary>
    [JsonPropertyName("detail")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Detail { get; set; }

    /// <summary>A URI reference that identifies the specific occurrence of the problem.</summary>
    [JsonPropertyName("instance")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Instance { get; set; }

    /// <summary>Extension members (errors, traceId, etc.).</summary>
    [JsonExtensionData]
    public Dictionary<string, object?> Extensions { get; } = new();

    // ── Well-known type URIs ──────────────────────────────────────────────────

    internal static string TypeForStatus(int status) => status switch
    {
        400 => "https://tools.ietf.org/html/rfc7231#section-6.5.1",
        401 => "https://tools.ietf.org/html/rfc7235#section-3.1",
        403 => "https://tools.ietf.org/html/rfc7231#section-6.5.3",
        404 => "https://tools.ietf.org/html/rfc7231#section-6.5.4",
        405 => "https://tools.ietf.org/html/rfc7231#section-6.5.5",
        409 => "https://tools.ietf.org/html/rfc7231#section-6.5.8",
        422 => "https://tools.ietf.org/html/rfc4918#section-11.2",
        429 => "https://tools.ietf.org/html/rfc6585#section-4",
        500 => "https://tools.ietf.org/html/rfc7231#section-6.6.1",
        503 => "https://tools.ietf.org/html/rfc7231#section-6.6.4",
        _   => "about:blank"
    };

    internal static string TitleForStatus(int status) => status switch
    {
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        409 => "Conflict",
        422 => "Unprocessable Entity",
        429 => "Too Many Requests",
        500 => "An error occurred while processing your request.",
        503 => "Service Unavailable",
        _   => "An error occurred."
    };
}
