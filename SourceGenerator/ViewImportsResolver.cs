using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace CosmoApiServer.SourceGenerator;

internal static class ViewImportsResolver
{
    internal record Directives(string? InheritsDirective, List<UsingDirective> UsingDirectives, string? NamespaceDirective, List<InjectDirective> InjectDirectives);

    public static ImmutableDictionary<string, AdditionalText> BuildViewImportsMap(ImmutableArray<AdditionalText> allCshtmlFiles)
    {
        return allCshtmlFiles
            .Where(f => Path.GetFileName(f.Path).Equals("_ViewImports.cshtml", StringComparison.OrdinalIgnoreCase))
            .ToImmutableDictionary(f => Path.GetDirectoryName(f.Path), f => f);
    }

    public static Directives ResolveDirectives(string filePath, string projectDirectory, ImmutableDictionary<string, AdditionalText> viewImportsMap, SourceText sliceText)
    {
        var inherits = RazorDirectiveParser.ParseInheritsDirective(sliceText);
        var usings = RazorDirectiveParser.ParseUsingDirectives(sliceText);
        var ns = RazorDirectiveParser.ParseNamespaceDirective(sliceText);
        var injects = RazorDirectiveParser.ParseInjectDirectives(sliceText);

        var currentDir = Path.GetDirectoryName(filePath);
        while (currentDir != null && currentDir.StartsWith(projectDirectory))
        {
            if (viewImportsMap.TryGetValue(currentDir, out var viewImportsFile))
            {
                var viewImportsText = viewImportsFile.GetText();
                if (viewImportsText != null)
                {
                    inherits ??= RazorDirectiveParser.ParseInheritsDirective(viewImportsText);
                    ns ??= RazorDirectiveParser.ParseNamespaceDirective(viewImportsText);
                    usings.AddRange(RazorDirectiveParser.ParseUsingDirectives(viewImportsText));
                    injects.AddRange(RazorDirectiveParser.ParseInjectDirectives(viewImportsText));
                }
            }
            currentDir = Path.GetDirectoryName(currentDir);
        }

        return new Directives(inherits, usings, ns, injects);
    }
}
