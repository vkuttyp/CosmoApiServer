using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.Text;

namespace CosmoApiServer.SourceGenerator;

internal static class RazorDirectiveParser
{
    internal static string? ParseInheritsDirective(SourceText sourceText)
    {
        foreach (var line in sourceText.Lines)
        {
            var lineText = line.ToString().TrimStart();
            if (lineText.StartsWith("@inherits ", StringComparison.Ordinal))
            {
                var value = lineText.Substring("@inherits ".Length).Trim();
                if (value.Length > 0) return value;
            }
        }
        return null;
    }

    internal static List<UsingDirective> ParseUsingDirectives(SourceText sourceText)
    {
        var usings = new List<UsingDirective>();
        foreach (var line in sourceText.Lines)
        {
            var lineText = line.ToString().TrimStart();
            if (lineText.StartsWith("@using ", StringComparison.Ordinal))
            {
                var value = lineText.Substring("@using ".Length).Trim();
                if (value.EndsWith(";", StringComparison.Ordinal))
                    value = value.Substring(0, value.Length - 1).Trim();
                
                if (value.Length == 0) continue;

                var equalsIndex = value.IndexOf('=');
                if (equalsIndex > 0)
                {
                    var alias = value.Substring(0, equalsIndex).Trim();
                    var target = value.Substring(equalsIndex + 1).Trim();
                    if (alias.Length > 0 && target.Length > 0)
                        usings.Add(new UsingDirective(target, alias));
                }
                else
                {
                    usings.Add(new UsingDirective(value, null));
                }
            }
        }
        return usings;
    }
    
    internal static string? ParseNamespaceDirective(SourceText sourceText)
    {
        foreach (var line in sourceText.Lines)
        {
            var lineText = line.ToString().TrimStart();
            if (lineText.StartsWith("@namespace ", StringComparison.Ordinal))
            {
                var value = lineText.Substring("@namespace ".Length).Trim();
                if (value.Length > 0) return value;
            }
        }
        return null;
    }

    internal static List<string> ParsePageDirectives(SourceText sourceText)
    {
        var routes = new List<string>();
        foreach (var line in sourceText.Lines)
        {
            var lineText = line.ToString().TrimStart();
            if (lineText.StartsWith("@page ", StringComparison.Ordinal))
            {
                var value = lineText.Substring("@page ".Length).Trim();
                if (value.StartsWith("\"", StringComparison.Ordinal) && value.EndsWith("\"", StringComparison.Ordinal))
                    value = value.Substring(1, value.Length - 2);
                
                if (value.Length > 0) routes.Add(value);
            }
        }
        return routes;
    }

    internal static string? ExtractModelType(string baseType)
    {
        var openAngle = baseType.IndexOf('<');
        if (openAngle < 0) return null;
        var closeAngle = baseType.LastIndexOf('>');
        if (closeAngle <= openAngle) return null;
        var modelType = baseType.Substring(openAngle + 1, closeAngle - openAngle - 1).Trim();
        return modelType.Length > 0 ? modelType : null;
    }

    internal static string ExtractBaseTypeName(string baseType)
    {
        var openAngle = baseType.IndexOf('<');
        return openAngle >= 0 ? baseType.Substring(0, openAngle).Trim() : baseType.Trim();
    }
}

internal readonly struct UsingDirective(string namespaceOrType, string? alias) : IEquatable<UsingDirective>
{
    public string NamespaceOrType { get; } = namespaceOrType;
    public string? Alias { get; } = alias;

    public bool Equals(UsingDirective other) =>
        string.Equals(NamespaceOrType, other.NamespaceOrType, StringComparison.Ordinal) &&
        string.Equals(Alias, other.Alias, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is UsingDirective other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            return (NamespaceOrType.GetHashCode() * 397) ^ (Alias?.GetHashCode() ?? 0);
        }
    }
}
