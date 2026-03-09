using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;

namespace CosmoApiServer.SourceGenerator;

internal static class ModelTypeResolver
{
    private static readonly Dictionary<string, string> PrimitiveTypeMap = new(StringComparer.Ordinal)
    {
        { "bool", "global::System.Boolean" },
        { "byte", "global::System.Byte" },
        { "sbyte", "global::System.SByte" },
        { "char", "global::System.Char" },
        { "decimal", "global::System.Decimal" },
        { "double", "global::System.Double" },
        { "float", "global::System.Single" },
        { "int", "global::System.Int32" },
        { "uint", "global::System.UInt32" },
        { "long", "global::System.Int64" },
        { "ulong", "global::System.UInt64" },
        { "short", "global::System.Int16" },
        { "ushort", "global::System.UInt16" },
        { "string", "global::System.String" },
        { "object", "global::System.Object" },
    };

    private static readonly string[] ImplicitNamespaces =
    [
        "System",
        "System.Collections.Generic",
        "System.Linq",
        "System.Threading.Tasks",
        "CosmoApiServer.Core.Templates",
    ];

    internal static string? ResolveModelType(string modelTypeName, List<UsingDirective> usingDirectives, Compilation compilation, string? rootNamespace = null)
    {
        return ResolveTypeExpression(modelTypeName.Trim(), usingDirectives, compilation, rootNamespace);
    }

