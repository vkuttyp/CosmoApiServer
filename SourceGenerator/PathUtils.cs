using System;
using System.IO;

namespace CosmoApiServer.SourceGenerator;

internal static class PathUtils
{
    public static string GetRelativePath(string relativeTo, string path)
    {
        if (string.IsNullOrEmpty(relativeTo)) return path;
        
        var uri = new Uri(path);
        var relativeToUri = new Uri(relativeTo.EndsWith(Path.DirectorySeparatorChar.ToString()) ? relativeTo : relativeTo + Path.DirectorySeparatorChar);
        return Uri.UnescapeDataString(relativeToUri.MakeRelativeUri(uri).ToString().Replace('/', Path.DirectorySeparatorChar));
    }
}
