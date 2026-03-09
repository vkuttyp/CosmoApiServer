using System.Net;

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
/// Provides HTML encoding utilities for Razor templates.
/// </summary>
public static class HtmlEncoder
{
    public static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
    public static string Encode(object? value) => WebUtility.HtmlEncode(value?.ToString() ?? string.Empty);
}
