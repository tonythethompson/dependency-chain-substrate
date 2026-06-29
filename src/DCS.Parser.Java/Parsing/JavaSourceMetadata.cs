using DCS.Core.IR;

namespace DCS.Parser.Java.Parsing;

public static class JavaSourceMetadata
{
    public static string InferModuleId(string filePath, string rootPath)
    {
        var relative = Path.GetRelativePath(rootPath, filePath).Replace('\\', '/');
        var parts = relative.Split('/');
        if (parts.Length >= 3 && parts[1] == "src")
            return parts[0];

        return "*";
    }

    public static SourceSetKind InferSourceSet(string filePath)
    {
        var normalized = filePath.Replace('\\', '/');
        if (normalized.Contains("/src/test/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("src/test/java", StringComparison.OrdinalIgnoreCase))
            return SourceSetKind.Test;

        if (normalized.Contains("/src/main/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("src/main/java", StringComparison.OrdinalIgnoreCase))
            return SourceSetKind.Main;

        if (normalized.Contains("/generated", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("target/generated", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("build/generated", StringComparison.OrdinalIgnoreCase))
            return SourceSetKind.Generated;

        return SourceSetKind.Unknown;
    }

    public static bool MatchesFilter(SourceSetKind kind, SourceSetFilter filter) =>
        kind switch
        {
            SourceSetKind.Main => filter.HasFlag(SourceSetFilter.Main),
            SourceSetKind.Test => filter.HasFlag(SourceSetFilter.Test),
            SourceSetKind.Generated => filter.HasFlag(SourceSetFilter.Generated),
            _ => filter.HasFlag(SourceSetFilter.Unknown)
        };
}
