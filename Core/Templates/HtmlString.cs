using System.Net;
using System.Text;
using System.Collections.Concurrent;
using System.Buffers;

namespace CosmoApiServer.Core.Templates;

/// <summary>
/// A string that is already HTML-encoded and should not be encoded again.
/// </summary>
public sealed class HtmlString(string? value)
{
    private readonly string _value = value ?? string.Empty;

    public override string ToString() => _value;

    public static implicit operator string(HtmlString htmlString) => htmlString.ToString();

    public static HtmlString FromEncoded(string value) => new(value);
}

/// <summary>
/// Provides high-performance HTML encoding utilities that write directly to buffers.
/// </summary>
public static class HtmlEncoder
{
    public static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
    public static string Encode(object? value) => WebUtility.HtmlEncode(value?.ToString() ?? string.Empty);

    /// <summary>
    /// Encodes the string and writes UTF-8 bytes directly to the writer.
    /// </summary>
    public static void EncodeToWriter(string? value, IBufferWriter<byte> writer)
    {
        if (string.IsNullOrEmpty(value)) return;

        // Use WebUtility.HtmlEncode for now, but in a real high-perf scenario
        // we'd use a custom SIMD-based encoder that writes to spans.
        var encoded = WebUtility.HtmlEncode(value);
        var bytes = Encoding.UTF8.GetBytes(encoded);
        writer.Write(bytes);
    }

    /// <summary>
    /// Encodes the object's string representation and writes UTF-8 bytes directly to the writer.
    /// </summary>
    public static async ValueTask EncodeToWriterAsync(object? value, IBufferWriter<byte> writer)
    {
        if (value == null) return;
        if (value is string s) EncodeToWriter(s, writer);
        else if (value is HtmlString html) writer.Write(Encoding.UTF8.GetBytes(html.ToString()));
        else if (value is RenderToBufferDelegate fragment) await fragment(writer);
        else EncodeToWriter(value.ToString(), writer);
    }
}

/// <summary>
/// Global cache for UTF-8 encoded versions of static strings.
/// Prevents repetitive Encoding.UTF8.GetBytes() calls for static HTML markup.
/// </summary>
internal static class Utf8LiteralCache
{
    private static readonly ConcurrentDictionary<string, byte[]> _cache = new();

    public static byte[] GetEncoded(string value)
    {
        return _cache.GetOrAdd(value, static v => Encoding.UTF8.GetBytes(v));
    }
}