    internal static string? ResolveModelTypeFromSliceBaseType(string baseTypeName, List<UsingDirective> usingDirectives, Compilation compilation, string? rootNamespace = null)
    {
        var trimmedBaseTypeName = baseTypeName.Trim();
        if (trimmedBaseTypeName.StartsWith("global::", StringComparison.Ordinal))
            trimmedBaseTypeName = trimmedBaseTypeName.Substring("global::".Length);

        var baseTypeSymbol = ResolveTypeSymbolExpression(trimmedBaseTypeName, usingDirectives, compilation, rootNamespace) as INamedTypeSymbol;
        if (baseTypeSymbol is null) return null;

        for (var currentType = baseTypeSymbol; currentType is not null; currentType = currentType.BaseType)
        {
            if (!string.Equals(currentType.Name, "RazorSlice", StringComparison.Ordinal) ||
                !string.Equals(currentType.ContainingNamespace.ToDisplayString(), "CosmoApiServer.Core.Templates", StringComparison.Ordinal))
            {
                continue;
            }

            if (currentType.IsGenericType && currentType.TypeArguments.Length == 1)
            {
                return "global::" + currentType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat
                    .WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.OmittedAsContaining));
            }
            return null;
        }
        return null;
    }

    private static string? ResolveTypeExpression(string typeName, List<UsingDirective> usingDirectives, Compilation compilation, string? rootNamespace)
    {
        if (typeName.EndsWith("?", StringComparison.Ordinal))
        {
            var innerType = typeName.Substring(0, typeName.Length - 1).Trim();
            var resolved = ResolveTypeExpression(innerType, usingDirectives, compilation, rootNamespace);
            return resolved != null ? resolved + "?" : null;
        }

        if (typeName.EndsWith("]", StringComparison.Ordinal))
        {
            var bracketStart = FindArrayBracketStart(typeName);
            if (bracketStart >= 0)
            {
                var elementType = typeName.Substring(0, bracketStart).Trim();
                var arraySuffix = typeName.Substring(bracketStart);
                var resolved = ResolveTypeExpression(elementType, usingDirectives, compilation, rootNamespace);
                return resolved != null ? resolved + arraySuffix : null;
            }
        }

        var genericOpen = FindTopLevelGenericOpen(typeName);
        if (genericOpen >= 0)
            return ResolveGenericType(typeName, genericOpen, usingDirectives, compilation, rootNamespace);

        return ResolveSimpleType(typeName, usingDirectives, compilation, rootNamespace: rootNamespace);
    }

    private static ITypeSymbol? ResolveTypeSymbolExpression(string typeName, List<UsingDirective> usingDirectives, Compilation compilation, string? rootNamespace)
    {
        if (typeName.EndsWith("?", StringComparison.Ordinal))
        {
            var innerType = typeName.Substring(0, typeName.Length - 1).Trim();
            return ResolveTypeSymbolExpression(innerType, usingDirectives, compilation, rootNamespace);
        }

        if (typeName.EndsWith("]", StringComparison.Ordinal))
        {
            var bracketStart = FindArrayBracketStart(typeName);
            if (bracketStart >= 0)
            {
                var elementType = typeName.Substring(0, bracketStart).Trim();
                var elementSymbol = ResolveTypeSymbolExpression(elementType, usingDirectives, compilation, rootNamespace);
                if (elementSymbol is null) return null;

                var arraySuffix = typeName.Substring(bracketStart);
                int rank = 1;
                for (int i = 0; i < arraySuffix.Length; i++)
                    if (arraySuffix[i] == ',') rank++;

                return compilation.CreateArrayTypeSymbol(elementSymbol, rank);
            }
        }

        var genericOpen = FindTopLevelGenericOpen(typeName);
        if (genericOpen >= 0)
        {
            var outerType = typeName.Substring(0, genericOpen).Trim();
            var genericClose = typeName.LastIndexOf('>');
            if (genericClose <= genericOpen) return null;

            var argsString = typeName.Substring(genericOpen + 1, genericClose - genericOpen - 1);
            var args = SplitGenericArguments(argsString);

            var metadataName = outerType + "`" + args.Count;
            var resolvedOuter = ResolveSimpleType(outerType, usingDirectives, compilation, metadataName, stripGenericParams: true, rootNamespace: rootNamespace);
            if (resolvedOuter is null) return null;

            var outerSymbol = ResolveNamedTypeSymbol(resolvedOuter + "`" + args.Count, compilation);
            if (outerSymbol is null) return null;

            var resolvedArgs = new ITypeSymbol[args.Count];
            for (int i = 0; i < args.Count; i++)
            {
                var resolvedArg = ResolveTypeSymbolExpression(args[i].Trim(), usingDirectives, compilation, rootNamespace);
                if (resolvedArg is null) return null;
                resolvedArgs[i] = resolvedArg;
            }
            return outerSymbol.Construct(resolvedArgs);
        }

        var resolvedSimple = ResolveSimpleType(typeName, usingDirectives, compilation, rootNamespace: rootNamespace);
        if (resolvedSimple is null) return null;
        return ResolveNamedTypeSymbol(resolvedSimple, compilation);
    }

    private static string? ResolveGenericType(string typeName, int genericOpen, List<UsingDirective> usingDirectives, Compilation compilation, string? rootNamespace)
    {
        var outerType = typeName.Substring(0, genericOpen).Trim();
        var genericClose = typeName.LastIndexOf('>');
        if (genericClose <= genericOpen) return null;

        var argsString = typeName.Substring(genericOpen + 1, genericClose - genericOpen - 1);
        var args = SplitGenericArguments(argsString);

        var resolvedArgs = new List<string>();
        foreach (var arg in args)
        {
            var resolved = ResolveTypeExpression(arg.Trim(), usingDirectives, compilation, rootNamespace);
            if (resolved is null) return null;
            resolvedArgs.Add(resolved);
        }

        var metadataName = outerType + "`" + args.Count;
        var resolvedOuter = ResolveSimpleType(outerType, usingDirectives, compilation, metadataName, stripGenericParams: true, rootNamespace: rootNamespace);
        if (resolvedOuter is null) return null;

        var sb = new StringBuilder();
        sb.Append(resolvedOuter);
        sb.Append('<');
        for (int i = 0; i < resolvedArgs.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(resolvedArgs[i]);
        }
        sb.Append(">");
        return sb.ToString();
    }

    private static string? ResolveSimpleType(string typeName, List<UsingDirective> usingDirectives, Compilation compilation, string? metadataNameOverride = null, bool stripGenericParams = false, string? rootNamespace = null)
    {
        if (PrimitiveTypeMap.TryGetValue(typeName, out var primitiveType)) return primitiveType;

        var dotIndex = typeName.IndexOf('.');
        if (dotIndex > 0)
        {
            var prefix = typeName.Substring(0, dotIndex);
            var suffix = typeName.Substring(dotIndex + 1);
            foreach (var ud in usingDirectives)
            {
                if (ud.Alias != null && string.Equals(ud.Alias, prefix, StringComparison.Ordinal))
                {
                    var expandedType = ud.NamespaceOrType + "." + suffix;
                    string? expandedLookup = null;
                    if (metadataNameOverride != null)
                    {
                        var metaDotIndex = metadataNameOverride.IndexOf('.');
                        var metaSuffix = metaDotIndex >= 0 ? metadataNameOverride.Substring(metaDotIndex + 1) : metadataNameOverride;
                        expandedLookup = ud.NamespaceOrType + "." + metaSuffix;
                    }
                    var resolved = TryResolveViaCompilation(expandedType, expandedLookup, compilation, stripGenericParams);
                    if (resolved != null) return resolved;
                    resolved = TryResolveNestedType(expandedType, expandedLookup, compilation, stripGenericParams);
                    if (resolved != null) return resolved;
                }
            }
        }

        var result = TryResolveWithNamespaces(typeName, metadataNameOverride, usingDirectives, compilation, stripGenericParams, rootNamespace);
        if (result != null) return result;

        var parts = typeName.Split('.');
        for (int j = parts.Length - 1; j > 0; j--)
        {
            var outerPart = string.Join(".", parts, 0, j);
            var nestedPart = string.Join("+", parts, j, parts.Length - j);
            var candidateName = outerPart + "+" + nestedPart;
            string? candidateLookup = null;
            if (metadataNameOverride != null)
            {
                var arityIndex = metadataNameOverride.IndexOf('`');
                candidateLookup = arityIndex >= 0 ? candidateName + metadataNameOverride.Substring(arityIndex) : candidateName;
            }
            result = TryResolveWithNamespaces(candidateName, candidateLookup, usingDirectives, compilation, stripGenericParams, rootNamespace);
            if (result != null) return result;
        }
        return null;
    }

    private static string? TryResolveNestedType(string typeName, string? metadataNameOverride, Compilation compilation, bool stripGenericParams)
    {
        var parts = typeName.Split('.');
        for (int j = parts.Length - 1; j > 0; j--)
        {
            var namespacePart = string.Join(".", parts, 0, j);
            var nestedPart = string.Join("+", parts, j, parts.Length - j);
            var candidateName = namespacePart + "+" + nestedPart;
            string? candidateLookup = null;
            if (metadataNameOverride != null)
            {
                var metaParts = metadataNameOverride.Split('.');
                if (metaParts.Length == parts.Length)
                {
                    var metaNamespacePart = string.Join(".", metaParts, 0, j);
                    var metaNestedPart = string.Join("+", metaParts, j, metaParts.Length - j);
                    candidateLookup = metaNamespacePart + "+" + metaNestedPart;
                }
            }
            var resolved = TryResolveViaCompilation(candidateName, candidateLookup, compilation, stripGenericParams);
            if (resolved != null) return resolved;
        }
        return null;
    }

    private static string? TryResolveWithNamespaces(string typeName, string? metadataNameOverride, List<UsingDirective> usingDirectives, Compilation compilation, bool stripGenericParams, string? rootNamespace)
    {
        var lookupName = metadataNameOverride ?? typeName;
        var result = TryResolveViaCompilation(typeName, lookupName, compilation, stripGenericParams);
        if (result != null) return result;

        foreach (var ud in usingDirectives)
        {
            if (ud.Alias != null) continue;
            var candidateName = ud.NamespaceOrType + "." + typeName;
            var candidateLookup = metadataNameOverride != null ? ud.NamespaceOrType + "." + metadataNameOverride : candidateName;
            result = TryResolveViaCompilation(candidateName, candidateLookup, compilation, stripGenericParams);
            if (result != null) return result;
        }

        foreach (var implicitNs in ImplicitNamespaces)
        {
            var candidateName = implicitNs + "." + typeName;
            var candidateLookup = metadataNameOverride != null ? implicitNs + "." + metadataNameOverride : candidateName;
            result = TryResolveViaCompilation(candidateName, candidateLookup, compilation, stripGenericParams);
            if (result != null) return result;
        }

        if (!string.IsNullOrEmpty(rootNamespace))
        {
            var candidateName = rootNamespace + "." + typeName;
            var candidateLookup = metadataNameOverride != null ? rootNamespace + "." + metadataNameOverride : candidateName;
            result = TryResolveViaCompilation(candidateName, candidateLookup, compilation, stripGenericParams);
            if (result != null) return result;
        }
        return null;
    }

    private static string? TryResolveViaCompilation(string displayName, string? metadataName, Compilation compilation, bool stripGenericParams = false)
    {
        var lookup = metadataName ?? displayName;
        var symbol = compilation.GetTypeByMetadataName(lookup);
        if (symbol != null)
        {
            var result = "global::" + symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat
                .WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.OmittedAsContaining));
            if (stripGenericParams)
            {
                var angleIndex = result.IndexOf('<');
                if (angleIndex >= 0) result = result.Substring(0, angleIndex);
            }
            return result;
        }
        return null;
    }

    private static INamedTypeSymbol? ResolveNamedTypeSymbol(string fullyQualifiedTypeName, Compilation compilation)
    {
        const string GlobalPrefix = "global::";
        var metadataName = fullyQualifiedTypeName.StartsWith(GlobalPrefix, StringComparison.Ordinal)
            ? fullyQualifiedTypeName.Substring(GlobalPrefix.Length) : fullyQualifiedTypeName;

        var symbol = compilation.GetTypeByMetadataName(metadataName);
        if (symbol is not null) return symbol;

        var parts = metadataName.Split('.');
        for (int j = parts.Length - 1; j > 0; j--)
        {
            var namespacePart = string.Join(".", parts, 0, j);
            var nestedPart = string.Join("+", parts, j, parts.Length - j);
            var candidateName = namespacePart + "+" + nestedPart;
            symbol = compilation.GetTypeByMetadataName(candidateName);
            if (symbol is not null) return symbol;
        }
        return null;
    }

    private static List<string> SplitGenericArguments(string args)
    {
        var result = new List<string>();
        int angleDepth = 0, squareDepth = 0, parenDepth = 0, start = 0;
        for (int i = 0; i < args.Length; i++)
        {
            var c = args[i];
            if (c == '<') angleDepth++;
            else if (c == '>') angleDepth--;
            else if (c == '[') squareDepth++;
            else if (c == ']') squareDepth--;
            else if (c == '(') parenDepth++;
            else if (c == ')') parenDepth--;
            else if (c == ',' && angleDepth == 0 && squareDepth == 0 && parenDepth == 0)
            {
                result.Add(args.Substring(start, i - start));
                start = i + 1;
            }
        }
        result.Add(args.Substring(start));
        return result;
    }

    private static int FindArrayBracketStart(string typeName)
    {
        int depth = 0;
        for (int i = typeName.Length - 1; i >= 0; i--)
        {
            if (typeName[i] == ']') depth++;
            else if (typeName[i] == '[')
            {
                depth--;
                if (depth == 0) return i;
            }
            else if (depth == 0) return -1;
        }
        return -1;
    }

    private static int FindTopLevelGenericOpen(string typeName)
    {
        for (int i = 0; i < typeName.Length; i++) if (typeName[i] == '<') return i;
        return -1;
    }
}
