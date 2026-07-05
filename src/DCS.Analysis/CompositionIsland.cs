namespace DCS.Analysis;

/// <summary>
/// Host-scoped composition island for multi-root C# applications (ADR-010).
/// </summary>
public enum CompositionIsland
{
    Unknown,
    Desktop,
    Api,
    Lambda,
    External
}

/// <summary>
/// Infers composition island from registration source file path.
/// </summary>
public static class CompositionIslandAttribution
{
    public static CompositionIsland InferFromFilePath(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return CompositionIsland.Unknown;

        var normalized = filePath.Replace('\\', '/').ToLowerInvariant();

        if (normalized.Contains("trackdub.worker", StringComparison.Ordinal) ||
            normalized.Contains("trackdub.sdk", StringComparison.Ordinal) && normalized.Contains("program.cs", StringComparison.Ordinal))
            return CompositionIsland.External;

        if (normalized.Contains("trackdub.webhookdelivery", StringComparison.Ordinal) ||
            normalized.EndsWith("/function.cs", StringComparison.Ordinal))
            return CompositionIsland.Lambda;

        if (normalized.Contains("trackdub.api", StringComparison.Ordinal))
            return CompositionIsland.Api;

        if (normalized.Contains("trackdub.composition", StringComparison.Ordinal) ||
            normalized.Contains("trackdub.app.avalonia", StringComparison.Ordinal) ||
            normalized.Contains("trackdub.app/", StringComparison.Ordinal))
            return CompositionIsland.Desktop;

        if (normalized.Contains("trackdub.cli", StringComparison.Ordinal) ||
            normalized.Contains("trackdub.benchmarks", StringComparison.Ordinal))
            return CompositionIsland.External;

        return CompositionIsland.Unknown;
    }

    public static string ToAnnotationValue(CompositionIsland island) => island switch
    {
        CompositionIsland.Desktop => "desktop",
        CompositionIsland.Api => "api",
        CompositionIsland.Lambda => "lambda",
        CompositionIsland.External => "external",
        _ => "unknown"
    };

    public static CompositionIsland FromAnnotationValue(string? value) => value?.ToLowerInvariant() switch
    {
        "desktop" => CompositionIsland.Desktop,
        "api" => CompositionIsland.Api,
        "lambda" => CompositionIsland.Lambda,
        "external" => CompositionIsland.External,
        _ => CompositionIsland.Unknown
    };

    public static bool MatchesFilter(CompositionIsland island, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter) || string.Equals(filter, "all", StringComparison.OrdinalIgnoreCase))
            return true;

        return string.Equals(filter, ToAnnotationValue(island), StringComparison.OrdinalIgnoreCase);
    }
}
