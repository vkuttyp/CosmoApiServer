using System;
using System.Text.RegularExpressions;

namespace CosmoApiServer.SourceGenerator;

internal static class CSharpHelpers
{
    private static readonly Regex InvalidTypeNameChars = new Regex(@"[^a-zA-Z0-9_]", RegexOptions.Compiled);

    public static bool IsValidTypeName(string identifier) => 
        !string.IsNullOrEmpty(identifier) && char.IsLetter(identifier[0]) && !InvalidTypeNameChars.IsMatch(identifier);

    public static string CreateValidTypeName(string identifier)
    {
        var sanitized = InvalidTypeNameChars.Replace(identifier, "_");
        if (char.IsDigit(sanitized[0])) sanitized = "_" + sanitized;
        return sanitized;
    }

    public static bool IsValidNamespace(string ns) => 
        !string.IsNullOrEmpty(ns) && Array.TrueForAll(ns.Split('.'), IsValidTypeName);

    public static string CreateValidNamespace(string ns) =>
        string.Join(".", Array.ConvertAll(ns.Split('.'), CreateValidTypeName));
}
