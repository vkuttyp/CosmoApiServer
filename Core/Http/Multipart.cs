using System.Text;

namespace CosmoApiServer.Core.Http;

/// <summary>A single file from a multipart/form-data upload.</summary>
public sealed class MultipartFile
{
    public string Name { get; init; } = string.Empty;
    public string Filename { get; init; } = string.Empty;
    public string ContentType { get; init; } = "application/octet-stream";
    public byte[] Data { get; init; } = [];
}

/// <summary>Parsed result of a multipart/form-data request body.</summary>
public sealed class MultipartForm
{
    public IReadOnlyDictionary<string, string> Fields { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, MultipartFile> Files { get; init; } = new Dictionary<string, MultipartFile>();
}

/// <summary>Parses multipart/form-data bodies.</summary>
public static class MultipartParser
{
    /// <summary>Parse from an HttpRequest. Throws if Content-Type is not multipart/form-data.</summary>
    public static MultipartForm Parse(HttpRequest request)
    {
        var ct = request.Headers.TryGetValue("content-type", out var v) ? v : string.Empty;
        return Parse(request.Body, ct);
    }

    /// <summary>Parse from raw body bytes and a Content-Type header value.</summary>
    public static MultipartForm Parse(byte[] body, string contentType)
    {
        var boundary = ExtractBoundary(contentType)
            ?? throw new InvalidOperationException("Content-Type is not multipart/form-data or missing boundary.");
        return ParseWithBoundary(body, boundary);
    }

    // -------------------------------------------------------------------------

    private static string? ExtractBoundary(string contentType)
    {
        // e.g. "multipart/form-data; boundary=----WebKitFormBoundary"
        if (!contentType.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            return null;

        foreach (var part in contentType.Split(';'))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("boundary=", StringComparison.OrdinalIgnoreCase))
            {
                var b = trimmed["boundary=".Length..].Trim('"');
                return b.Length > 0 ? b : null;
            }
        }
        return null;
    }

    private static MultipartForm ParseWithBoundary(byte[] body, string boundary)
    {
        var fields = new Dictionary<string, string>();
        var files  = new Dictionary<string, MultipartFile>();

        // Delimiters as bytes
        var delimiter    = Encoding.ASCII.GetBytes("--" + boundary);
        var doubleCrlf   = "\r\n\r\n"u8.ToArray();
        var nextDelimPrefix = Encoding.ASCII.GetBytes("\r\n--" + boundary);

        int pos = 0;

        // Skip preamble — find first delimiter
        int firstDelim = IndexOf(body, delimiter, pos);
        if (firstDelim < 0) return new MultipartForm { Fields = fields, Files = files };
        pos = firstDelim + delimiter.Length;

        while (pos < body.Length)
        {
            // After delimiter: expect \r\n (part) or -- (end)
            if (pos + 1 < body.Length && body[pos] == '-' && body[pos + 1] == '-')
                break; // final boundary

            if (pos + 1 < body.Length && body[pos] == '\r' && body[pos + 1] == '\n')
                pos += 2; // skip CRLF after delimiter
            else
                break;

            // Find end of headers (double CRLF)
            int headersEnd = IndexOf(body, doubleCrlf, pos);
            if (headersEnd < 0) break;

            var headerBytes = body[pos..headersEnd];
            var headers = ParsePartHeaders(Encoding.UTF8.GetString(headerBytes));
            pos = headersEnd + doubleCrlf.Length;

            // Find next delimiter to bound the part body
            // Next delimiter is \r\n--{boundary}
            int nextDelim = IndexOf(body, nextDelimPrefix, pos);
            if (nextDelim < 0) break;

            var partBody = body[pos..nextDelim];
            pos = nextDelim + nextDelimPrefix.Length;

            // Parse Content-Disposition
            if (!headers.TryGetValue("content-disposition", out var disposition))
                continue;

            var name     = ExtractParam(disposition, "name");
            var filename = ExtractParam(disposition, "filename");

            if (name is null) continue;

            if (filename is not null)
            {
                var partCt = headers.TryGetValue("content-type", out var pct)
                    ? pct.Trim()
                    : "application/octet-stream";
                files[name] = new MultipartFile
                {
                    Name        = name,
                    Filename    = filename,
                    ContentType = partCt,
                    Data        = partBody
                };
            }
            else
            {
                fields[name] = Encoding.UTF8.GetString(partBody);
            }
        }

        return new MultipartForm { Fields = fields, Files = files };
    }

    private static Dictionary<string, string> ParsePartHeaders(string raw)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in raw.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = line.IndexOf(':');
            if (colon < 0) continue;
            headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }
        return headers;
    }

    private static string? ExtractParam(string header, string param)
    {
        // Handles: name="value" or name=value
        // Use word-boundary-aware search to avoid matching substrings
        // (e.g., "name=" must not match inside "filename=")
        var search = param + "=";
        int idx = 0;
        while (true)
        {
            idx = header.IndexOf(search, idx, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            // Ensure it's not a substring of another parameter name:
            // the char before must be a delimiter (start of string, space, or semicolon)
            if (idx == 0 || header[idx - 1] == ' ' || header[idx - 1] == ';')
                break;
            idx += search.Length; // skip this false match and try again
        }

        var start = idx + search.Length;
        if (start >= header.Length) return null;

        if (header[start] == '"')
        {
            var end = header.IndexOf('"', start + 1);
            return end < 0 ? null : header[(start + 1)..end];
        }

        var semi = header.IndexOf(';', start);
        return semi < 0 ? header[start..].Trim() : header[start..semi].Trim();
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int start = 0)
    {
        var span = haystack.AsSpan(start);
        var idx  = span.IndexOf(needle);
        return idx < 0 ? -1 : start + idx;
    }
}
